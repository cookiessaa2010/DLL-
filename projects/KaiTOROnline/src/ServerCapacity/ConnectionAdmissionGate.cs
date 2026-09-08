using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace KaiTOROnline.ServerCapacity
{
    /// <summary>
    /// Bridges the pure controller/connection-id admission gate to real connection objects.
    ///
    /// Each live connection object receives a random stable token for its lifetime. This avoids
    /// relying on transport peer ids, which may be recycled after a disconnect. The wrapper also
    /// resolves a superseded reconnect token back to the old connection object so the integration
    /// layer can close that stale peer after accepting the new one.
    /// </summary>
    public sealed class ConnectionAdmissionGate<TConnection> where TConnection : class
    {
        private readonly object sync = new object();
        private readonly PlayerAdmissionGate gate = new PlayerAdmissionGate();
        private readonly Dictionary<TConnection, string> connectionToToken =
            new Dictionary<TConnection, string>(ReferenceEqualityComparer<TConnection>.Instance);
        private readonly Dictionary<string, TConnection> tokenToConnection =
            new Dictionary<string, TConnection>(StringComparer.Ordinal);

        public int ActiveSlots => gate.ActiveSlots;

        public ConnectionAdmissionResult<TConnection> TryReserve(string controllerId, TConnection connection)
        {
            if (connection == null)
                return ConnectionAdmissionResult<TConnection>.Rejected(AdmissionRejectReason.InvalidConnectionId);

            lock (sync)
            {
                string token = GetOrCreateToken(connection);
                AdmissionResult result = gate.TryReserve(controllerId, token);

                if (!result.Accepted)
                    return ConnectionAdmissionResult<TConnection>.Rejected(result.RejectReason);

                TConnection superseded = null;
                if (result.IsReconnect && !string.IsNullOrEmpty(result.SupersededConnectionId))
                {
                    tokenToConnection.TryGetValue(result.SupersededConnectionId, out superseded);
                    if (superseded != null)
                    {
                        connectionToToken.Remove(superseded);
                        tokenToConnection.Remove(result.SupersededConnectionId);
                    }
                }

                return result.IsReconnect
                    ? ConnectionAdmissionResult<TConnection>.AcceptedReconnect(superseded)
                    : ConnectionAdmissionResult<TConnection>.AcceptedNew();
            }
        }

        public bool Release(TConnection connection)
        {
            if (connection == null)
                return false;

            lock (sync)
            {
                if (!connectionToToken.TryGetValue(connection, out string token))
                    return false;

                connectionToToken.Remove(connection);
                tokenToConnection.Remove(token);
                return gate.ReleaseConnection(token);
            }
        }

        public void Reset()
        {
            lock (sync)
            {
                connectionToToken.Clear();
                tokenToConnection.Clear();
                gate.Reset();
            }
        }

        private string GetOrCreateToken(TConnection connection)
        {
            if (connectionToToken.TryGetValue(connection, out string token))
                return token;

            token = Guid.NewGuid().ToString("N");
            connectionToToken.Add(connection, token);
            tokenToConnection.Add(token, connection);
            return token;
        }
    }

    public sealed class ConnectionAdmissionResult<TConnection> where TConnection : class
    {
        public bool Accepted { get; private set; }
        public bool IsReconnect { get; private set; }
        public TConnection SupersededConnection { get; private set; }
        public AdmissionRejectReason RejectReason { get; private set; }

        private ConnectionAdmissionResult()
        {
        }

        public static ConnectionAdmissionResult<TConnection> AcceptedNew()
        {
            return new ConnectionAdmissionResult<TConnection>
            {
                Accepted = true,
                RejectReason = AdmissionRejectReason.None
            };
        }

        public static ConnectionAdmissionResult<TConnection> AcceptedReconnect(TConnection supersededConnection)
        {
            return new ConnectionAdmissionResult<TConnection>
            {
                Accepted = true,
                IsReconnect = true,
                SupersededConnection = supersededConnection,
                RejectReason = AdmissionRejectReason.None
            };
        }

        public static ConnectionAdmissionResult<TConnection> Rejected(AdmissionRejectReason reason)
        {
            return new ConnectionAdmissionResult<TConnection>
            {
                Accepted = false,
                RejectReason = reason
            };
        }
    }

    internal sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T> where T : class
    {
        public static readonly ReferenceEqualityComparer<T> Instance = new ReferenceEqualityComparer<T>();

        private ReferenceEqualityComparer()
        {
        }

        public bool Equals(T x, T y) => ReferenceEquals(x, y);

        public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
