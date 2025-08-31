using System.Collections.Generic;
using System.Text;
using Sandbox.Definitions;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using TSUT.HeatManagement;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Utils;
using static TSUT.HeatManagement.HmsApi;

namespace TSUT.H2Real
{
    public class GasGeneratorHandler : AHeatBehavior
    {
        HmsApi _api;
        IMyGasGenerator _generator;
        const int ONE_MILLION = 1000000;

        public GasGeneratorHandler(IMyGasGenerator block, HmsApi api) : base(block)
        {
            _generator = block;
            _api = api;
            _generator.AppendingCustomInfo += AppendHeatInfo;
            _generator.RefreshCustomInfo();
            _generator.SetDetailedInfoDirty();
        }

        public override void Cleanup()
        {
            _generator.AppendingCustomInfo -= AppendHeatInfo;
        }

        private void AppendHeatInfo(IMyTerminalBlock block, StringBuilder builder)
        {
            if (!(block is IMyGasGenerator))
                return;
            var generator = block as IMyGasGenerator;
            var def = generator.SlimBlock.BlockDefinition as MyOxygenGeneratorDefinition;
            float currentHeat = _api.Utils.GetHeat(generator);
            float blockCapacity = _api.Utils.GetThermalCapacity(generator);

            float standardConsumption = 0f;
            float meltingPower = 0f;
            float compressionPower = 0f;
            float processedIceKg = 0f;
            float temperatureChange = 0f;
            float neighborExchange;
            float networkExchange;

            var neighborInfo = new StringBuilder();

            AddNeighborAndNetworksInfo(
                block,
                _api,
                neighborInfo,
                out neighborExchange,
                out networkExchange
            );

            if (generator.IsProducing)
            {
                standardConsumption = def != null ? def.OperationalPowerConsumption : 0f;

                processedIceKg = CalculateMelting(1, ref meltingPower, ref temperatureChange);
                compressionPower = CalculateCompression(processedIceKg);
            }

            float heatChange = GetHeatChange(1f) + neighborExchange + networkExchange; // Assuming deltaTime of 1 second for display purposes

            Dictionary<MyDefinitionId, float> products = new Dictionary<MyDefinitionId, float>();
            foreach (var item in def.ProducedGases)
            {
                products.Add(item.Id, item.IceToGasRatio * processedIceKg);
            }
            builder.AppendLine("--- HMS.H2Real ---");
            builder.AppendLine($"Temperature: {currentHeat:F2} °C");
            string heatStatus = heatChange > 0 ? "Heating" : heatChange < -0.01 ? "Cooling" : "Stable";
            builder.AppendLine($"Thermal Status: {heatStatus}");
            builder.AppendLine($"Net Heat Change: {heatChange:+0.00;-0.00;0.00} °C/s");
            builder.AppendLine($"Thermal Capacity: {blockCapacity / ONE_MILLION:F2} MJ/°C");
            builder.AppendLine("");
            builder.AppendLine("Production:");
            builder.AppendLine($"  Ice consumption: {processedIceKg:F2} kg/s");
            foreach (var kvp in products)
            {
                var name = GetGasDisplayName(kvp.Key);
                builder.AppendLine($"  {name} production: {kvp.Value:F2} L/s");
            }
            builder.AppendLine("");
            builder.AppendLine("Power consumtion:");
            builder.AppendLine($"  Electrolisys: {standardConsumption:F2} MW");
            builder.AppendLine($"  Melting: {meltingPower:F2} MW");
            builder.AppendLine($"  Compressing: {compressionPower:F2} MW");
            builder.AppendLine($"Total: {(standardConsumption + meltingPower + compressionPower):F2} MW");
            builder.AppendLine("");
            builder.AppendLine("Heat sources:");
            builder.AppendLine($"  Internal use: {temperatureChange:F2} °C/s");
            builder.AppendLine($"  Air Exchange: {-_api.Utils.GetAmbientHeatLoss(block, 1):+0.00;-0.00;0.00} °C/s");
            builder.Append(neighborInfo);
        }

        string GetGasDisplayName(MyDefinitionId gasId)
        {
            var resourceName = gasId.SubtypeName;
            string locKey = $"DisplayName_Item_{resourceName}";
            return MyTexts.GetString(MyStringId.GetOrCompute(locKey));
        }

