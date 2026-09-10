using Common.Commands;
using Coop.Core.Server.Connections;
using GameInterface.Services.ObjectManager;
using GameInterface.Services.Players;
using Newtonsoft.Json;
using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;

namespace Coop.Core.Server.Commands
{
    /// <summary>
    /// Emits the authoritative four-player campaign identity and movement snapshot consumed by
    /// KaiTOR runtime validators. Diagnostics only; it does not mutate admission or persistent
    /// player state.
    /// </summary>
    public sealed class KaiTORFourPlayerSnapshotCommand : ICoopCommand
    {
        private readonly IPlayerManager playerManager;
        private readonly IPlayerAdmissionGate admissionGate;
        private readonly IObjectManager objectManager;

        public KaiTORFourPlayerSnapshotCommand(
            IPlayerManager playerManager,
            IPlayerAdmissionGate admissionGate,
            IObjectManager objectManager)
        {
            this.playerManager = playerManager;
            this.admissionGate = admissionGate;
            this.objectManager = objectManager;
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
                .Select(player =>
                {
                    var partyResolved = objectManager.TryGetObject(player.MobilePartyId, out MobileParty party);
                    var heroResolved = objectManager.TryGetObject(player.HeroId, out Hero hero);
                    return new
                    {
                        controllerId = player.ControllerId,
                        heroId = player.HeroId,
                        mobilePartyId = player.MobilePartyId,
                        clanId = player.ClanId,
                        characterObjectId = player.CharacterObjectId,
                        connected = playerManager.IsConnected(player),
                        heroResolved,
                        heroIsDead = heroResolved && hero.IsDead,
                        heroIsPrisoner = heroResolved && hero.IsPrisoner,
                        prisonerPartyId = heroResolved ? hero.PartyBelongedToAsPrisoner?.StringId : null,
                        partyResolved,
                        partyActive = partyResolved && party.IsActive,
                        mapEventId = partyResolved ? party.MapEvent?.StringId : null,
                        positionX = partyResolved ? (float?)party.Position.X : null,
                        positionY = partyResolved ? (float?)party.Position.Y : null,
                    };
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
