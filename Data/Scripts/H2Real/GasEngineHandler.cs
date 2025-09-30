using System.Collections.Generic;
using System.Text;
using Sandbox.Game;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using TSUT.HeatManagement;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.Definitions;
using static TSUT.HeatManagement.HmsApi;

namespace TSUT.H2Real
{
    public class GasEngineHandler : AHeatBehavior
    {
        IMyPowerProducer _engine;
        HmsApi _api;
        bool _playerWantsOn;
        const int ONE_MILLION = 1000000;
        bool _switchSubscribed = false;
        bool _nextCallINternal = false;

        public GasEngineHandler(IMyPowerProducer block, HmsApi api) : base(block)
        {
            _api = api;
            _engine = block;
            _engine.AppendingCustomInfo += AppendHeatInfo;
            _playerWantsOn = _engine.Enabled;
            TrackOnSwitch();
            _engine.RefreshCustomInfo();
            _engine.SetDetailedInfoDirty();
            block.EnabledChanged += Block_EnabledChanged;
        }

        private void Block_EnabledChanged(IMyTerminalBlock block)
        {
            if (_nextCallINternal)
            {
                _nextCallINternal = false;
                return;
            }
            _playerWantsOn = (block as IMyFunctionalBlock).Enabled;
        }

        private void TrackOnSwitch()
        {
            MyAPIGateway.TerminalControls.CustomControlGetter += OnCustomControlGetter;
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

        private List<IMyGasTank> FindConnectedO2TanksThroughConveyor()
        {
            var result = new List<IMyGasTank>();
            var grids = FindSubgrids(_engine.CubeGrid);
            foreach (var grid in grids)
            {
                var candidates = new List<IMyGasTank>();
                MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid).GetBlocksOfType(candidates);
                foreach (var candidate in candidates)
                {
                    var isOxygen = candidate.BlockDefinition.SubtypeName == "" || candidate.BlockDefinition.SubtypeName.Contains("Oxygen");
                    var isStokpile = candidate.Stockpile;
                    var alreadyFound = result.Contains(candidate);
                    if (isOxygen & !isStokpile & !alreadyFound)
                    {
                        result.Add(candidate);
                    }
                }
            }

            return result;
        }

        List<IMyCubeGrid> FindSubgrids(IMyCubeGrid root)
        {
            var visited = new List<IMyCubeGrid>();
            var stack = new List<IMyCubeGrid>
            {
                root
            };

            while (stack.Count > 0)
            {
                var grid = stack.Pop();
                if (grid == null)
                    continue;
                if (visited.Contains(grid))
                    continue;

                visited.Add(grid);

                var motors = grid.GetFatBlocks<IMyMotorStator>();
                foreach (var block in motors)
                {
                    stack.Add(block?.TopGrid);
                }
                var pistons = grid.GetFatBlocks<IMyPistonBase>();
                foreach (var block in pistons)
                {
                    stack.Add(block?.TopGrid);
                }
                var connectors = grid.GetFatBlocks<IMyShipConnector>();
                foreach (var block in connectors)
                {
                    stack.Add(block?.OtherConnector?.CubeGrid);
                }
            }

            return visited;
        }

        float GetCurrentH2Consumption()
        {
            var sink = _engine?.Components.Get<MyResourceSinkComponent>();
            if (sink == null)
                return 0f;

            var hydrogenId = new MyDefinitionId(typeof(MyObjectBuilder_GasProperties), "Hydrogen");
            var currentConsumption = sink.CurrentInputByType(hydrogenId);

            return currentConsumption; // L/s
        }

        float GetCurrentO2ConsumptionInt()
        {
            var result = GetCurrentH2Consumption() * 0.5f;
            return result;
        }

        float GetCurrentO2Consumption()
        {
            var sink = _engine?.Components.Get<MyResourceSinkComponent>();
            if (sink == null)
                return 0f;

            var oxygenId = new MyDefinitionId(typeof(MyObjectBuilder_GasProperties), "Oxygen");
            var currentConsumption = sink.CurrentInputByType(oxygenId);

            return currentConsumption; // L/s
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
            float currentO2Consumption = GetCurrentO2ConsumptionInt();
            float currentHeat = _api.Utils.GetHeat(_engine);
            float blockCapacity = _api.Utils.GetThermalCapacity(_engine);

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

            float currentPower = _engine.CurrentOutput;
            float heatChange = GetHeatChange(1f) + neighborExchange + networkExchange; // Assuming deltaTime of 1 second for display purposes

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
            builder.AppendLine($"  Air Exchange: {-_api.Utils.GetAmbientHeatLoss(block, 1):+0.00;-0.00;0.00} °C/s");
            builder.Append(neighborInfo);
        }

        public override void Cleanup()
        {
            _engine.AppendingCustomInfo -= AppendHeatInfo;
            MyAPIGateway.TerminalControls.CustomControlGetter -= OnCustomControlGetter;
        }

        public float CalculateHeat(float consumption)
        {
            float chemicalPower = Config.Instance.ENERGY_PER_LITER * consumption; // J/s
            float heatPower = chemicalPower * (1 - Config.Instance.H2_ENGINE_EFFICIENCY); // J/s (Watts)
            return heatPower;
        }

        public override float GetHeatChange(float deltaTime)
        {
            float tempChange = 0f;
            tempChange -= _api.Utils.GetAmbientHeatLoss(_engine, deltaTime);

            float currentH2Consumption = GetCurrentH2Consumption();
            var newState = false;
                
            if (_playerWantsOn)
            {
                float shouldBeConsumed = currentH2Consumption * 0.5f * deltaTime;
                bool enoughO2 = ConsumeO2(shouldBeConsumed);

                newState = enoughO2;

            }

            if (_engine.Enabled != newState) {
                _nextCallINternal = true;
                _engine.Enabled = newState;
            }

            float capacity = _api.Utils.GetThermalCapacity(_engine);
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
                "ArcWepSmallMissileExplShip",    // sound subtypeId from Audio.sbc
                _engine.Name
            );
        }

        public override void ReactOnNewHeat(float heat)
        {
            _api.Effects.UpdateBlockHeatLight(_engine, heat);
            _engine.SetDetailedInfoDirty();
            _engine.RefreshCustomInfo();
            if (heat >= Config.Instance.H2_ENGINE_CRITICAL_TEMP && _engine.IsFunctional)
            {
                DamageEngine();
            }
        }

        public override void SpreadHeat(float deltaTime)
        {
            SpreadHeatStandard(deltaTime, _engine, _api);
        }
    }
}