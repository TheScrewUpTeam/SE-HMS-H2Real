using System.Collections.Generic;
using System.Text;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using TSUT.HeatManagement;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.ObjectBuilders;
using VRage.Utils;
using static TSUT.HeatManagement.HmsApi;

namespace TSUT.H2Real
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_HydrogenEngine), false)]
    public class GasEngineController : AHmsBlockComponent
    {
        IMyPowerProducer _engine;
        bool _playerWantsOn;
        const int ONE_MILLION = 1000000;
        bool _switchSubscribed = false;
        bool _nextCallInternal = false;
        float _cachedConsumption = 0f;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);
            _engine = (IMyPowerProducer)Entity;
            _engine.AppendingCustomInfo += AppendHeatInfo;
            _playerWantsOn = _engine.Enabled;
            MyAPIGateway.TerminalControls.CustomControlGetter += OnCustomControlGetter;
            _engine.EnabledChanged += Block_EnabledChanged;
        }

        public override void Close()
        {
            if (_engine != null)
            {
                _engine.AppendingCustomInfo -= AppendHeatInfo;
                _engine.EnabledChanged -= Block_EnabledChanged;
                MyAPIGateway.TerminalControls.CustomControlGetter -= OnCustomControlGetter;
            }
            base.Close();
        }

        public override void OnDetachedFromHeatSystem() { }

        private void Block_EnabledChanged(IMyTerminalBlock block)
        {
            if (_nextCallInternal)
            {
                _nextCallInternal = false;
                return;
            }
            _playerWantsOn = (block as IMyFunctionalBlock).Enabled;
        }

        private void OnCustomControlGetter(IMyTerminalBlock topBlock, List<IMyTerminalControl> controls)
        {
            if (topBlock != _engine || _switchSubscribed)
                return;
            foreach (var control in controls)
            {
                if (control.Id == "OnOff")
                {
                    var onOffControl = control as IMyTerminalControlOnOffSwitch;
                    if (onOffControl != null)
                    {
                        onOffControl.Getter += (block) =>
                        {
                            if (block == _engine)
                                return _playerWantsOn;
                            return (block as IMyFunctionalBlock).Enabled;
                        };
                        onOffControl.Setter += (block, value) =>
                        {
                            if (block != _engine)
                                return;
                            _playerWantsOn = value;
                        };
                        _switchSubscribed = true;
                    }
                }
            }
        }

        float GetCurrentH2Consumption()
        {
            var sink = _engine?.Components.Get<MyResourceSinkComponent>();
            if (sink == null)
                return 0f;
            var hydrogenId = new MyDefinitionId(typeof(MyObjectBuilder_GasProperties), "Hydrogen");
            return sink.CurrentInputByType(hydrogenId) / 5;
        }

        float GetCurrentO2Consumption()
        {
            return GetCurrentH2Consumption() * Config.Instance.O2_USAGE_FROM_H2_ENGINE;
        }

        private void AppendHeatInfo(IMyTerminalBlock block, StringBuilder builder)
        {
            if (Api?.Utils == null) return;
            float currentH2Consumption = GetCurrentH2Consumption();
            float currentO2Consumption = GetCurrentO2Consumption();
            float currentHeat = Api.Utils.GetHeat(_engine);
            float blockCapacity = Api.Utils.GetThermalCapacity(_engine);

            float internalUse = CalculateHeat(currentH2Consumption) / blockCapacity;

            float neighborExchange;
            float networkExchange;
            var neighborInfo = new StringBuilder();

            AddNeighborAndNetworksInfo(
                neighborInfo,
                out neighborExchange,
                out networkExchange
            );

            float currentPower = _engine.CurrentOutput;
            float heatChange = GetHeatChange(1f) + neighborExchange + networkExchange;

            builder.AppendLine($"Current Power Output: {currentPower:F2} MW");
            builder.AppendLine("--- HMS.H2Real ---");
            builder.AppendLine($"Hydrogen consumption: {currentH2Consumption:0.00} L/s");
            builder.AppendLine($"Oxygen consumption: {currentO2Consumption:0.00} L/s");
            builder.AppendLine("");
            builder.AppendLine($"Temperature: {currentHeat:F2} °C");
            string heatStatus = heatChange > 0 ? "Heating" : heatChange < -0.01 ? "Cooling" : "Stable";
            builder.AppendLine($"Thermal Status: {heatStatus}");
            builder.AppendLine($"Net Heat Change: {heatChange:+0.00;-0.00;0.00} °C/s");
            builder.AppendLine($"Thermal Capacity: {blockCapacity / ONE_MILLION:F2} MJ/°C");
            builder.AppendLine("");
            builder.AppendLine("Heat sources:");
            builder.AppendLine($"  Internal use: {internalUse:F2} °C/s");
            builder.AppendLine($"  Air Exchange: {-Api.Utils.GetAmbientHeatLoss(block, 1):+0.00;-0.00;0.00} °C/s");
            builder.Append(neighborInfo);
        }

        public float CalculateHeat(float consumption)
        {
            float chemicalPower = Config.Instance.ENERGY_PER_LITER * consumption;
            float heatPower = chemicalPower * (1 - Config.Instance.H2_ENGINE_EFFICIENCY);
            return heatPower;
        }

        public override float GetHeatChange(float deltaTime)
        {
            float tempChange = 0f;
            tempChange -= Api.Utils.GetAmbientHeatLoss(_engine, deltaTime);

            float currentH2Consumption = GetCurrentH2Consumption();
            var newState = false;

            if (_playerWantsOn)
            {
                float consumptionPerSecond = GetCurrentO2Consumption();
                if (consumptionPerSecond > 0f && !float.IsNaN(consumptionPerSecond) && !float.IsInfinity(consumptionPerSecond))
                    _cachedConsumption = consumptionPerSecond;

                if (_cachedConsumption > 0f)
                {
                    float shouldBeConsumed = _cachedConsumption * deltaTime;
                    bool enoughO2 = Api.Utils.HasEnoughO2(shouldBeConsumed, deltaTime, Block);
                    if (enoughO2)
                        newState = Api.Utils.ConsumeO2(shouldBeConsumed, deltaTime, Block) <= 0;
                }
                else
                {
                    newState = true;
                }
            }

            if (_engine.Enabled != newState)
            {
                _nextCallInternal = true;
                _engine.Enabled = newState;
            }

            float capacity = Api.Utils.GetThermalCapacity(_engine);
            if (capacity <= 0f)
                return 0f;
            tempChange += CalculateHeat(currentH2Consumption * deltaTime) / capacity;

            return tempChange;
        }

        private void DamageEngine()
        {
            var slimBlock = _engine.SlimBlock;
            var integrity = slimBlock.MaxIntegrity;
            var damage = integrity * Config.Instance.DAMAGE_PERCENT_ON_VERHEAT;
            slimBlock.DoDamage(damage, MyDamageType.Explosion, true);
            MyVisualScriptLogicProvider.PlaySingleSoundAtEntity(
                "ArcWepSmallMissileExplShip",
                _engine.Name
            );
        }

        public override void ReactOnNewHeat(float heat)
        {
            Api.Effects.UpdateBlockHeatLight(_engine, heat);
            _engine.SetDetailedInfoDirty();
            _engine.RefreshCustomInfo();
            if (heat >= Config.Instance.H2_ENGINE_CRITICAL_TEMP && _engine.IsFunctional)
                DamageEngine();
        }

        public override void SpreadHeat(float deltaTime)
        {
            SpreadHeatStandard(deltaTime);
        }
    }
}
