using Sandbox.ModAPI;
using VRage.Game.Components;
using TSUT.HeatManagement;

namespace TSUT.H2Real
{
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public class Session : MySessionComponentBase
    {
        protected override void UnloadData()
        {
            HmsApi.Instance?.Cleanup();
            HydrogenThrusterController.ResetStatics();
            GasEngineController.ResetStatics();
        }

        public override void SaveData()
        {
            if (MyAPIGateway.Multiplayer.IsServer)
                Config.Instance.Save();
        }
    }
}
