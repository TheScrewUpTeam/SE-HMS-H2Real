using System.Collections.Generic;
using System.Text;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using TSUT.HeatManagement;
using VRage.Game;
using static TSUT.HeatManagement.HmsApi;

namespace TSUT.H2Real
{
    public class HydrogenThrusterHandler : AHeatBehavior
    {
        IMyThrust _thruster;
        HmsApi _api;
        bool _playerWnatsOn;
        const int ONE_MILLION = 1000000;

        public HydrogenThrusterHandler(IMyThrust block, HmsApi api) : base(block)
        {
            _api = api;
            _thruster = block;
            _thruster.AppendingCustomInfo += AppendHeatInfo;
            _playerWnatsOn = _thruster.Enabled;
            TrackOnSwitch();
        }

        private void TrackOnSwitch()
        {
            IMyTerminalControls terminal = MyAPIGateway.TerminalControls;
            List<IMyTerminalControl> controls;
            terminal.GetControls<IMyThrust>(out controls);
            foreach (var control in controls)
            {
                if (control.Id == "OnOff")
                {
                    var onOffControl = control as IMyTerminalControlOnOffSwitch;
                    if (onOffControl != null)
                    {
                        onOffControl.Getter += (block) =>
                        {
                            bool value;
                            if (block != _thruster)
                                value = (block as IMyFunctionalBlock).Enabled;
                            else
                                value = _playerWnatsOn;
                            return value;
                        };
                        onOffControl.Setter += (block, value) =>
                        {
                            if (block != _thruster)
                                return;
                            _playerWnatsOn = value;
                        };
                    }
                }
            }
        }

        private List<IMyGasTank> FindConnectedO2TanksThroughConveyor()
        {
            var result = new List<IMyGasTank>();
            var candidates = new List<IMyGasTank>();
            MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(_thruster.CubeGrid).GetBlocksOfType(candidates);
            foreach (var candidate in candidates)
            {
                if (candidate.BlockDefinition.SubtypeName == "" || candidate.BlockDefinition.SubtypeName.Contains("Oxygen"))
                {
                    result.Add(candidate);
                }
            }

            return result;
        }

        float GetCurrentH2Consumption()
        {
            var def = _thruster.SlimBlock.BlockDefinition as MyThrustDefinition;
            var fuelConv = def.FuelConverter;
            return _thruster.CurrentThrust * fuelConv.Efficiency / 1500f; // Convert from kN to L/s
        }
        float GetCurrentO2Consumption()
        {
            return GetCurrentH2Consumption() * 0.5f;
        }

        bool ConsumeO2(float shouldBeConsumed)
        {
            var tanks = FindConnectedO2TanksThroughConveyor();
            foreach (IMyGasTank tank in tanks)
            {
                if (tank.FilledRatio == 0)
                {
                    continue;
                }
                double currentVolume = tank.Capacity * tank.FilledRatio;

                if (currentVolume < shouldBeConsumed)
                {
                    tank.ChangeFilledRatio(0, true);
                    shouldBeConsumed -= (float)currentVolume;
                }
                else
                {
                    var newVolume = currentVolume - shouldBeConsumed;
                    tank.ChangeFilledRatio(newVolume / tank.Capacity, true);
                    shouldBeConsumed = 0;
                    return true;
                }
                tank.SetDetailedInfoDirty();
            }
            return false;
        }

        private void AppendHeatInfo(IMyTerminalBlock block, StringBuilder builder)
        {
            float currentH2Consumption = GetCurrentH2Consumption();
            float currentO2Consumption = GetCurrentO2Consumption();
            float currentHeat = _api.Utils.GetHeat(_thruster);
            float blockCapacity = _api.Utils.GetThermalCapacity(_thruster);

            float internalUse = CalculateHeat(currentH2Consumption) / blockCapacity; // °C/s
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

            float currentThrust = _thruster.CurrentThrust / 1000f;
            float ambientExchange = _api.Utils.GetAmbientHeatLoss(block, 1);
            float heatChange = internalUse - ambientExchange + neighborExchange + networkExchange; // Assuming deltaTime of 1 second for display purposes

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

        public override void Cleanup()
        {
            _thruster.AppendingCustomInfo -= AppendHeatInfo;
        }
        public float CalculateHeat(float consumption)
        {
            float chemicalPower = Config.Instance.ENERGY_PER_LITER * consumption; // J/s
            float heatPower = chemicalPower * (1 - Config.Instance.H2_THRUST_EFFICIENCY); // J/s (Watts)
            return heatPower;
        }

        public override float GetHeatChange(float deltaTime)
        {
            float currentH2Consumption = GetCurrentH2Consumption();

            float shouldBeConsumed = currentH2Consumption * 0.5f * deltaTime;
            bool enoughO2 = ConsumeO2(shouldBeConsumed);

            
            _thruster.Enabled = _playerWnatsOn && enoughO2;
            
            float tempChange = 0f;

            float capacity = _api.Utils.GetThermalCapacity(_thruster);
            var internalUse = CalculateHeat(currentH2Consumption * deltaTime) / capacity;
            var ambientExchange = _api.Utils.GetAmbientHeatLoss(_thruster, deltaTime);

            tempChange += internalUse;
            tempChange -= ambientExchange;

            return tempChange;
        }

        public override void ReactOnNewHeat(float heat)
        {
            _api.Effects.UpdateBlockHeatLight(_thruster, heat);
            _thruster.SetDetailedInfoDirty();
            _thruster.RefreshCustomInfo();
            if (heat >= Config.Instance.H2_THRUSTER_CRITICAL_TEMP && _thruster.IsFunctional)
            {
                DamageThruster();
            }
        }

        private void DamageThruster()
        {
            var slimBlock = _thruster.SlimBlock;
            var integrity = slimBlock.MaxIntegrity;
            var damage = integrity * Config.Instance.DAMAGE_PERCENT_ON_VERHEAT;
            slimBlock.DoDamage(damage, MyDamageType.Explosion, true);
            MyVisualScriptLogicProvider.PlaySingleSoundAtEntity(
                "ArcWepSmallMissileExplShip",    // sound subtypeId from Audio.sbc
                _thruster.Name
            );
        }

        public override void SpreadHeat(float deltaTime)
        {
            SpreadHeatStandard(deltaTime, _thruster, _api);
        }
    }
}