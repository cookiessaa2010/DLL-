using System;
using System.Collections.Generic;
using System.Linq;
using KaiTOR.Diplomacy.Decisions;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Conservative NAP AI. A run creates at most one proposal. AI-AI proposals continue
/// to use their own council. An AI->player proposal is instead placed in the player's
/// council and can never be accepted on the player's behalf.
/// </summary>
public sealed class KaiDiplomacyAiBehavior : CampaignBehaviorBase
{
    private const int MinimumIndividualAcceptance = 15;
    private const int MinimumCombinedAcceptance = 50;
    private const int IncomingPlayerOfferCooldownDays = 30;
    private const string IncomingCooldownSaveKey = "kaitor_ai_player_nap_offer_cooldown_v1";

    private Dictionary<string, double> _incomingCooldownByKingdom = new();

    public override void RegisterEvents()
    {
        CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
    }

    public override void SyncData(IDataStore dataStore)
    {
        dataStore.SyncData(IncomingCooldownSaveKey, ref _incomingCooldownByKingdom);
        _incomingCooldownByKingdom ??= new Dictionary<string, double>();
    }

    private void OnWeeklyTick()
    {
        var diplomacy = Campaign.Current?.GetCampaignBehavior<KaiDiplomacyBehavior>();
        if (diplomacy == null || !diplomacy.RuntimeEnabled)
            return;

        CleanupCooldowns();

        // Keep one proposal per weekly pass. Player-facing proposals have priority
        // because the old implementation had no inbound path at all.
        if (TryQueueIncomingPlayerProposal(diplomacy))
            return;

        TryQueueAiToAiProposal(diplomacy);
    }

    private bool TryQueueIncomingPlayerProposal(KaiDiplomacyBehavior diplomacy)
    {
        var playerKingdom = Clan.PlayerClan?.Kingdom;
        if (playerKingdom == null ||
            playerKingdom.IsEliminated ||
            playerKingdom.RulingClan == null ||
            playerKingdom.RulingClan != Clan.PlayerClan)
            return false;

        IncomingCandidate best = null;
        foreach (var aiKingdom in Kingdom.All
                     .Where(k => k != null &&
                                 !k.IsEliminated &&
                                 k != playerKingdom &&
                                 k.RulingClan != null)
                     .OrderBy(k => k.StringId, StringComparer.Ordinal))
        {
            if (GetIncomingCooldown(aiKingdom.StringId) > CampaignTime.Now.ToDays)
                continue;
            if (HasPendingIncomingDecision(playerKingdom, aiKingdom))
                continue;
            if (!diplomacy.CanCreateNonAggressionPact(playerKingdom, aiKingdom, KaiDiplomacyBehavior.DefaultNapDays, out _))
                continue;

            var aiAcceptance = diplomacy.GetNapAcceptanceScore(aiKingdom, playerKingdom);
            var playerCouncilAffinity = diplomacy.GetNapAcceptanceScore(playerKingdom, aiKingdom);
            if (aiAcceptance < MinimumIndividualAcceptance)
                continue;

            var combined = aiAcceptance + playerCouncilAffinity;
            if (combined < MinimumCombinedAcceptance)
                continue;

            var minimumInfluence = KaiDiplomacyBehavior.NapProposalInfluenceCost + KaiDiplomacyBehavior.AiInfluenceReserve;
            if (aiKingdom.RulingClan.Influence < minimumInfluence)
                continue;

            if (best == null || combined > best.CombinedScore ||
                (combined == best.CombinedScore && string.CompareOrdinal(aiKingdom.StringId, best.Proposer.StringId) < 0))
                best = new IncomingCandidate(aiKingdom, combined);
        }

        if (best == null)
            return false;

        // The foreign ruling clan pays the normal proposal cost. The decision itself
        // lives in the player's kingdom with cost 0 so the player is never charged
        // merely for receiving an offer.
        ChangeClanInfluenceAction.Apply(best.Proposer.RulingClan, -KaiDiplomacyBehavior.NapProposalInfluenceCost);

        var decision = new KaiNonAggressionPactDecision(
            playerKingdom.RulingClan,
            best.Proposer,
            KaiDiplomacyBehavior.DefaultNapDays,
            true);

        playerKingdom.AddDecision(decision, false);
        _incomingCooldownByKingdom[best.Proposer.StringId] =
            CampaignTime.Now.ToDays + IncomingPlayerOfferCooldownDays;

        KaiRuntimeLog.Write(
            "AI_NAP_PROPOSAL_TO_PLAYER",
            $"from={best.Proposer.StringId}; to={playerKingdom.StringId}; duration={KaiDiplomacyBehavior.DefaultNapDays}; aiInfluenceCost={KaiDiplomacyBehavior.NapProposalInfluenceCost}; cooldown={IncomingPlayerOfferCooldownDays}d; score={best.CombinedScore}");

        return true;
    }

