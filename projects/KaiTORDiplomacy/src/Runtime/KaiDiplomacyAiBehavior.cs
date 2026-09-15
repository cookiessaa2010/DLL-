using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Conservative AI layer for KaiTOR NAPs. It never replaces TOR's war/peace AI,
/// never signs on behalf of the player's kingdom, and creates at most one AI-AI pact per week.
/// </summary>
public sealed class KaiDiplomacyAiBehavior : CampaignBehaviorBase
{
    private const int AiNapDays = 90;
    private const int MinimumIndividualAcceptance = 15;
    private const int MinimumCombinedAcceptance = 50;

    public override void RegisterEvents()
    {
        CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
    }

    public override void SyncData(IDataStore dataStore)
    {
    }

    private void OnWeeklyTick()
    {
        var diplomacy = Campaign.Current?.GetCampaignBehavior<KaiDiplomacyBehavior>();
        if (diplomacy == null || !diplomacy.RuntimeEnabled) return;

        var playerKingdom = Clan.PlayerClan?.Kingdom;
        var kingdoms = Kingdom.All
            .Where(k => k != null && !k.IsEliminated && k != playerKingdom)
            .OrderBy(k => k.StringId, StringComparer.Ordinal)
            .ToArray();

        Candidate best = null;

        for (var i = 0; i < kingdoms.Length; i++)
        {
            for (var j = i + 1; j < kingdoms.Length; j++)
            {
                var first = kingdoms[i];
                var second = kingdoms[j];

                if (!diplomacy.CanCreateNonAggressionPact(first, second, AiNapDays, out _))
                    continue;

                var firstScore = diplomacy.GetNapAcceptanceScore(first, second);
                var secondScore = diplomacy.GetNapAcceptanceScore(second, first);
                if (firstScore < MinimumIndividualAcceptance || secondScore < MinimumIndividualAcceptance)
                    continue;

                var combined = firstScore + secondScore;
                if (combined < MinimumCombinedAcceptance)
                    continue;

                if (best == null || combined > best.CombinedScore ||
                    (combined == best.CombinedScore && string.CompareOrdinal(TreatyKey.For(first, second), TreatyKey.For(best.First, best.Second)) < 0))
                {
                    best = new Candidate(first, second, combined);
                }
            }
        }

        if (best != null)
            diplomacy.TryCreateNonAggressionPact(best.First, best.Second, AiNapDays, out _);
    }

    private sealed class Candidate
    {
        public Candidate(Kingdom first, Kingdom second, int combinedScore)
        {
            First = first;
            Second = second;
            CombinedScore = combinedScore;
        }

        public Kingdom First { get; }
        public Kingdom Second { get; }
        public int CombinedScore { get; }
    }
}
