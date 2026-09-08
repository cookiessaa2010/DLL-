using LiteNetLib;
using System;
using System.Collections.Generic;

namespace Coop.Core.Server.Connections
{
    public interface IPlayerAdmissionGate
    {
        int ActiveSlots { get; }
        AdmissionDecision TryAdmit(string controllerId, NetPeer peer);
        bool Release(NetPeer peer);
    }

    /// <summary>
    /// KaiTOR Online's hard 4-player server boundary.
    ///
    /// A slot belongs to a persistent controller id, not to a Player object and not
    /// merely to a socket. This makes simultaneous validation atomic and lets a
    /// reconnect replace the old NetPeer without consuming a fifth slot.
    /// </summary>
    public sealed class PlayerAdmissionGate : IPlayerAdmissionGate
    {
        public const int MaxPlayers = 4;

        private readonly object sync = new object();
        private readonly Dictionary<string, NetPeer> controllerToPeer =
            new Dictionary<string, NetPeer>(StringComparer.Ordinal);
        private readonly Dictionary<NetPeer, string> peerToController =
            new Dictionary<NetPeer, string>();

        public int ActiveSlots
        {
            get
            {
                lock (sync)
                {
                    return controllerToPeer.Count;
                }
            }
        }

        public AdmissionDecision TryAdmit(string controllerId, NetPeer peer)
        {
            if (string.IsNullOrWhiteSpace(controllerId) || peer == null)
                return AdmissionDecision.Reject(AdmissionRejectReason.InvalidIdentity);

            lock (sync)
            {
                if (peerToController.TryGetValue(peer, out string currentController) &&
                    string.Equals(currentController, controllerId, StringComparison.Ordinal))
                {
                    return AdmissionDecision.Accept();
                }

                if (controllerToPeer.TryGetValue(controllerId, out NetPeer oldPeer))
                {
                    if (!ReferenceEquals(oldPeer, peer))
                    {
                        peerToController.Remove(oldPeer);
                        controllerToPeer[controllerId] = peer;
                        peerToController[peer] = controllerId;
                        return AdmissionDecision.AcceptReconnect(oldPeer);
                    }

                    return AdmissionDecision.Accept();
                }

                if (controllerToPeer.Count >= MaxPlayers)
                    return AdmissionDecision.Reject(AdmissionRejectReason.ServerFull);

                controllerToPeer.Add(controllerId, peer);
                peerToController.Add(peer, controllerId);
                return AdmissionDecision.Accept();
            }
        }

        public bool Release(NetPeer peer)
        {
            if (peer == null)
                return false;

            lock (sync)
            {
                if (!peerToController.TryGetValue(peer, out string controllerId))
                    return false;

                peerToController.Remove(peer);

                if (controllerToPeer.TryGetValue(controllerId, out NetPeer currentPeer) &&
                    ReferenceEquals(currentPeer, peer))
                {
                    controllerToPeer.Remove(controllerId);
                }

                return true;
            }
        }
    }

    public enum AdmissionRejectReason
    {
        None = 0,
        ServerFull = 1,
        InvalidIdentity = 2
    }

    public sealed class AdmissionDecision
    {
        public bool Accepted { get; private set; }
        public bool IsReconnect { get; private set; }
        public NetPeer SupersededPeer { get; private set; }
        public AdmissionRejectReason RejectReason { get; private set; }

        private AdmissionDecision()
        {
        }

        public static AdmissionDecision Accept()
        {
            return new AdmissionDecision
            {
                Accepted = true,
                RejectReason = AdmissionRejectReason.None
            };
        }

        public static AdmissionDecision AcceptReconnect(NetPeer oldPeer)
        {
            return new AdmissionDecision
            {
                Accepted = true,
                IsReconnect = true,
                SupersededPeer = oldPeer,
                RejectReason = AdmissionRejectReason.None
            };
        }

        public static AdmissionDecision Reject(AdmissionRejectReason reason)
        {
            return new AdmissionDecision
            {
                Accepted = false,
                RejectReason = reason
            };
        }
    }
}
