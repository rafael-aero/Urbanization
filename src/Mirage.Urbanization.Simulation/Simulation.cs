﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Permissions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Mirage.Urbanization.Persistence;
using Mirage.Urbanization.Simulation.Persistence;
using Mirage.Urbanization.Statistics;
using Mirage.Urbanization.ZoneConsumption.Base;

namespace Mirage.Urbanization.Simulation
{
    public static class MiscCityStatisticsExtensions
    {
        public static PersistedCityStatistics Convert(this ICityStatistics cityStatistics)
        {
            return new PersistedCityStatistics
            {
                CrimeNumbers = new PersistedNumberSummary(cityStatistics.MiscCityStatistics.CrimeNumbers),
                PollutionNumbers = new PersistedNumberSummary(cityStatistics.MiscCityStatistics.PollutionNumbers),
                LandValueNumbers = new PersistedNumberSummary(cityStatistics.MiscCityStatistics.LandValueNumbers),
                TrafficNumbers = new PersistedNumberSummary(cityStatistics.GrowthZoneStatistics.RoadInfrastructureStatistics.TrafficNumbers),

                NumberOfRoadZones = cityStatistics.GrowthZoneStatistics.RoadInfrastructureStatistics.NumberOfRoadZones,

                NumberOfRailRoadZones = cityStatistics.GrowthZoneStatistics.RailroadInfrastructureStatistics.NumberOfRailRoadZones,
                NumberOfTrainStations = cityStatistics.GrowthZoneStatistics.RailroadInfrastructureStatistics.NumberOfTrainStations,

                PowerAmountOfConsumers = cityStatistics.PowerGridStatistics.PowerGridNetworkStatistics.Sum(x => x.AmountOfConsumers),
                PowerAmountOfSuppliers = cityStatistics.PowerGridStatistics.PowerGridNetworkStatistics.Sum(x => x.AmountOfSuppliers),
                PowerConsumptionInUnits = cityStatistics.PowerGridStatistics.PowerGridNetworkStatistics.Sum(x => x.ConsumptionInUnits),
                PowerSupplyInUnits = cityStatistics.PowerGridStatistics.PowerGridNetworkStatistics.Sum(x => x.SupplyInUnits),

                CommercialZonePopulationStatistics = new PersistedNumberSummary(cityStatistics.GrowthZoneStatistics.CommercialZonePopulationNumbers),
                IndustrialZonePopulationStatistics = new PersistedNumberSummary(cityStatistics.GrowthZoneStatistics.IndustrialZonePopulationNumbers),
                ResidentialZonePopulationStatistics = new PersistedNumberSummary(cityStatistics.GrowthZoneStatistics.ResidentialZonePopulationNumbers),
                GlobalZonePopulationStatistics = new PersistedNumberSummary(cityStatistics.GrowthZoneStatistics.GlobalZonePopulationNumbers),

                TimeCode = cityStatistics.TimeCode,
            };
        }
    }

    public class PersistedNumberSummary
    {
        public PersistedNumberSummary()
        {

        }

        public PersistedNumberSummary(INumberSummary summary)
        {
            Count = summary.Count;
            Sum = summary.Sum;
            Max = summary.Highest;
            Min = summary.Lowest;
            Average = summary.Average;
        }

        public int Count { get; set; }
        public int Sum { get; set; }
        public int Max { get; set; }
        public int Min { get; set; }
        public int Average { get; set; }
    }

    public class SimulationSession : ISimulationSession
    {
        private readonly IList<PersistedCityStatistics> _persistedCityStatistics = new List<PersistedCityStatistics>();

        private readonly Area _area;
        private readonly Task _growthSimulationTask, _crimeAndPollutionTask, _powerTask;
        private readonly YearAndMonth _yearAndMonth = new YearAndMonth();
        private IPowerGridStatistics _lastPowerGridStatistics;
        private IMiscCityStatistics _lastMiscCityStatistics;
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        public IReadOnlyArea Area { get { return _area; } }