    private static void TryQueueAiToAiProposal(KaiDiplomacyBehavior diplomacy)
    {
        var playerKingdom = Clan.PlayerClan?.Kingdom;
        var kingdoms = Kingdom.All
            .Where(k => k != null && !k.IsEliminated && k != playerKingdom && k.RulingClan != null)
            .OrderBy(k => k.StringId, StringComparer.Ordinal)
            .ToArray();

        Candidate best = null;
        for (var i = 0; i < kingdoms.Length; i++)
        {
            for (var j = i + 1; j < kingdoms.Length; j++)
            {
                var first = kingdoms[i];
                var second = kingdoms[j];
                if (!diplomacy.CanCreateNonAggressionPact(first, second, KaiDiplomacyBehavior.DefaultNapDays, out _))
                    continue;
                if (HasPendingNapDecision(first, second) || HasPendingNapDecision(second, first))
                    continue;

                var firstScore = diplomacy.GetNapAcceptanceScore(first, second);
                var secondScore = diplomacy.GetNapAcceptanceScore(second, first);
                if (firstScore < MinimumIndividualAcceptance || secondScore < MinimumIndividualAcceptance)
                    continue;

                var combined = firstScore + secondScore;
                if (combined < MinimumCombinedAcceptance)
                    continue;

                var proposer = firstScore >= secondScore ? first : second;
                var target = proposer == first ? second : first;
                var minimumInfluence = KaiDiplomacyBehavior.NapProposalInfluenceCost + KaiDiplomacyBehavior.AiInfluenceReserve;
                if (proposer.RulingClan.Influence < minimumInfluence)
                    continue;

                if (best == null || combined > best.CombinedScore ||
                    (combined == best.CombinedScore &&
                     string.CompareOrdinal(TreatyKey.For(first, second), TreatyKey.For(best.Proposer, best.Target)) < 0))
                    best = new Candidate(proposer, target, combined);
            }
        }

        if (best == null)
            return;

        var decision = new KaiNonAggressionPactDecision(
            best.Proposer.RulingClan,
            best.Target,
            KaiDiplomacyBehavior.DefaultNapDays);

        best.Proposer.AddDecision(decision, false);
        KaiRuntimeLog.Write(
            "AI_NAP_PROPOSAL",
            $"from={best.Proposer.StringId}; to={best.Target.StringId}; duration={KaiDiplomacyBehavior.DefaultNapDays}; score={best.CombinedScore}");
    }

    private static bool HasPendingIncomingDecision(Kingdom playerKingdom, Kingdom sourceAi)
        => playerKingdom.UnresolvedDecisions
            .OfType<KaiNonAggressionPactDecision>()
            .Any(d => d.IsIncomingAiProposal && d.TargetKingdom == sourceAi && !d.ShouldBeCancelled());

    private static bool HasPendingNapDecision(Kingdom source, Kingdom target)
        => source.UnresolvedDecisions
            .OfType<KaiNonAggressionPactDecision>()
            .Any(d => d.TargetKingdom == target && !d.ShouldBeCancelled());

    private double GetIncomingCooldown(string kingdomId)
        => _incomingCooldownByKingdom != null &&
           kingdomId != null &&
           _incomingCooldownByKingdom.TryGetValue(kingdomId, out var value) &&
           !double.IsNaN(value) &&
           !double.IsInfinity(value)
            ? value
            : 0d;

    private void CleanupCooldowns()
    {
        var now = CampaignTime.Now.ToDays;
        foreach (var key in _incomingCooldownByKingdom
                     .Where(x => x.Value <= now || double.IsNaN(x.Value) || double.IsInfinity(x.Value))
                     .Select(x => x.Key)
                     .ToArray())
            _incomingCooldownByKingdom.Remove(key);
    }

    private sealed class Candidate
    {
        public Candidate(Kingdom proposer, Kingdom target, int combinedScore)
        {
            Proposer = proposer;
            Target = target;
            CombinedScore = combinedScore;
        }

        public Kingdom Proposer { get; }
        public Kingdom Target { get; }
        public int CombinedScore { get; }
    }

    private sealed class IncomingCandidate
    {
        public IncomingCandidate(Kingdom proposer, int combinedScore)
        {
            Proposer = proposer;
            CombinedScore = combinedScore;
        }

        public Kingdom Proposer { get; }
        public int CombinedScore { get; }
    }
}
