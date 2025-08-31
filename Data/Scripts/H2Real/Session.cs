using Sandbox.ModAPI;
using VRage.Game.Components;
using TSUT.HeatManagement;
using System.Collections.Generic;
using VRage.Game.ModAPI;
using VRage.Utils;
using SpaceEngineers.Game.Entities.Blocks;

namespace TSUT.H2Real
{
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public class Session : MySessionComponentBase
    {
        HmsApi _api;

        public override void LoadData()
        {
            _api = new HmsApi(OnHmsConnected);
        }

        private void OnHmsConnected()
        {
            _api.RegisterHeatBehaviorFactory(
                (grid) =>
                {
                    var generators = new List<IMyGasGenerator>();
                    MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid).GetBlocksOfType(generators);
                    var cubeBlocks = new List<IMyCubeBlock>();
                    foreach (var generator in generators)
                    {
                        cubeBlocks.Add(generator);
                    }
                    return cubeBlocks;
                },
                (block) =>
                {
                    if (!(block is IMyGasGenerator))
                        return null;

                    return new GasGeneratorHandler(block as IMyGasGenerator, _api);
                }
            );
            _api.RegisterHeatBehaviorFactory(
                (grid) =>
                {
                    var thrusters = new List<IMyThrust>();
                    MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid).GetBlocksOfType(thrusters);
                    var cubeBlocks = new List<IMyCubeBlock>();
                    foreach (var thruster in thrusters)
                    {
                        if (!thruster.BlockDefinition.SubtypeName.Contains("HydrogenThrust"))
                            continue;

                        cubeBlocks.Add(thruster);
                    }
                    MyLog.Default.WriteLine($"[H2Real] Thrusters found on {grid.DisplayNameText}: {cubeBlocks.Count}");
                    return cubeBlocks;
                },
                (block) =>
                {
                    if (!(block is IMyThrust) || !block.BlockDefinition.SubtypeName.Contains("HydrogenThrust"))
                        return null;

                    return new HydrogenThrusterHandler(block as IMyThrust, _api);
                }
            );
            _api.RegisterHeatBehaviorFactory(
                (grid) =>
                {
                    var engined = new List<IMyPowerProducer>();
                    MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid).GetBlocksOfType(engined);
                    var cubeBlocks = new List<IMyCubeBlock>();
                    foreach (var engine in engined)
                    {
                        if (!engine.BlockDefinition.SubtypeName.Contains("HydrogenEngine"))
                            continue;

                        cubeBlocks.Add(engine);
                    }
                    MyLog.Default.WriteLine($"[H2Real] Engines found on {grid.DisplayNameText}: {cubeBlocks.Count}");
                    return cubeBlocks;
                },
                (block) =>
                {
                    if (!(block is IMyPowerProducer) || !block.BlockDefinition.SubtypeName.Contains("HydrogenEngine"))
                        return null;

                    return new GasEngineHandler(block as IMyPowerProducer, _api);
                }
            );
        }

        protected override void UnloadData()
        {
            _api.Cleanup();
        }
    }
}
