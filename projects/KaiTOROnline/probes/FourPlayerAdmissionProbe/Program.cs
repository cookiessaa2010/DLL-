using Coop.Core.Server.Connections;
using LiteNetLib;
using System;
using System.Runtime.CompilerServices;

static class Program
{
    private static void Main()
    {
        var gate = new PlayerAdmissionGate();

        var p1 = NewPeer();
        var p2 = NewPeer();
        var p3 = NewPeer();
        var p4 = NewPeer();
        var p5 = NewPeer();
        var p1Reconnect = NewPeer();

        Require(gate.ActiveSlots == 0, "gate must start empty");

        RequireAccepted(gate.TryAdmit("player-1", p1), reconnect: false, "player 1");
        RequireAccepted(gate.TryAdmit("player-2", p2), reconnect: false, "player 2");
        RequireAccepted(gate.TryAdmit("player-3", p3), reconnect: false, "player 3");
        RequireAccepted(gate.TryAdmit("player-4", p4), reconnect: false, "player 4");
        Require(gate.ActiveSlots == PlayerAdmissionGate.MaxPlayers, "four distinct players must occupy exactly four slots");

        var fifth = gate.TryAdmit("player-5", p5);
        Require(!fifth.Accepted, "fifth distinct player must be rejected");
        Require(fifth.RejectReason == AdmissionRejectReason.ServerFull, "fifth distinct player must receive ServerFull");
        Require(gate.ActiveSlots == 4, "rejected fifth player must not consume a slot");

        var invalid = gate.TryAdmit("", NewPeer());
        Require(!invalid.Accepted && invalid.RejectReason == AdmissionRejectReason.InvalidIdentity,
            "blank identity must be rejected without consuming a slot");
        Require(gate.ActiveSlots == 4, "invalid identity must not consume a slot");

        var duplicateSamePeer = gate.TryAdmit("player-1", p1);
        RequireAccepted(duplicateSamePeer, reconnect: false, "same peer idempotent validation");
        Require(gate.ActiveSlots == 4, "idempotent validation must not consume another slot");

        var reconnect = gate.TryAdmit("player-1", p1Reconnect);
        RequireAccepted(reconnect, reconnect: true, "player 1 reconnect");
        Require(ReferenceEquals(reconnect.SupersededPeer, p1), "reconnect must identify the superseded peer");
        Require(gate.ActiveSlots == 4, "reconnect must reuse the existing controller slot");

        Require(!gate.Release(p1), "late disconnect from superseded peer must not release the live reconnect slot");
        Require(gate.ActiveSlots == 4, "stale disconnect must leave four live slots");

        Require(gate.Release(p1Reconnect), "current reconnect peer must release its slot");
        Require(gate.ActiveSlots == 3, "disconnect of current peer must free one live slot");

        var fifthAfterRelease = gate.TryAdmit("player-5", p5);
        RequireAccepted(fifthAfterRelease, reconnect: false, "player 5 after slot release");
        Require(gate.ActiveSlots == 4, "freed slot must be reusable by a distinct player");

        Require(gate.Release(p2), "player 2 release");
        Require(gate.Release(p3), "player 3 release");
        Require(gate.Release(p4), "player 4 release");
        Require(gate.Release(p5), "player 5 release");
        Require(gate.ActiveSlots == 0, "all live slots must be released at the end");

        Console.WriteLine("PASS: KaiTOR hard four-player admission gate semantics validated.");
    }

    private static NetPeer NewPeer() =>
        (NetPeer)RuntimeHelpers.GetUninitializedObject(typeof(NetPeer));

    private static void RequireAccepted(AdmissionDecision decision, bool reconnect, string label)
    {
        Require(decision.Accepted, $"{label} must be accepted");
        Require(decision.IsReconnect == reconnect, $"{label} reconnect flag mismatch");
        Require(decision.RejectReason == AdmissionRejectReason.None, $"{label} must not have a rejection reason");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException("FourPlayerAdmissionProbe failed: " + message);
    }
}