        public IAreaConsumptionResult ConsumeZoneAt(IReadOnlyZoneInfo readOnlyZoneInfo, IAreaConsumption consumption)
        {
            return _area.ConsumeZoneAt(readOnlyZoneInfo, consumption);
        }

        private bool PowerAndMiscStatisticsLoaded
        {
            get { return (_lastPowerGridStatistics != null && _lastMiscCityStatistics != null); }
        }

        public SimulationSession(SimulationOptions simulationOptions)
        {
            _area = new Area(simulationOptions.GetAreaOptions());

            _area.OnAreaMessage += HandleAreaMessage;

            simulationOptions.WithPersistedSimulation(persistedSimulation =>
            {
                if (persistedSimulation.PersistedCityStatistics == null)
                    return;
                foreach (var x in persistedSimulation.PersistedCityStatistics)
                    _persistedCityStatistics.Add(x);

                _yearAndMonth.LoadTimeCode(persistedSimulation.PersistedCityStatistics.OrderByDescending(x => x.TimeCode).First().TimeCode);

                _yearAndMonth.AddWeek();
            });

            _growthSimulationTask = new Task(() =>
            {
                while (true)
                {
                    {
                        var onYearAndOrMonthChanged = OnYearAndOrMonthChanged;
                        if (onYearAndOrMonthChanged != null)
                            onYearAndOrMonthChanged(this, new EventArgsWithData<IYearAndMonth>(_yearAndMonth));
                    }
                    foreach (var iteration in Enumerable.Range(0, 4))
                    {
                        if (PowerAndMiscStatisticsLoaded)
                        {
                            var growthZoneStatistics = _area.PerformGrowthSimulationCycle(_cancellationTokenSource.Token).Result;
                            var onYearAndOrMonthChanged = OnYearAndOrMonthChanged;
                            if (onYearAndOrMonthChanged != null) onYearAndOrMonthChanged(this, new EventArgsWithData<IYearAndMonth>(_yearAndMonth));

                            var statistics = new CityStatistics(_yearAndMonth.TimeCode, _lastPowerGridStatistics, growthZoneStatistics, _lastMiscCityStatistics);
                            _persistedCityStatistics.Add(statistics.Convert());

                            var eventCapture = CityStatisticsUpdated;
                            if (eventCapture != null)
                                eventCapture(this, new CityStatisticsUpdatedEventArgs(statistics));
                            _yearAndMonth.AddWeek();
                        }
                        _cancellationTokenSource.Token.ThrowIfCancellationRequested();
                        Task.Delay(2000).Wait();
                    }
                }
            }, _cancellationTokenSource.Token);
            _powerTask = new Task(() =>
            {
                while (true)
                {
                    var stopwatch = new Stopwatch();

                    stopwatch.Start();
                    _cancellationTokenSource.Token.ThrowIfCancellationRequested();
                    _lastPowerGridStatistics = _area.CalculatePowergridStatistics(_cancellationTokenSource.Token).Result;
                    Console.WriteLine("Power grid scan completed. (Took: '{0}')", stopwatch.Elapsed);

                    Task.Delay(2000).Wait();
                }
            }, _cancellationTokenSource.Token);
            _crimeAndPollutionTask = new Task(() =>
            {
                while (true)
                {
                    var stopwatch = new Stopwatch();

                    stopwatch.Start();

                    _cancellationTokenSource.Token.ThrowIfCancellationRequested();
                    var queryCrimeResults = new HashSet<IQueryCrimeResult>(_area.EnumerateZoneInfos().Select(x => x.QueryCrime()).Where(x => x.HasMatch).Select(x => x.MatchingObject));

                    _cancellationTokenSource.Token.ThrowIfCancellationRequested();
                    var queryLandValueResults = new HashSet<IQueryLandValueResult>(_area.EnumerateZoneInfos().Select(x => x.QueryLandValue()).Where(x => x.HasMatch).Select(x => x.MatchingObject));

                    _cancellationTokenSource.Token.ThrowIfCancellationRequested();
                    var queryPollutionResults = new HashSet<IQueryPollutionResult>(_area.EnumerateZoneInfos().Select(x => x.QueryPollution()).Where(x => x.HasMatch).Select(x => x.MatchingObject));

                    _cancellationTokenSource.Token.ThrowIfCancellationRequested();

                    _lastMiscCityStatistics = new MiscCityStatistics(queryCrimeResults, queryLandValueResults, queryPollutionResults);

                    Console.WriteLine("Crime and pollution scan completed. (Took: '{0}')", stopwatch.Elapsed);

                    Task.Delay(2000).Wait();
                }
            }, _cancellationTokenSource.Token);
        }

