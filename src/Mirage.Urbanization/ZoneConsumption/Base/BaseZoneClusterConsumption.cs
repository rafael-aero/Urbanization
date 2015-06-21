﻿using System;
using System.Collections.Generic;
using System.Linq;
using Mirage.Urbanization.ZoneConsumption.Base.Behaviours;

namespace Mirage.Urbanization.ZoneConsumption.Base
{
    public abstract class BaseZoneClusterConsumption : IAreaZoneClusterConsumption
    {
        private readonly IElectricityBehaviour _electricityBehaviour;

        public abstract char KeyChar { get; }

        public abstract int Cost { get; }

        public abstract IPollutionBehaviour PollutionBehaviour { get; }
        public abstract ICrimeBehaviour CrimeBehaviour { get; }
        public IElectricityBehaviour ElectricityBehaviour { get { return _electricityBehaviour; } }

        public abstract int Value { get; }

        public abstract bool LandValueAffectedBySurroundings { get; }

        public int CellValue
        {
            get
            {
                if (Value > 0 && ZoneClusterMembers.Count > 0)
                    return Value / ZoneClusterMembers.Count;
                else
                {
                    return 0;
                }
            }
        }

        protected BaseZoneClusterConsumption(IElectricityBehaviour electricityBehaviour)
        {
            _electricityBehaviour = electricityBehaviour;
        }

        public bool HasPower
        {
            get { return _electricityBehaviour.IsPowered; }
        }

        public abstract string Name { get; }
        public abstract IReadOnlyCollection<ZoneClusterMemberConsumption> ZoneClusterMembers { get; }
        public bool ClusterMembersAreUnlocked { get; private set; }

        private readonly DateTime _dateTimeCreated = DateTime.Now;

        protected DateTime DateTimeCreated { get { return _dateTimeCreated; } }

        private readonly object _clusterMemberLocker = new object();

        public void WithUnlockedClusterMembers(Action action)
        {
            lock (_clusterMemberLocker)
            {
                if (ClusterMembersAreUnlocked) throw new InvalidOperationException();
                ClusterMembersAreUnlocked = true;
                try
                {
                    action();
                }
                catch
                {
                    ClusterMembersAreUnlocked = false;
                    throw;
                }
            }
        }
    }
}