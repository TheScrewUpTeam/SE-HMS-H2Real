using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using TSUT.HeatManagement;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
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
        float _cachedConsumption = 0f;

        static bool _staticInitDone = false;
        static Func<IMyTerminalBlock, bool> _origOnOffGetter;
        static Action<IMyTerminalBlock, bool> _origOnOffSetter;
        static Action<IMyTerminalBlock> _origToggleAction;
        static Action<IMyTerminalBlock> _origOnAction;
        static Action<IMyTerminalBlock> _origOffAction;

        static GasEngineController Get(IMyTerminalBlock b)
            => b.GameLogic.GetAs<GasEngineController>();

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            base.Init(objectBuilder);
            _engine = (IMyPowerProducer)Entity;
            _engine.AppendingCustomInfo += AppendHeatInfo;
            _playerWantsOn = _engine.Enabled;

            if (!_staticInitDone)
            {
                _staticInitDone = true;
                PatchControlsAndActions();
            }
        }

        public override void Close()
        {
            if (_engine != null)
                _engine.AppendingCustomInfo -= AppendHeatInfo;
            base.Close();
        }

        static void PatchControlsAndActions()
        {
            List<IMyTerminalControl> controls;
            MyAPIGateway.TerminalControls.GetControls<IMyFunctionalBlock>(out controls);
            foreach (var control in controls)
            {
                if (control.Id != "OnOff") continue;
                var onOff = control as IMyTerminalControlOnOffSwitch;
                if (onOff == null) break;
                _origOnOffGetter = onOff.Getter;
                _origOnOffSetter = onOff.Setter;
                onOff.Getter = OnOffGetter;
                onOff.Setter = OnOffSetter;
                break;
            }

            List<IMyTerminalAction> actions;
            MyAPIGateway.TerminalControls.GetActions<IMyFunctionalBlock>(out actions);
            foreach (var action in actions)
            {
                switch (action.Id)
                {
                    case "OnOff":
                        _origToggleAction = action.Action;
                        action.Action = OnToggleAction;
                        break;
                    case "OnOff_On":
                        _origOnAction = action.Action;
                        action.Action = OnOnAction;
                        break;
                    case "OnOff_Off":
                        _origOffAction = action.Action;
                        action.Action = OnOffAction;
                        break;
                }
            }
        }

        public static void ResetStatics()
        {
            _staticInitDone = false;
            _origOnOffGetter = null;
            _origOnOffSetter = null;
            _origToggleAction = null;
            _origOnAction = null;
            _origOffAction = null;
        }

        static bool OnOffGetter(IMyTerminalBlock block)
        {
            var ctrl = Get(block);
            return ctrl != null
                ? ctrl._playerWantsOn
                : _origOnOffGetter?.Invoke(block) ?? (block as IMyFunctionalBlock)?.Enabled ?? false;
        }

        static void OnOffSetter(IMyTerminalBlock block, bool value)
        {
            var ctrl = Get(block);
            if (ctrl != null)
                ctrl._playerWantsOn = value;
            else
                _origOnOffSetter?.Invoke(block, value);
        }

        static void OnToggleAction(IMyTerminalBlock block)
        {
            var ctrl = Get(block);
            if (ctrl != null)
                ctrl._playerWantsOn = !ctrl._playerWantsOn;
            else
                _origToggleAction?.Invoke(block);
        }

        static void OnOnAction(IMyTerminalBlock block)
        {
            var ctrl = Get(block);
            if (ctrl != null)
                ctrl._playerWantsOn = true;
            else
                _origOnAction?.Invoke(block);
        }

        static void OnOffAction(IMyTerminalBlock block)
        {
            var ctrl = Get(block);
            if (ctrl != null)
                ctrl._playerWantsOn = false;
            else
                _origOffAction?.Invoke(block);
        }

        public override void OnDetachedFromHeatSystem() { }

        float GetCurrentH2Consumption()
        {
            var def = _engine?.SlimBlock.BlockDefinition as MyGasFueledPowerProducerDefinition;
            if (def == null || def.FuelProductionToCapacityMultiplier <= 0f) return 0f;
            return _engine.CurrentOutput / def.FuelProductionToCapacityMultiplier;
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
            if (blockCapacity <= 0f) return;

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
            float ambientExchange = Api.Utils.GetAmbientHeatLoss(block, 1);
            float heatChange = internalUse - ambientExchange + neighborExchange + networkExchange;

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
            builder.AppendLine($"  Air Exchange: {-ambientExchange:+0.00;-0.00;0.00} °C/s");
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
            if (_engine == null) return 0f;

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
                _engine.Enabled = newState;

            float capacity = Api.Utils.GetThermalCapacity(_engine);
            if (capacity <= 0f)
                return tempChange;
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
            if (_engine == null) return;
            Api.Effects.UpdateBlockHeatLight(_engine, heat);
            _engine.SetDetailedInfoDirty();
            _engine.RefreshCustomInfo();
            if (heat >= Config.Instance.H2_ENGINE_CRITICAL_TEMP && _engine.IsFunctional)
                DamageEngine();
        }

        public override void SpreadHeat(float deltaTime)
        {
            if (_engine == null) return;
            SpreadHeatStandard(deltaTime);
        }
    }
}
