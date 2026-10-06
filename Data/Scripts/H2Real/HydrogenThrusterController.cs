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
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Thrust), false)]
    public class HydrogenThrusterController : AHmsBlockComponent
    {
        IMyThrust _thruster;
        bool _playerWantsOn;
        const int ONE_MILLION = 1000000;
        float _cachedConsumption = 0f;

        static bool _staticInitDone = false;
        static Func<IMyTerminalBlock, bool> _origOnOffGetter;
        static Action<IMyTerminalBlock, bool> _origOnOffSetter;
        static Action<IMyTerminalBlock> _origToggleAction;
        static Action<IMyTerminalBlock> _origOnAction;
        static Action<IMyTerminalBlock> _origOffAction;

        static HydrogenThrusterController Get(IMyTerminalBlock b)
        {
            var ctrl = b.GameLogic.GetAs<HydrogenThrusterController>();
            return ctrl?._thruster != null ? ctrl : null;
        }

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            if (!((IMyCubeBlock)Entity).BlockDefinition.SubtypeName.Contains("HydrogenThrust"))
                return;

            base.Init(objectBuilder);
            _thruster = (IMyThrust)Entity;
            _thruster.AppendingCustomInfo += AppendHeatInfo;
            _playerWantsOn = _thruster.Enabled;

            if (!_staticInitDone)
            {
                _staticInitDone = true;
                PatchControlsAndActions();
            }
        }

        public override void Close()
        {
            if (_thruster != null)
                _thruster.AppendingCustomInfo -= AppendHeatInfo;
            base.Close();
        }

        static void PatchControlsAndActions()
        {
            List<IMyTerminalControl> controls;
            MyAPIGateway.TerminalControls.GetControls<IMyThrust>(out controls);
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
            MyAPIGateway.TerminalControls.GetActions<IMyThrust>(out actions);
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
                ? ctrl.WantsOn
                : _origOnOffGetter?.Invoke(block) ?? (block as IMyFunctionalBlock)?.Enabled ?? false;
        }

        static void OnOffSetter(IMyTerminalBlock block, bool value)
        {
            var ctrl = Get(block);
            if (ctrl != null)
                ctrl.WantsOn = value;
            else
                _origOnOffSetter?.Invoke(block, value);
        }

        static void OnToggleAction(IMyTerminalBlock block)
        {
            var ctrl = Get(block);
            if (ctrl != null)
                ctrl.WantsOn = !ctrl.WantsOn;
            else
                _origToggleAction?.Invoke(block);
        }

        static void OnOnAction(IMyTerminalBlock block)
        {
            var ctrl = Get(block);
            if (ctrl != null)
                ctrl.WantsOn = true;
            else
                _origOnAction?.Invoke(block);
        }

        static void OnOffAction(IMyTerminalBlock block)
        {
            var ctrl = Get(block);
            if (ctrl != null)
                ctrl.WantsOn = false;
            else
                _origOffAction?.Invoke(block);
        }

        public bool WantsOn
        {
            get { return _playerWantsOn; }
            set
            {
                if (!MyAPIGateway.Multiplayer.IsServer)
                {
                    Session.Networking.SendToServer(new ThrusterWantsOnRequest { EntityId = Entity.EntityId, Value = value });
                    return;
                }
                if (_playerWantsOn == value) return;
                _playerWantsOn = value;
                Session.Networking.RelayToClients(new ThrusterWantsOnSync { EntityId = Entity.EntityId, Value = value });
            }
        }

        public void ReceiveWantsOn(bool value)
        {
            _playerWantsOn = value;
        }

        public override void OnDetachedFromHeatSystem() { }

        float GetCurrentH2Consumption()
        {
            var def = _thruster.SlimBlock.BlockDefinition as MyThrustDefinition;
            if (def == null) return 0f;
            return _thruster.CurrentThrust / (def.FuelConverter.Efficiency * Config.Instance.H2_THRUST_SPECIFIC_FORCE);
        }

        float GetCurrentO2Consumption()
        {
            return GetCurrentH2Consumption() * Config.Instance.O2_USAGE_FROM_H2_THRUSTER;
        }

        private void AppendHeatInfo(IMyTerminalBlock block, StringBuilder builder)
        {
            if (Api?.Utils == null) return;
            float currentH2Consumption = GetCurrentH2Consumption();
            float currentO2Consumption = GetCurrentO2Consumption();
            float currentHeat = Api.Utils.GetHeat(_thruster);
            float blockCapacity = Api.Utils.GetThermalCapacity(_thruster);

            float internalUse = CalculateHeat(currentH2Consumption) / blockCapacity;
            float neighborExchange;
            float networkExchange;
            var neighborInfo = new StringBuilder();

            AddNeighborAndNetworksInfo(
                neighborInfo,
                out neighborExchange,
                out networkExchange
            );

            float currentThrust = _thruster.CurrentThrust / 1000f;
            float ambientExchange = Api.Utils.GetAmbientHeatLoss(block, 1);
            float heatChange = internalUse - ambientExchange + neighborExchange + networkExchange;

            builder.AppendLine($"Current Thrust: {currentThrust:F2} kN");
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
            float heatPower = chemicalPower * (1 - Config.Instance.H2_THRUST_EFFICIENCY);
            return heatPower;
        }

        public override float GetHeatChange(float deltaTime)
        {
            if (_thruster == null) return 0f;

            float currentH2Consumption = GetCurrentH2Consumption();
            var newState = false;

            if (_playerWantsOn)
            {
                float consumptionPerSecond = GetCurrentO2Consumption();
                if (consumptionPerSecond != 0f)
                    _cachedConsumption = consumptionPerSecond;

                bool enoughO2 = Api.Utils.HasEnoughO2(_cachedConsumption * deltaTime, deltaTime, Block);
                float shouldBeConsumed = GetCurrentO2Consumption() * deltaTime;
                if (enoughO2)
                    newState = Api.Utils.ConsumeO2(shouldBeConsumed, deltaTime, Block) <= 0;
            }

            if (_thruster.Enabled != newState)
                _thruster.Enabled = newState;

            float capacity = Api.Utils.GetThermalCapacity(_thruster);
            float internalUse = CalculateHeat(currentH2Consumption * deltaTime) / capacity;
            float ambientExchange = Api.Utils.GetAmbientHeatLoss(_thruster, deltaTime);

            return internalUse - ambientExchange;
        }

        public override void ReactOnNewHeat(float heat)
        {
            if (_thruster == null) return;
            Api.Effects.UpdateBlockHeatLight(_thruster, heat);
            _thruster.SetDetailedInfoDirty();
            _thruster.RefreshCustomInfo();
            if (heat >= Config.Instance.H2_THRUSTER_CRITICAL_TEMP && _thruster.IsFunctional)
                DamageThruster();
        }

        private void DamageThruster()
        {
            var slimBlock = _thruster.SlimBlock;
            var integrity = slimBlock.MaxIntegrity;
            var damage = integrity * Config.Instance.DAMAGE_PERCENT_ON_VERHEAT;
            slimBlock.DoDamage(damage, MyDamageType.Explosion, true);
            MyVisualScriptLogicProvider.PlaySingleSoundAtEntity(
                "ArcWepSmallMissileExplShip",
                _thruster.Name
            );
        }

        public override void SpreadHeat(float deltaTime)
        {
            if (_thruster == null) return;
            SpreadHeatStandard(deltaTime);
        }
    }
}
