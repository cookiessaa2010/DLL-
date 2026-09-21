using System;
using System.Collections.Generic;
using KaiTOR.Diplomacy.Models;
using TaleWorlds.CampaignSystem;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Read-only pregnancy lifecycle diagnostics. This behavior never changes fertility,
/// pregnancy state, parents or newborns; it only records the native Bannerlord/TOR
/// conception and birth events so live tests can follow a pregnancy end-to-end.
/// </summary>
public sealed class KaiPregnancyLifecycleBehavior : CampaignBehaviorBase
{
    public override void RegisterEvents()
    {
        CampaignEvents.OnChildConceivedEvent.AddNonSerializedListener(this, OnChildConceived);
        CampaignEvents.OnGivenBirthEvent.AddNonSerializedListener(this, OnGivenBirth);
    }

    public override void SyncData(IDataStore dataStore)
    {
        // Diagnostics only: no save state is owned by this behavior.
    }

    private static void OnChildConceived(Hero mother)
    {
        if (mother == null)
            return;

        try
        {
            var father = mother.Spouse;
            KaiRuntimeLog.Write(
                "PREGNANCY_STARTED",
                $"mother={Id(mother)}; father={Id(father)}; motherClan={ClanId(mother)}; fatherClan={ClanId(father)}; " +
                $"motherCulture={CultureId(mother)}; fatherCulture={CultureId(father)}; race={DescribeRace(mother)}; " +
                $"calendarAge={mother.Age:0.0}; biologicalAge={KaiRaceLifecycle.GetBiologicalAge(mother):0.0}; " +
                $"isPregnant={mother.IsPregnant}; campaignDay={CampaignTime.Now.ToDays:0.00}");
        }
        catch (Exception ex)
        {
            KaiRuntimeLog.Exception(
                "PREGNANCY_LIFECYCLE_LOG_FAIL",
                ex,
                $"stage=conceived; mother={Id(mother)}");
        }
    }

    private static void OnGivenBirth(Hero mother, List<Hero> aliveChildren, int stillbornCount)
    {
        if (mother == null)
            return;

        try
        {
            var father = mother.Spouse;
            var children = aliveChildren ?? new List<Hero>();

            KaiRuntimeLog.Write(
                "PREGNANCY_ENDED",
                $"mother={Id(mother)}; father={Id(father)}; reason=birth; aliveChildren={children.Count}; " +
                $"stillborn={Math.Max(0, stillbornCount)}; motherClan={ClanId(mother)}; race={DescribeRace(mother)}; " +
                $"campaignDay={CampaignTime.Now.ToDays:0.00}");

            foreach (var child in children)
            {
                if (child == null)
                    continue;

                KaiRuntimeLog.Write(
                    "CHILD_BORN",
                    $"child={Id(child)}; name={child.Name}; female={child.IsFemale}; mother={Id(mother)}; father={Id(father)}; " +
                    $"clan={ClanId(child)}; culture={CultureId(child)}; race={DescribeRace(child)}; " +
                    $"alive={child.IsAlive}; campaignDay={CampaignTime.Now.ToDays:0.00}");
            }
        }
        catch (Exception ex)
        {
            KaiRuntimeLog.Exception(
                "PREGNANCY_LIFECYCLE_LOG_FAIL",
                ex,
                $"stage=birth; mother={Id(mother)}; stillborn={stillbornCount}");
        }
    }

    private static string DescribeRace(Hero hero)
    {
        if (hero?.CharacterObject == null)
            return "unknown";
        if (KaiRaceLifecycle.IsDawi(hero))
            return "dawi";
        if (KaiRaceLifecycle.IsLongLivedElf(hero))
            return "elf";
        if (TorFamilySafety.IsVampire(hero))
            return "vampire";
        if (KaiRaceLifecycle.IsGreenskin(hero))
            return "greenskin";
        if (TorFamilySafety.IsUndead(hero))
            return "undead";

        return $"raceIndex:{hero.CharacterObject.Race}";
    }

    private static string Id(Hero hero) => hero?.StringId ?? "null";
    private static string ClanId(Hero hero) => hero?.Clan?.StringId ?? "null";
    private static string CultureId(Hero hero) => hero?.Culture?.StringId ?? "null";
}
