using System;
using System.Linq;
using KaiTOR.Diplomacy.Decisions;
using TaleWorlds.CampaignSystem;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Conservative AI layer for non-aggression pacts. It never replaces TOR war/peace AI,
/// never proposes on behalf of the player's kingdom, and creates at most one council proposal per week.
/// </summary>
public sealed class KaiDiplomacyAiBehavior : CampaignBehaviorBase
{
    private const int MinimumIndividualAcceptance = 15;
    private const int MinimumCombinedAcceptance = 50;

    public override void RegisterEvents()
    {
        CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
    }

    public override void SyncData(IDataStore dataStore) { }

    private void OnWeeklyTick()
    {
        var diplomacy = Campaign.Current?.GetCampaignBehavior<KaiDiplomacyBehavior>();
        if (diplomacy == null || !diplomacy.RuntimeEnabled) return;

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
                if (!diplomacy.CanCreateNonAggressionPact(first, second, KaiDiplomacyBehavior.DefaultNapDays, out _)) continue;
                if (HasPendingNapDecision(first, second) || HasPendingNapDecision(second, first)) continue;

                var firstScore = diplomacy.GetNapAcceptanceScore(first, second);
                var secondScore = diplomacy.GetNapAcceptanceScore(second, first);
                if (firstScore < MinimumIndividualAcceptance || secondScore < MinimumIndividualAcceptance) continue;

                var combined = firstScore + secondScore;
                if (combined < MinimumCombinedAcceptance) continue;

                var proposer = firstScore >= secondScore ? first : second;
                var target = proposer == first ? second : first;
                var minimumInfluence = KaiDiplomacyBehavior.NapProposalInfluenceCost + KaiDiplomacyBehavior.AiInfluenceReserve;
                if (proposer.RulingClan.Influence < minimumInfluence) continue;

                if (best == null || combined > best.CombinedScore ||
                    (combined == best.CombinedScore && string.CompareOrdinal(TreatyKey.For(first, second), TreatyKey.For(best.Proposer, best.Target)) < 0))
                    best = new Candidate(proposer, target, combined);
            }
        }

        if (best == null) return;
        var decision = new KaiNonAggressionPactDecision(best.Proposer.RulingClan, best.Target, KaiDiplomacyBehavior.DefaultNapDays);
        best.Proposer.AddDecision(decision, false);
    }

    private static bool HasPendingNapDecision(Kingdom source, Kingdom target)
        => source.UnresolvedDecisions.OfType<KaiNonAggressionPactDecision>()
            .Any(d => d.TargetKingdom == target && !d.ShouldBeCancelled());

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
}
