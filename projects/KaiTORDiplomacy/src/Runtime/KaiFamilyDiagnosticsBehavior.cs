using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace KaiTOR.Diplomacy.Runtime;

public sealed class KaiFamilyDiagnosticsBehavior : CampaignBehaviorBase
{
    public override void RegisterEvents()
    {
        CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        CampaignEvents.BeforeHeroesMarried.AddNonSerializedListener(this, OnBeforeHeroesMarried);
        CampaignEvents.OnChildConceivedEvent.AddNonSerializedListener(this, OnChildConceived);
        CampaignEvents.OnGivenBirthEvent.AddNonSerializedListener(this, OnGivenBirth);
        CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
    }

    public override void SyncData(IDataStore dataStore)
    {
        // Event diagnostics are append-only and deliberately carry no save state.
    }

    private static void OnSessionLaunched(CampaignGameStarter starter)
    {
        KaiFamilyLog.SessionStart();
        WriteGlobalSummary("SESSION_SUMMARY");
    }

    private static void OnBeforeHeroesMarried(Hero first, Hero second, bool showNotification)
    {
        if (first == null || second == null)
            return;

        KaiFamilyLog.Write(
            "MARRIAGE_SUCCESS",
            $"first={Id(first)}; firstName={Name(first)}; firstClan={ClanId(first)}; firstKingdom={KingdomId(first)}; firstCulture={CultureId(first)}; firstRace={Race(first)}; " +
            $"second={Id(second)}; secondName={Name(second)}; secondClan={ClanId(second)}; secondKingdom={KingdomId(second)}; secondCulture={CultureId(second)}; secondRace={Race(second)}; " +
            $"biologicalChildren={(Models.TorFamilySafety.CanUseVanillaPregnancy(first, second) ? "allowed" : "blocked")}");
    }

    private static void OnChildConceived(Hero mother)
    {
        var father = mother?.Spouse;
        KaiFamilyLog.Write(
            "PREGNANCY_CONCEIVED",
            $"mother={Id(mother)}; motherName={Name(mother)}; motherClan={ClanId(mother)}; motherKingdom={KingdomId(mother)}; motherCulture={CultureId(mother)}; motherRace={Race(mother)}; " +
            $"father={Id(father)}; fatherName={Name(father)}; fatherClan={ClanId(father)}; fatherKingdom={KingdomId(father)}; fatherCulture={CultureId(father)}; fatherRace={Race(father)}");
    }

    private static void OnGivenBirth(Hero mother, List<Hero> aliveChildren, int stillbornCount)
    {
        var father = mother?.Spouse;
        if (aliveChildren != null)
        {
            foreach (var child in aliveChildren.Where(x => x != null))
            {
                KaiFamilyLog.Write(
                    "BIRTH_SUCCESS",
                    $"mother={Id(mother)}; father={Id(father)}; child={Id(child)}; childName={Name(child)}; female={child.IsFemale}; clan={ClanId(child)}; kingdom={KingdomId(child)}; culture={CultureId(child)}; race={Race(child)}");
            }
        }

        if (stillbornCount > 0)
        {
            KaiFamilyLog.Write(
                "BIRTH_STILLBORN",
                $"mother={Id(mother)}; father={Id(father)}; count={stillbornCount}; clan={ClanId(mother)}; kingdom={KingdomId(mother)}");
        }
    }

    private static void OnDailyTick()
        => WriteGlobalSummary("DAILY_SUMMARY");

    private static void OnWeeklyTick()
    {
        if (Campaign.Current == null)
            return;

        foreach (var kingdom in Kingdom.All
                     .Where(k => k != null && !k.IsEliminated)
                     .OrderBy(k => k.StringId, StringComparer.Ordinal))
        {
            var heroes = Hero.AllAliveHeroes
                .Where(h => h != null && h.Clan?.Kingdom == kingdom)
                .ToArray();

            var marriedPairs = heroes.Count(h =>
                h.Spouse != null &&
                h.Spouse.IsAlive &&
                h.Spouse.Clan?.Kingdom == kingdom &&
                string.CompareOrdinal(h.StringId, h.Spouse.StringId) < 0);

            var pregnant = heroes.Count(h => h.IsPregnant);
            var adultAge = Campaign.Current.Models.AgeModel.HeroComesOfAge;
            var children = heroes.Count(h => h.Age < adultAge);
            var eligibleSingles = CountEligibleSingles(heroes);

            KaiFamilyLog.Write(
                "WEEKLY_KINGDOM",
                $"kingdom={kingdom.StringId}; name={kingdom.Name}; clans={kingdom.Clans.Count}; aliveHeroes={heroes.Length}; marriedPairs={marriedPairs}; pregnant={pregnant}; children={children}; eligibleSingles={eligibleSingles}");
        }

        WriteGlobalSummary("WEEKLY_SUMMARY");
    }

    private static void WriteGlobalSummary(string stage)
    {
        if (Campaign.Current == null)
            return;

        var alive = Hero.AllAliveHeroes.Where(h => h != null).ToArray();
        var marriedPairs = alive.Count(h =>
            h.Spouse != null &&
            h.Spouse.IsAlive &&
            string.CompareOrdinal(h.StringId, h.Spouse.StringId) < 0);

        var adultAge = Campaign.Current.Models.AgeModel.HeroComesOfAge;
        KaiFamilyLog.Write(
            stage,
            $"campaignDay={CampaignTime.Now.ToDays:0.00}; kingdoms={Kingdom.All.Count(k => k != null && !k.IsEliminated)}; clans={Clan.All.Count(c => c != null && !c.IsEliminated)}; " +
            $"aliveHeroes={alive.Length}; marriedPairs={marriedPairs}; pregnant={alive.Count(h => h.IsPregnant)}; children={alive.Count(h => h.Age < adultAge)}; eligibleSingles={CountEligibleSingles(alive)}");
    }

    private static int CountEligibleSingles(IEnumerable<Hero> heroes)
    {
        var model = Campaign.Current?.Models?.MarriageModel;
        if (model == null)
            return 0;

        var count = 0;
        foreach (var hero in heroes)
        {
            try
            {
                if (hero != null && hero.Spouse == null && model.IsSuitableForMarriage(hero))
                    count++;
            }
            catch
            {
                // A broken hero should not stop world diagnostics.
            }
        }
        return count;
    }

    private static string Id(Hero hero) => hero?.StringId ?? "null";
    private static string Name(Hero hero) => hero?.Name?.ToString() ?? string.Empty;
    private static string ClanId(Hero hero) => hero?.Clan?.StringId ?? "none";
    private static string KingdomId(Hero hero) => hero?.Clan?.Kingdom?.StringId ?? "none";
    private static string CultureId(Hero hero) => hero?.Culture?.StringId ?? "none";
    private static int Race(Hero hero) => hero?.CharacterObject?.Race ?? -1;
}
