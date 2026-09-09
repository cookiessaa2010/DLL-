using Common.Commands;
using Coop.Core.Server.Connections;
using GameInterface.Services.Players;
using Newtonsoft.Json;
using System;
using System.Linq;

namespace Coop.Core.Server.Commands
{
    /// <summary>
    /// Emits the authoritative four-player campaign identity snapshot consumed by
    /// Test-KaiTORFourPlayerSnapshot.ps1. Diagnostics only; it does not mutate admission
    /// or persistent player state.
    /// </summary>
    public sealed class KaiTORFourPlayerSnapshotCommand : ICoopCommand
    {
        private readonly IPlayerManager playerManager;
        private readonly IPlayerAdmissionGate admissionGate;

        public KaiTORFourPlayerSnapshotCommand(
            IPlayerManager playerManager,
            IPlayerAdmissionGate admissionGate)
        {
            this.playerManager = playerManager;
            this.admissionGate = admissionGate;
        }

        public string Prefix => "coop.debug.kaitor";
        public string Name => "snapshot4p";
        public string Description => "Emits KaiTOR's authoritative four-player shared-map snapshot as JSON.";
        public CoopCommandSide Side => CoopCommandSide.Server;
        public IExpectedArgs[] ExpectedArgs { get; } = Array.Empty<IExpectedArgs>();

        public CoopCommandResult ProcessCommand(ICoopCommandArgs args)
        {
            var players = playerManager.Players
                .OrderBy(player => player.ControllerId, StringComparer.Ordinal)
                .Select(player => new
                {
                    controllerId = player.ControllerId,
                    heroId = player.HeroId,
                    mobilePartyId = player.MobilePartyId,
                    clanId = player.ClanId,
                    characterObjectId = player.CharacterObjectId,
                    connected = playerManager.IsConnected(player),
                })
                .ToArray();

            var snapshot = new
            {
                activeAdmissionSlots = admissionGate.ActiveSlots,
                playerRegistryCount = players.Length,
                players,
            };

            var json = JsonConvert.SerializeObject(snapshot);
            return new CoopCommandResult(true, "KAITOR_4P_SNAPSHOT_JSON=" + json);
        }
    }
}
