using KaiTOROnline.ServerCapacity;
using Xunit;

namespace KaiTOROnline.Tests.ServerCapacity
{
    public sealed class ConnectionAdmissionGateTests
    {
        private sealed class FakeConnection
        {
        }

        [Fact]
        public void FifthDistinctConnection_IsRejected()
        {
            var gate = new ConnectionAdmissionGate<FakeConnection>();

            Assert.True(gate.TryReserve("p1", new FakeConnection()).Accepted);
            Assert.True(gate.TryReserve("p2", new FakeConnection()).Accepted);
            Assert.True(gate.TryReserve("p3", new FakeConnection()).Accepted);
            Assert.True(gate.TryReserve("p4", new FakeConnection()).Accepted);

            var fifth = gate.TryReserve("p5", new FakeConnection());

            Assert.False(fifth.Accepted);
            Assert.Equal(AdmissionRejectReason.ServerFull, fifth.RejectReason);
            Assert.Equal(4, gate.ActiveSlots);
        }

        [Fact]
        public void Reconnect_ReturnsSupersededConnectionObject()
        {
            var gate = new ConnectionAdmissionGate<FakeConnection>();
            var oldConnection = new FakeConnection();
            var newConnection = new FakeConnection();

            Assert.True(gate.TryReserve("p1", oldConnection).Accepted);

            var reconnect = gate.TryReserve("p1", newConnection);

            Assert.True(reconnect.Accepted);
            Assert.True(reconnect.IsReconnect);
            Assert.Same(oldConnection, reconnect.SupersededConnection);
            Assert.Equal(1, gate.ActiveSlots);
        }

        [Fact]
        public void SupersededDisconnect_DoesNotReleaseReplacementSlot()
        {
            var gate = new ConnectionAdmissionGate<FakeConnection>();
            var oldConnection = new FakeConnection();
            var newConnection = new FakeConnection();

            gate.TryReserve("p1", oldConnection);
            gate.TryReserve("p1", newConnection);

            Assert.False(gate.Release(oldConnection));
            Assert.Equal(1, gate.ActiveSlots);
            Assert.True(gate.Release(newConnection));
            Assert.Equal(0, gate.ActiveSlots);
        }

        [Fact]
        public void SameConnectionRepeatedValidation_IsIdempotent()
        {
            var gate = new ConnectionAdmissionGate<FakeConnection>();
            var connection = new FakeConnection();

            Assert.True(gate.TryReserve("p1", connection).Accepted);
            Assert.True(gate.TryReserve("p1", connection).Accepted);
            Assert.Equal(1, gate.ActiveSlots);
        }

        [Fact]
        public void DifferentConnectionObjectsNeverDependOnRecycledTransportIds()
        {
            var gate = new ConnectionAdmissionGate<FakeConnection>();
            var first = new FakeConnection();
            var replacement = new FakeConnection();

            Assert.True(gate.TryReserve("p1", first).Accepted);
            Assert.True(gate.Release(first));
            Assert.True(gate.TryReserve("p2", replacement).Accepted);
            Assert.Equal(1, gate.ActiveSlots);
        }
    }
}
