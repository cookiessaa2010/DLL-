using KaiTOROnline.ServerCapacity;
using Xunit;

namespace KaiTOROnline.Tests.ServerCapacity
{
    public sealed class PlayerAdmissionGateTests
    {
        [Fact]
        public void FourDistinctControllers_AreAccepted()
        {
            var gate = new PlayerAdmissionGate();

            Assert.True(gate.TryReserve("p1", "c1").Accepted);
            Assert.True(gate.TryReserve("p2", "c2").Accepted);
            Assert.True(gate.TryReserve("p3", "c3").Accepted);
            Assert.True(gate.TryReserve("p4", "c4").Accepted);
            Assert.Equal(4, gate.ActiveSlots);
        }

        [Fact]
        public void FifthDistinctController_IsRejectedAsServerFull()
        {
            var gate = new PlayerAdmissionGate();

            gate.TryReserve("p1", "c1");
            gate.TryReserve("p2", "c2");
            gate.TryReserve("p3", "c3");
            gate.TryReserve("p4", "c4");

            var result = gate.TryReserve("p5", "c5");

            Assert.False(result.Accepted);
            Assert.Equal(AdmissionRejectReason.ServerFull, result.RejectReason);
            Assert.Equal(4, gate.ActiveSlots);
        }

        [Fact]
        public void SameControllerReconnect_ReplacesOldConnectionWithoutUsingExtraSlot()
        {
            var gate = new PlayerAdmissionGate();

            gate.TryReserve("p1", "old");
            gate.TryReserve("p2", "c2");
            gate.TryReserve("p3", "c3");
            gate.TryReserve("p4", "c4");

            var result = gate.TryReserve("p1", "new");

            Assert.True(result.Accepted);
            Assert.True(result.IsReconnect);
            Assert.Equal("old", result.SupersededConnectionId);
            Assert.Equal(4, gate.ActiveSlots);
        }

        [Fact]
        public void Disconnect_FreesSlotForAnotherController()
        {
            var gate = new PlayerAdmissionGate();

            gate.TryReserve("p1", "c1");
            gate.TryReserve("p2", "c2");
            gate.TryReserve("p3", "c3");
            gate.TryReserve("p4", "c4");

            Assert.True(gate.ReleaseConnection("c3"));

            var result = gate.TryReserve("p5", "c5");

            Assert.True(result.Accepted);
            Assert.Equal(4, gate.ActiveSlots);
        }

        [Fact]
        public void SupersededConnectionDisconnect_DoesNotReleaseCurrentReconnectSlot()
        {
            var gate = new PlayerAdmissionGate();

            gate.TryReserve("p1", "old");
            gate.TryReserve("p1", "new");

            Assert.False(gate.ReleaseConnection("old"));
            Assert.True(gate.IsReserved("p1"));
            Assert.Equal(1, gate.ActiveSlots);
        }

        [Fact]
        public void RepeatedValidationFromSameConnection_IsIdempotent()
        {
            var gate = new PlayerAdmissionGate();

            Assert.True(gate.TryReserve("p1", "c1").Accepted);
            Assert.True(gate.TryReserve("p1", "c1").Accepted);
            Assert.Equal(1, gate.ActiveSlots);
        }

        [Fact]
        public void InvalidIdentity_IsRejectedWithoutConsumingSlot()
        {
            var gate = new PlayerAdmissionGate();

            var result = gate.TryReserve("", "c1");

            Assert.False(result.Accepted);
            Assert.Equal(AdmissionRejectReason.InvalidControllerId, result.RejectReason);
            Assert.Equal(0, gate.ActiveSlots);
        }
    }
}