        public void StartSimulation()
        {
            _powerTask.Start();
            _growthSimulationTask.Start();
            _crimeAndPollutionTask.Start();
        }

        public void Dispose()
        {
            Console.WriteLine("Killing simulation...");

            _cancellationTokenSource.Cancel();

            foreach (var task in new[] { _powerTask, _growthSimulationTask, _crimeAndPollutionTask })
            {
                try
                {
                    task.Wait();
                }
                catch (AggregateException aggEx)
                {
                    if (!aggEx.IsCancelled())
                        throw;
                }
            }
            Console.WriteLine("Simulation killed.");
        }

        public QueryResult<PersistedCityStatistics> GetRecentStatistics()
        {
            return new QueryResult<PersistedCityStatistics>(_persistedCityStatistics.OrderByDescending(x => x.TimeCode).FirstOrDefault());
        }

        public event EventHandler<EventArgsWithData<IYearAndMonth>> OnYearAndOrMonthChanged;

        public PersistedSimulation GeneratePersistedArea()
        {
            return new PersistedSimulation
            {
                PersistedArea = _area.GeneratePersistenceSnapshot(),
                PersistedCityStatistics = _persistedCityStatistics.ToArray()
            };
        }

        public event EventHandler<CityStatisticsUpdatedEventArgs> CityStatisticsUpdated;


        public IReadOnlyCollection<PersistedCityStatistics> GetAllCityStatistics()
        {
            return _persistedCityStatistics.ToList();
        }

        public event EventHandler<CityBudgetValueChangedEventArgs> OnCityBudgetValueChanged;
        public event EventHandler<AreaConsumptionResultEventArgs> OnAreaMessage;

        private void HandleAreaMessage(object sender, AreaConsumptionResultEventArgs e)
        {
            var onOnAreaMessage = OnAreaMessage;
            if (onOnAreaMessage == null)
                return;

            if (e.AreaConsumptionResult.Success)
            {
                var cost = e.AreaConsumptionResult.AreaConsumption.Cost;
                _cityBudget.Subtract(cost);

                var onCityBudgetValueChanged = OnCityBudgetValueChanged;
                if (onCityBudgetValueChanged != null)
                {
                    onCityBudgetValueChanged(this, new CityBudgetValueChangedEventArgs(_cityBudget.CurrentAmount));
                }
            }

            onOnAreaMessage(sender, e);
        }

        private readonly CityBudget _cityBudget = new CityBudget();
    }

    public class CityBudgetValueChangedEventArgs : EventArgs
    {
        private readonly int _newValue;

        public CityBudgetValueChangedEventArgs(int newValue)
        {
            _newValue = newValue;
        }

        public int NewValue { get { return _newValue; } }
    }

    internal class CityBudget
    {
        private int _currentAmount = 10000;

        public int CurrentAmount { get { return _currentAmount; } }

        public void Add(int amount)
        {
            Interlocked.Add(ref _currentAmount, amount);
        }

        public void Subtract(int amount)
        {
            Interlocked.Add(ref _currentAmount, -amount);
        }
    }
}
