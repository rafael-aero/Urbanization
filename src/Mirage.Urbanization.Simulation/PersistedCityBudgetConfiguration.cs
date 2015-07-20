﻿using System;

namespace Mirage.Urbanization.Simulation
{
    public class PersistedCityBudgetConfiguration : ICityBudgetConfiguration
    {
        public decimal ResidentialTaxRate { get; set; } = TaxDefinition.DefaultTaxRate;
        public decimal CommercialTaxRate { get; set; } = TaxDefinition.DefaultTaxRate;
        public decimal IndustrialTaxRate { get; set; } = TaxDefinition.DefaultTaxRate;
        public decimal PoliceServiceRate { get; set; } = 1M;
        public decimal FireDepartmentServiceRate { get; set; } = 1M;
        public decimal RoadInfrastructureServiceRate { get; set; } = 1M;
        public decimal RailroadInfrastructureServiceRate { get; set; } = 1M;
    }
}