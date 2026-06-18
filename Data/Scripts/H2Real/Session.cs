using Sandbox.ModAPI;
using VRage.Game.Components;
using TSUT.HeatManagement;

namespace TSUT.H2Real
{
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public class Session : MySessionComponentBase
    {
        public static readonly Networking Networking = new Networking(7960);

        public override void BeforeStart()
        {
            Networking.Register();
        }

        protected override void UnloadData()
        {
            Networking.Unregister();
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
