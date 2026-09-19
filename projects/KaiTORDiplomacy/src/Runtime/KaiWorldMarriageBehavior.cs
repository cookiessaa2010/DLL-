using System;
using System.Collections.Generic;
using System.Linq;
using KaiTOR.Diplomacy.Models;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.ComponentInterfaces;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Bounded weekly world-marriage pass. TOR disables conventional marriage globally;
/// the KaiTOR marriage model restores eligibility, while this behavior makes sure the
/// restored system actually produces dynasties instead of relying only on vanilla's
/// single-random-clan daily probe.
/// </summary>
public sealed class KaiWorldMarriageBehavior : CampaignBehaviorBase
{
    private const int MaxWorldMarriagesPerWeek = 12;
    private const int MaxMarriagePerKingdomPerWeek = 1;

    public override void RegisterEvents()
    {
        CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
    }

    public override void SyncData(IDataStore dataStore)
    {
        // Stateless. Existing spouse links are the source of truth.
    }

    private static void OnWeeklyTick()
    {
        var campaign = Campaign.Current;
        var model = campaign?.Models?.MarriageModel;
        if (campaign == null || model == null)
            return;

        var candidates = Hero.AllAliveHeroes
            .Where(h => IsCandidate(h, model))
            .OrderBy(h => h.StringId, StringComparer.Ordinal)
            .ToArray();

        var pairs = BuildPairs(candidates, model)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.First.StringId, StringComparer.Ordinal)
            .ThenBy(x => x.Second.StringId, StringComparer.Ordinal)
            .ToArray();

        var usedHeroes = new HashSet<string>(StringComparer.Ordinal);
        var kingdomCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var created = 0;

        KaiFamilyLog.Write("WORLD_MARRIAGE_SCAN", $"eligibleSingles={candidates.Length}; eligiblePairs={pairs.Length}; weeklyCap={MaxWorldMarriagesPerWeek}");

        foreach (var pair in pairs)
        {
            if (created >= MaxWorldMarriagesPerWeek)
                break;
            if (usedHeroes.Contains(pair.First.StringId) || usedHeroes.Contains(pair.Second.StringId))
                continue;
            if (!WithinKingdomCap(pair.First, kingdomCounts) || !WithinKingdomCap(pair.Second, kingdomCounts))
                continue;

            try
            {
                if (!model.IsCoupleSuitableForMarriage(pair.First, pair.Second))
                    continue;

                MarriageAction.Apply(pair.First, pair.Second, true);
                if (pair.First.Spouse != pair.Second || pair.Second.Spouse != pair.First)
                {
                    KaiFamilyLog.Write("WORLD_MARRIAGE_FAILED", $"first={pair.First.StringId}; second={pair.Second.StringId}; reason=MarriageAction_not_applied");
                    continue;
                }

                usedHeroes.Add(pair.First.StringId);
                usedHeroes.Add(pair.Second.StringId);
                IncrementKingdom(pair.First, kingdomCounts);
                IncrementKingdom(pair.Second, kingdomCounts);
                created++;

                var fertile = CanProduceBiologicalChildren(pair.First, pair.Second);
                KaiRuntimeLog.Write("WORLD_MARRIAGE_SUCCESS", $"first={pair.First.StringId}; second={pair.Second.StringId}; score={pair.Score:0.000000}; fertile={fertile}");
                KaiFamilyLog.Write("WORLD_MARRIAGE_SUCCESS", $"first={pair.First.StringId}; second={pair.Second.StringId}; firstKingdom={pair.First.Clan?.Kingdom?.StringId ?? "none"}; secondKingdom={pair.Second.Clan?.Kingdom?.StringId ?? "none"}; fertile={fertile}; score={pair.Score:0.000000}");
            }
            catch (Exception ex)
            {
                KaiFamilyLog.Write("WORLD_MARRIAGE_FAILED", $"first={pair.First.StringId}; second={pair.Second.StringId}; type={ex.GetType().Name}; message={ex.Message}");
            }
        }

        KaiFamilyLog.Write("WORLD_MARRIAGE_RESULT", $"created={created}; weeklyCap={MaxWorldMarriagesPerWeek}");
    }

    private static bool IsCandidate(Hero hero, MarriageModel model)
    {
        if (hero == null || hero == Hero.MainHero || hero.Clan == null || hero.Clan == Clan.PlayerClan)
            return false;
        if (hero.Clan.IsEliminated || hero.Spouse != null)
            return false;

        try
        {
            return model.IsSuitableForMarriage(hero);
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<Pair> BuildPairs(Hero[] heroes, MarriageModel model)
    {
        for (var i = 0; i < heroes.Length; i++)
        {
            var first = heroes[i];
            for (var j = i + 1; j < heroes.Length; j++)
            {
                var second = heroes[j];
                if (first.Clan == second.Clan)
                    continue;

                Pair pair = null;
                try
                {
                    if (model.ShouldNpcMarriageBetweenClansBeAllowed(first.Clan, second.Clan) &&
                        model.IsCoupleSuitableForMarriage(first, second))
                    {
                        var chance = model.NpcCoupleMarriageChance(first, second);
                        if (chance > 0f)
                        {
                            var sameKingdom = first.Clan.Kingdom != null && first.Clan.Kingdom == second.Clan.Kingdom;
                            var relation = first.Clan.GetRelationWithClan(second.Clan);
                            var score = chance * 1000f + (sameKingdom ? 10f : 0f) + Math.Max(-50, relation) / 100f;
                            pair = new Pair(first, second, score);
                        }
                    }
                }
                catch
                {
                    // Ignore malformed TOR heroes/pairs and continue the weekly scan.
                }

                if (pair != null)
                    yield return pair;
            }
        }
    }

    private static bool CanProduceBiologicalChildren(Hero first, Hero second)
    {
        if (!TorFamilySafety.CanUseVanillaPregnancy(first, second))
            return false;

        var female = first?.IsFemale == true ? first : second?.IsFemale == true ? second : null;
        return female != null && KaiRaceLifecycle.IsWithinLoreFertilityWindow(female);
    }

    private static bool WithinKingdomCap(Hero hero, Dictionary<string, int> counts)
    {
        var id = hero?.Clan?.Kingdom?.StringId;
        return string.IsNullOrEmpty(id) || !counts.TryGetValue(id, out var count) || count < MaxMarriagePerKingdomPerWeek;
    }

    private static void IncrementKingdom(Hero hero, Dictionary<string, int> counts)
    {
        var id = hero?.Clan?.Kingdom?.StringId;
        if (string.IsNullOrEmpty(id))
            return;
        counts[id] = counts.TryGetValue(id, out var count) ? count + 1 : 1;
    }

    private sealed class Pair
    {
        public Pair(Hero first, Hero second, float score)
        {
            First = first;
            Second = second;
            Score = score;
        }

        public Hero First { get; }
        public Hero Second { get; }
        public float Score { get; }
    }
}
