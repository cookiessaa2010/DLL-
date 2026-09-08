using System;
using System.Collections.Generic;

namespace KaiTOROnline.ServerCapacity
{
    /// <summary>
    /// Server-authoritative reservation table for KaiTOR Online.
    ///
    /// The gate is intentionally independent of Bannerlord/Coop types so it can be
    /// unit-tested in isolation and then wired into Coop's ResolveCharacterState.
    /// A controller id represents the persistent player identity; a connection id
    /// represents the current live network session.
    /// </summary>
    public sealed class PlayerAdmissionGate
    {
        public const int MaxPlayers = 4;

        private readonly object sync = new object();
        private readonly Dictionary<string, string> controllerToConnection =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> connectionToController =
            new Dictionary<string, string>(StringComparer.Ordinal);

        public int ActiveSlots
        {
            get
            {
                lock (sync)
                {
                    return controllerToConnection.Count;
                }
            }
        }

        public AdmissionResult TryReserve(string controllerId, string connectionId)
        {
            if (string.IsNullOrWhiteSpace(controllerId))
                return AdmissionResult.Rejected(AdmissionRejectReason.InvalidControllerId);

            if (string.IsNullOrWhiteSpace(connectionId))
                return AdmissionResult.Rejected(AdmissionRejectReason.InvalidConnectionId);

            lock (sync)
            {
                // Same transport connection repeating validation is idempotent.
                if (connectionToController.TryGetValue(connectionId, out string existingController) &&
                    string.Equals(existingController, controllerId, StringComparison.Ordinal))
                {
                    return AdmissionResult.AcceptedNew();
                }

                // A reconnect for the same persistent controller replaces the old network
                // session and never consumes a fifth slot. The caller should disconnect the
                // returned superseded connection after the new peer is accepted.
                if (controllerToConnection.TryGetValue(controllerId, out string oldConnectionId))
                {
                    if (!string.Equals(oldConnectionId, connectionId, StringComparison.Ordinal))
                    {
                        connectionToController.Remove(oldConnectionId);
                        controllerToConnection[controllerId] = connectionId;
                        connectionToController[connectionId] = controllerId;
                        return AdmissionResult.AcceptedReconnect(oldConnectionId);
                    }

                    return AdmissionResult.AcceptedNew();
                }

                if (controllerToConnection.Count >= MaxPlayers)
                    return AdmissionResult.Rejected(AdmissionRejectReason.ServerFull);

                controllerToConnection.Add(controllerId, connectionId);
                connectionToController.Add(connectionId, controllerId);
                return AdmissionResult.AcceptedNew();
            }
        }

        public bool ReleaseConnection(string connectionId)
        {
            if (string.IsNullOrWhiteSpace(connectionId))
                return false;

            lock (sync)
            {
                if (!connectionToController.TryGetValue(connectionId, out string controllerId))
                    return false;

                connectionToController.Remove(connectionId);

                // Do not clear a controller reservation if this was a superseded connection.
                if (controllerToConnection.TryGetValue(controllerId, out string currentConnectionId) &&
                    string.Equals(currentConnectionId, connectionId, StringComparison.Ordinal))
                {
                    controllerToConnection.Remove(controllerId);
                }

                return true;
            }
        }

        public bool IsReserved(string controllerId)
        {
            if (string.IsNullOrWhiteSpace(controllerId))
                return false;

            lock (sync)
            {
                return controllerToConnection.ContainsKey(controllerId);
            }
        }

        public void Reset()
        {
            lock (sync)
            {
                controllerToConnection.Clear();
                connectionToController.Clear();
            }
        }
    }

    public enum AdmissionRejectReason
    {
        None = 0,
        ServerFull = 1,
        InvalidControllerId = 2,
        InvalidConnectionId = 3
    }

    public sealed class AdmissionResult
    {
        public bool Accepted { get; private set; }
        public bool IsReconnect { get; private set; }
        public string SupersededConnectionId { get; private set; }
        public AdmissionRejectReason RejectReason { get; private set; }

        private AdmissionResult()
        {
        }

        public static AdmissionResult AcceptedNew()
        {
            return new AdmissionResult
            {
                Accepted = true,
                RejectReason = AdmissionRejectReason.None
            };
        }

        public static AdmissionResult AcceptedReconnect(string supersededConnectionId)
        {
            return new AdmissionResult
            {
                Accepted = true,
                IsReconnect = true,
                SupersededConnectionId = supersededConnectionId,
                RejectReason = AdmissionRejectReason.None
            };
        }

        public static AdmissionResult Rejected(AdmissionRejectReason reason)
        {
            return new AdmissionResult
            {
                Accepted = false,
                RejectReason = reason
            };
        }
    }
}