        public override float GetHeatChange(float deltaTime)
        {
            if (!_generator.IsWorking)
                return 0;

            var sink = _generator.Components.Get<MyResourceSinkComponent>();
            if (sink == null)
            {
                return 0;
            }

            var def = _generator.SlimBlock.BlockDefinition as MyOxygenGeneratorDefinition;
            if (def == null)
                return 0f;

            var standardConsumption =  _generator.IsProducing ? def.OperationalPowerConsumption : 0;

            if (_generator.DisplayNameText.Contains("debug"))
            {
                // MyLog.Default.WriteLine($"[H2Real] Start processing {_generator.DisplayNameText} with {_api.Utils.GetHeat(_generator)}C ");
            }

            float extraPower = 0; // MW
            float usedTemperature = 0;
            // Add power consumption for melting or consume stored heat energy
            float processedIceKg = CalculateMelting(deltaTime, ref extraPower, ref usedTemperature);

            if (_generator.DisplayNameText.Contains("debug"))
            {
                // MyLog.Default.WriteLine($"[H2Real] Ice processing: {usedTemperature:F4}");
            }

            // Add power consumption for compressing
            extraPower += CalculateCompression(processedIceKg);

            usedTemperature -= _api.Utils.GetAmbientHeatLoss(_generator, deltaTime);

            if (_generator.DisplayNameText.Contains("debug"))
            {
                // MyLog.Default.WriteLine($"[H2Real] Amb exchange: {usedTemperature:F4}");
            }

            var previous = sink.RequiredInputByType(MyResourceDistributorComponent.ElectricityId);
            var current = standardConsumption + extraPower;
            if (current != previous)
            {
                sink.SetRequiredInputByType(MyResourceDistributorComponent.ElectricityId, current);
            }

            if (_generator.DisplayNameText.Contains("debug"))
            {
                // MyLog.Default.WriteLine($"[H2Real] Internal temperature change: {usedTemperature:F4}");
            }

            return usedTemperature;
        }

        private float CalculateCompression(float processedIceKg)
        {
            var def = _generator.SlimBlock.BlockDefinition as MyOxygenGeneratorDefinition;
            if (def == null)
                return 0f;

            float energy = 0;
            foreach (var gas in def.ProducedGases)
            {
                float tanksFilled = GetConnectedHydrogenFill(gas.Id.SubtypeName);
                float production = gas.IceToGasRatio * processedIceKg;
                energy += production * Config.Instance.GAS_COMPRESSION_POWER_FULL_PER_LITER * tanksFilled / ONE_MILLION;
            }

            return energy;
        }

        private float CalculateMelting(float deltaTime, ref float extraPower, ref float usedTemperature)
        {
            if (!_generator.IsProducing)
                return 0;

            var def = _generator.SlimBlock.BlockDefinition as MyOxygenGeneratorDefinition;
            if (def == null)
                return 0f;

            float currentTemp = _api.Utils.GetHeat(_generator);
            float blockCapacity = _api.Utils.GetThermalCapacity(_generator);

            float storedEnergy = currentTemp * blockCapacity;
            float processedIceKg = def.IceConsumptionPerSecond * deltaTime;
            float neededJoules = processedIceKg * Config.Instance.ICE_MELTING_ENERGY_PER_KG;
            if (storedEnergy > neededJoules)
            {
                usedTemperature -= neededJoules / blockCapacity;
            }
            else
            {
                if (storedEnergy > 0)
                {
                    neededJoules -= storedEnergy;
                }
                usedTemperature -= storedEnergy / blockCapacity;
                extraPower += neededJoules / ONE_MILLION; // Conversion to MJ
            }

            return processedIceKg;
        }

        public override void ReactOnNewHeat(float heat)
        {
            _generator.SetDetailedInfoDirty();
            _generator.RefreshCustomInfo();
        }

        public override void SpreadHeat(float deltaTime)
        {
            float newTemp = SpreadHeatStandard(deltaTime, _generator, _api);
            ReactOnNewHeat(newTemp);
        }

        private float GetConnectedHydrogenFill(string type)
        {
            // Get the terminal system for the grid this generator is on
            var terminalSystem = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(_generator.CubeGrid);

            if (terminalSystem == null)
                return 0f;

            var tanks = new List<IMyGasTank>();
            terminalSystem.GetBlocksOfType(tanks, t =>
                t.BlockDefinition.SubtypeId.Contains(type) // Filter only H2 tanks
            );

            double totalCapacity = 0;
            double totalStored = 0;

            foreach (var tank in tanks)
            {
                if (tank.IsWorking)
                {
                    double capacity = tank.Capacity;
                    double filled = capacity * tank.FilledRatio;

                    totalCapacity += capacity;
                    totalStored += filled;
                }
            }

            if (totalCapacity <= 0)
                return 0f;

            return (float)(totalStored / totalCapacity); // Percent (0.0f - 1.0f)
        }
    }
}