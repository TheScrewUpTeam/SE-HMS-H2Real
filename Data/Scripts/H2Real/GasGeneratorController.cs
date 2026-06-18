using System.Collections.Generic;
using System.Text;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using TSUT.HeatManagement;
using VRage;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using static TSUT.HeatManagement.HmsApi;

namespace TSUT.H2Real
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_OxygenGenerator), false)]
    public class GasGeneratorController : AHmsBlockComponent
    {
        private IMyGasGenerator Generator => Entity as IMyGasGenerator;
        const int ONE_MILLION = 1000000;

        private readonly List<IMyGasTank> _connectedTanks = new List<IMyGasTank>();
        private bool _tanksDirty = true;

        private float _coldMeltingPower;
        private float _coldThresholdTemp;
        private bool _isHot;
        private float _lastCompressionPower;

        protected override void OnHmsInit()
        {
            var gen = Generator;
            var def = gen.SlimBlock.BlockDefinition as MyOxygenGeneratorDefinition;
            float blockCapacity = Api.Utils.GetThermalCapacity(gen);

            _coldMeltingPower = (def?.IceConsumptionPerSecond ?? 0f) * Config.Instance.ICE_MELTING_ENERGY_PER_KG / ONE_MILLION;
            _coldThresholdTemp = blockCapacity > 0f
                ? (def?.IceConsumptionPerSecond ?? 0f) * Config.Instance.ICE_MELTING_ENERGY_PER_KG / blockCapacity
                : float.MaxValue;
            _isHot = Api.Utils.GetHeat(gen) >= _coldThresholdTemp;
            _lastCompressionPower = 0f;

            UpdateMultiplier(0f);

            gen.AppendingCustomInfo += AppendHeatInfo;
            GridGasPressureCache.GetOrCreate(gen.CubeGrid).Register(this);
        }

        public override void Close()
        {
            var gen = Generator;
            if (gen != null)
            {
                gen.AppendingCustomInfo -= AppendHeatInfo;
                ((Sandbox.ModAPI.IMyGasGenerator)gen).PowerConsumptionMultiplier = 1f;
                GridGasPressureCache cache;
                if (GridGasPressureCache.TryGet(gen.CubeGrid, out cache))
                    cache.Unregister(this);
            }
            base.Close();
        }

        public override void OnDetachedFromHeatSystem() { }

        public void InvalidateTankCache()
        {
            _tanksDirty = true;
        }

        private void UpdateMultiplier(float compressionPower)
        {
            var gen = Generator;
            if (gen == null) return;
            var def = gen.SlimBlock.BlockDefinition as MyOxygenGeneratorDefinition;
            float standard = def?.OperationalPowerConsumption ?? 0f;
            if (standard <= 0f) return;
            float coldExtra = _isHot ? 0f : _coldMeltingPower;
            ((Sandbox.ModAPI.IMyGasGenerator)gen).PowerConsumptionMultiplier = (standard + coldExtra + compressionPower) / standard;
        }

        private void RebuildConnectedTanks()
        {
            _connectedTanks.Clear();
            var gen = Generator;
            if (gen == null) { _tanksDirty = false; return; }

            var genInv = gen.GetInventory(0);
            if (genInv == null) { _tanksDirty = false; return; }

            var termSystem = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(gen.CubeGrid);
            if (termSystem == null) { _tanksDirty = false; return; }

            var allTanks = new List<IMyGasTank>();
            termSystem.GetBlocksOfType(allTanks);
            foreach (var tank in allTanks)
            {
                var tankInv = tank.GetInventory(0);
                if (tankInv != null && genInv.IsConnectedTo(tankInv))
                    _connectedTanks.Add(tank);
            }
            _tanksDirty = false;
        }

        private void AppendHeatInfo(IMyTerminalBlock block, StringBuilder builder)
        {
            if (Api?.Utils == null) return;
            if (!(block is IMyGasGenerator))
                return;
            var generator = block as IMyGasGenerator;
            var def = generator.SlimBlock.BlockDefinition as MyOxygenGeneratorDefinition;
            float currentHeat = Api.Utils.GetHeat(generator);
            float blockCapacity = Api.Utils.GetThermalCapacity(generator);

            float standardConsumption = 0f;
            float meltingPower = 0f;
            float compressionPower = 0f;
            float processedIceKg = 0f;
            float temperatureChange = 0f;
            float neighborExchange;
            float networkExchange;

            var neighborInfo = new StringBuilder();

            AddNeighborAndNetworksInfo(
                neighborInfo,
                out neighborExchange,
                out networkExchange
            );

            if (generator.IsProducing)
            {
                standardConsumption = def != null ? def.OperationalPowerConsumption : 0f;
                processedIceKg = CalculateMelting(1, ref meltingPower, ref temperatureChange);
                compressionPower = CalculateCompression();
            }

            float heatChange = GetHeatChange(1f) + neighborExchange + networkExchange;

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
            builder.AppendLine($"  Air Exchange: {-Api.Utils.GetAmbientHeatLoss(block, 1):+0.00;-0.00;0.00} °C/s");
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
            if (!Generator.IsWorking)
                return 0;

            var def = Generator.SlimBlock.BlockDefinition as MyOxygenGeneratorDefinition;
            if (def == null)
                return 0f;

            if (Generator.DisplayNameText.Contains("debug"))
                MyLog.Default.WriteLine($"[H2Real] Start processing {Generator.DisplayNameText} with {Api.Utils.GetHeat(Generator)}C ");

            float unusedPower = 0;
            float usedTemperature = 0;
            float processedIceKg = CalculateMelting(deltaTime, ref unusedPower, ref usedTemperature);

            if (Generator.DisplayNameText.Contains("debug"))
                MyLog.Default.WriteLine($"[H2Real] Ice processing: {usedTemperature:F4}");

            usedTemperature -= Api.Utils.GetAmbientHeatLoss(Generator, deltaTime);

            if (Generator.DisplayNameText.Contains("debug"))
                MyLog.Default.WriteLine($"[H2Real] Amb exchange: {usedTemperature:F4} Internal temperature change: {usedTemperature:F4}");

            _lastCompressionPower = CalculateCompression();
            UpdateMultiplier(_lastCompressionPower);

            return usedTemperature;
        }

        private float CalculateCompression()
        {
            var def = Generator.SlimBlock.BlockDefinition as MyOxygenGeneratorDefinition;
            if (def == null || !Generator.IsProducing)
                return 0f;

            if (_tanksDirty)
                RebuildConnectedTanks();

            float power = 0;
            foreach (var gas in def.ProducedGases)
            {
                float tanksFilled = GetConnectedGasFill(gas.Id.SubtypeName);
                float productionRate = gas.IceToGasRatio * def.IceConsumptionPerSecond;
                power += productionRate * Config.Instance.GAS_COMPRESSION_POWER_FULL_PER_LITER * tanksFilled / 1000f;
            }

            return power;
        }

        private float GetConnectedGasFill(string gasSubtype)
        {
            double totalCapacity = 0;
            double totalStored = 0;
            foreach (var tank in _connectedTanks)
            {
                if (!tank.IsWorking || !tank.BlockDefinition.SubtypeId.Contains(gasSubtype))
                    continue;
                double capacity = tank.Capacity;
                totalCapacity += capacity;
                totalStored += capacity * tank.FilledRatio;
            }
            return totalCapacity <= 0 ? 0f : (float)(totalStored / totalCapacity);
        }

        private float CalculateMelting(float deltaTime, ref float extraPower, ref float usedTemperature)
        {
            if (!Generator.IsProducing)
                return 0;

            var def = Generator.SlimBlock.BlockDefinition as MyOxygenGeneratorDefinition;
            if (def == null)
                return 0f;

            float currentTemp = Api.Utils.GetHeat(Generator);
            float blockCapacity = Api.Utils.GetThermalCapacity(Generator);

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
                    neededJoules -= storedEnergy;
                usedTemperature -= storedEnergy / blockCapacity;
                extraPower += neededJoules / ONE_MILLION;
            }

            return processedIceKg;
        }

        public override void ReactOnNewHeat(float heat)
        {
            bool nowHot = Api.Utils.GetHeat(Generator) >= _coldThresholdTemp;
            if (nowHot != _isHot)
            {
                _isHot = nowHot;
                UpdateMultiplier(_lastCompressionPower);
            }
            Generator.SetDetailedInfoDirty();
            Generator.RefreshCustomInfo();
        }

        public override void SpreadHeat(float deltaTime)
        {
            SpreadHeatStandard(deltaTime);
        }
    }
}
