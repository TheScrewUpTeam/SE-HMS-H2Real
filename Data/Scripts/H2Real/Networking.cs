using System;
using System.Collections.Generic;
using ProtoBuf;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;

namespace TSUT.H2Real
{
    public class Networking
    {
        public readonly ushort ChannelId;
        private List<IMyPlayer> _tempPlayers;

        public Networking(ushort channelId)
        {
            ChannelId = channelId;
        }

        public void Register()
        {
            MyAPIGateway.Multiplayer.RegisterMessageHandler(ChannelId, ReceivedPacket);
        }

        public void Unregister()
        {
            MyAPIGateway.Multiplayer.UnregisterMessageHandler(ChannelId, ReceivedPacket);
        }

        private void ReceivedPacket(byte[] rawData)
        {
            try
            {
                var packet = MyAPIGateway.Utilities.SerializeFromBinary<PacketBase>(rawData);
                HandlePacket(packet, rawData);
            }
            catch (Exception e)
            {
                MyLog.Default.WriteLineAndConsole($"[HMS.H2Real] Networking error: {e.Message}\n{e.StackTrace}");
            }
        }

        private void HandlePacket(PacketBase packet, byte[] rawData = null)
        {
            if (packet.Received())
                RelayToClients(packet, rawData);
        }

        public void SendToServer(PacketBase packet)
        {
            if (MyAPIGateway.Multiplayer.IsServer)
            {
                HandlePacket(packet);
                return;
            }
            MyAPIGateway.Multiplayer.SendMessageToServer(ChannelId, MyAPIGateway.Utilities.SerializeToBinary(packet));
        }

        public void RelayToClients(PacketBase packet, byte[] rawData = null)
        {
            if (!MyAPIGateway.Multiplayer.IsServer)
                return;

            if (_tempPlayers == null)
                _tempPlayers = new List<IMyPlayer>(MyAPIGateway.Session.SessionSettings.MaxPlayers);
            else
                _tempPlayers.Clear();

            MyAPIGateway.Players.GetPlayers(_tempPlayers);

            foreach (var p in _tempPlayers)
            {
                if (p.IsBot) continue;
                if (p.SteamUserId == MyAPIGateway.Multiplayer.ServerId) continue;
                if (p.SteamUserId == packet.SenderId) continue;

                if (rawData == null)
                    rawData = MyAPIGateway.Utilities.SerializeToBinary(packet);

                MyAPIGateway.Multiplayer.SendMessageTo(ChannelId, rawData, p.SteamUserId);
            }

            _tempPlayers.Clear();
        }
    }

    [ProtoInclude(1000, typeof(EngineWantsOnRequest))]
    [ProtoInclude(1001, typeof(EngineWantsOnSync))]
    [ProtoInclude(1002, typeof(ThrusterWantsOnRequest))]
    [ProtoInclude(1003, typeof(ThrusterWantsOnSync))]
    [ProtoContract]
    public abstract class PacketBase
    {
        [ProtoMember(1)]
        public ulong SenderId;

        protected PacketBase()
        {
            SenderId = MyAPIGateway.Multiplayer.MyId;
        }

        public abstract bool Received();
    }

    [ProtoContract]
    public class EngineWantsOnRequest : PacketBase
    {
        public EngineWantsOnRequest() { }

        [ProtoMember(1)] public long EntityId;
        [ProtoMember(2)] public bool Value;

        public override bool Received()
        {
            IMyEntity entity;
            if (MyAPIGateway.Entities.TryGetEntityById(EntityId, out entity))
            {
                var ctrl = (entity as IMyCubeBlock)?.GameLogic?.GetAs<GasEngineController>();
                if (ctrl != null) ctrl.WantsOn = Value;
            }
            return false;
        }
    }

    [ProtoContract]
    public class EngineWantsOnSync : PacketBase
    {
        public EngineWantsOnSync() { }

        [ProtoMember(1)] public long EntityId;
        [ProtoMember(2)] public bool Value;

        public override bool Received()
        {
            IMyEntity entity;
            if (MyAPIGateway.Entities.TryGetEntityById(EntityId, out entity))
                (entity as IMyCubeBlock)?.GameLogic?.GetAs<GasEngineController>()?.ReceiveWantsOn(Value);
            return false;
        }
    }

    [ProtoContract]
    public class ThrusterWantsOnRequest : PacketBase
    {
        public ThrusterWantsOnRequest() { }

        [ProtoMember(1)] public long EntityId;
        [ProtoMember(2)] public bool Value;

        public override bool Received()
        {
            IMyEntity entity;
            if (MyAPIGateway.Entities.TryGetEntityById(EntityId, out entity))
            {
                var ctrl = (entity as IMyCubeBlock)?.GameLogic?.GetAs<HydrogenThrusterController>();
                if (ctrl != null) ctrl.WantsOn = Value;
            }
            return false;
        }
    }

    [ProtoContract]
    public class ThrusterWantsOnSync : PacketBase
    {
        public ThrusterWantsOnSync() { }

        [ProtoMember(1)] public long EntityId;
        [ProtoMember(2)] public bool Value;

        public override bool Received()
        {
            IMyEntity entity;
            if (MyAPIGateway.Entities.TryGetEntityById(EntityId, out entity))
                (entity as IMyCubeBlock)?.GameLogic?.GetAs<HydrogenThrusterController>()?.ReceiveWantsOn(Value);
            return false;
        }
    }
}
