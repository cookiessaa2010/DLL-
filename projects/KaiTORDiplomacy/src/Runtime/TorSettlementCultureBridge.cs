using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace KaiTOR.Diplomacy.Runtime;

internal static class TorSettlementCultureBridge
{
    private const string TorCompanionsBehaviorType = "TOR_Core.CampaignMechanics.Companions.TORCompanionsCampaignBehavior";
    private const string TorAssimilationBehaviorType = "TOR_Core.CampaignMechanics.Assimilation.AssimilationCampaignBehavior";

    // Mirrors TORConstants.Cultures.All in the current TOR 1.3.15 codebase.
    // Reflection validation below also checks TOR's runtime list so this fails closed if upstream changes it.
    private static readonly HashSet<string> KnownPlayableCultures = new(StringComparer.Ordinal)
    {
        "empire",      // Empire
        "vlandia",     // Bretonnia
        "khuzait",     // Sylvania
        "mousillon",   // Mousillon
        "battania",    // Asrai
        "eonir",       // Eonir
        "sturgia",     // Dawi
        "aserai",      // Greenskin
    };

    public static bool ValidateFullConversion(CultureObject targetCulture, Settlement rootSettlement, out string reason)
    {
        reason = string.Empty;
        if (targetCulture == null)
        {
            reason = "Target culture is null.";
            return false;
        }

        if (!IsCurrentTorPlayableCulture(targetCulture.StringId))
        {
            reason = $"Culture '{targetCulture.StringId}' is not in TOR's current playable settlement-culture list.";
            return false;
        }

        // Settlement recruitment and scene population primitives.
        if (targetCulture.BasicTroop == null || targetCulture.EliteBasicTroop == null)
        {
            reason = $"Culture '{targetCulture.StringId}' is missing BasicTroop or EliteBasicTroop.";
            return false;
        }

        if (targetCulture.MeleeMilitiaTroop == null || targetCulture.RangedMilitiaTroop == null ||
            targetCulture.MeleeEliteMilitiaTroop == null || targetCulture.RangedEliteMilitiaTroop == null)
        {
            reason = $"Culture '{targetCulture.StringId}' is missing one or more militia troop templates.";
            return false;
        }

        if (targetCulture.Villager == null || targetCulture.Townsman == null || targetCulture.Townswoman == null ||
            targetCulture.Guard == null || targetCulture.PrisonGuard == null ||
            targetCulture.CaravanMaster == null || targetCulture.CaravanGuard == null)
        {
            reason = $"Culture '{targetCulture.StringId}' is missing one or more settlement/guard/caravan population templates.";
            return false;
        }

        foreach (var settlement in GetAffectedSettlements(rootSettlement))
        {
            if (!ValidateNotableOccupations(targetCulture, settlement, out reason))
                return false;
        }

        var recruitment = Campaign.Current?.GetCampaignBehavior<RecruitmentCampaignBehavior>();
        if (recruitment == null)
        {
            reason = "Native RecruitmentCampaignBehavior was not found.";
            return false;
        }

        var recruitmentType = typeof(RecruitmentCampaignBehavior);
        if (recruitmentType.GetMethod("UpdateVolunteersOfNotablesInSettlement", BindingFlags.Instance | BindingFlags.NonPublic) == null)
        {
            reason = "Bannerlord 1.3.15 volunteer refresh method was not found.";
            return false;
        }

        if (rootSettlement?.IsTown == true)
        {
            if (targetCulture.BasicMercenaryTroops == null || targetCulture.BasicMercenaryTroops.Count == 0)
            {
                reason = $"Culture '{targetCulture.StringId}' has no tavern mercenary pool.";
                return false;
            }

            if (recruitmentType.GetMethod("UpdateCurrentMercenaryTroopAndCount", BindingFlags.Instance | BindingFlags.NonPublic) == null)
            {
                reason = "Bannerlord 1.3.15 tavern mercenary refresh method was not found.";
                return false;
            }

            var companions = FindBehavior(TorCompanionsBehaviorType);
            if (companions == null)
            {
                reason = "TOR companion behavior was not found.";
                return false;
            }

            var companionType = companions.GetType();
            if (companionType.GetMethod("RemoveWanderer", BindingFlags.Instance | BindingFlags.NonPublic) == null ||
                companionType.GetMethod("SpawnWanderer", BindingFlags.Instance | BindingFlags.NonPublic) == null)
            {
                reason = "TOR companion refresh methods were not found.";
                return false;
            }

            if (!HasCompanionTemplate(companions, targetCulture.StringId))
            {
                reason = $"TOR has no wanderer/companion template for culture '{targetCulture.StringId}'.";
                return false;
            }
        }

        var assimilation = FindBehavior(TorAssimilationBehaviorType);
        if (assimilation == null || assimilation.GetType().GetMethod("DailyTickParty", BindingFlags.Instance | BindingFlags.NonPublic) == null)
        {
            reason = "TOR assimilation caravan refresh hook was not found.";
            return false;
        }

        return true;
    }

    public static bool RefreshAfterCultureChange(Settlement rootSettlement, CultureObject targetCulture, out string reason)
    {
        reason = string.Empty;
        if (!ValidateFullConversion(targetCulture, rootSettlement, out reason))
            return false;

        try
        {
            RefreshVolunteersAndTavernMercenaries(rootSettlement);
            RefreshTownWanderer(rootSettlement, targetCulture);
            RefreshHomeCaravans(rootSettlement);
            return true;
        }
        catch (Exception ex)
        {
            reason = $"TOR post-conversion refresh failed: {ex.GetBaseException().Message}";
            return false;
        }
    }

    public static IEnumerable<string> DescribePlayableCultureSupport()
    {
        foreach (var cultureId in GetCurrentTorPlayableCultureIds().OrderBy(x => x, StringComparer.Ordinal))
        {
            var culture = TaleWorlds.ObjectSystem.MBObjectManager.Instance.GetObject<CultureObject>(cultureId);
            if (culture == null)
            {
                yield return $"{cultureId}: MISSING CultureObject";
                continue;
            }

            var issues = new List<string>();
            if (culture.BasicTroop == null) issues.Add("basic troop");
            if (culture.EliteBasicTroop == null) issues.Add("elite troop");
            if (culture.MeleeMilitiaTroop == null || culture.RangedMilitiaTroop == null) issues.Add("militia");
            if (culture.BasicMercenaryTroops == null || culture.BasicMercenaryTroops.Count == 0) issues.Add("tavern mercs");
            if (culture.CaravanGuard == null) issues.Add("caravan guard");

            var companions = FindBehavior(TorCompanionsBehaviorType);
            if (companions == null || !HasCompanionTemplate(companions, cultureId)) issues.Add("wanderer template");

            yield return issues.Count == 0
                ? $"{cultureId}: FULL"
                : $"{cultureId}: BLOCKED ({string.Join(", ", issues)})";
        }
    }

    private static void RefreshVolunteersAndTavernMercenaries(Settlement rootSettlement)
    {
        var recruitment = Campaign.Current.GetCampaignBehavior<RecruitmentCampaignBehavior>();
        var type = typeof(RecruitmentCampaignBehavior);
        var updateVolunteers = type.GetMethod("UpdateVolunteersOfNotablesInSettlement", BindingFlags.Instance | BindingFlags.NonPublic);
        var updateMercenaries = type.GetMethod("UpdateCurrentMercenaryTroopAndCount", BindingFlags.Instance | BindingFlags.NonPublic);

        foreach (var settlement in GetAffectedSettlements(rootSettlement))
            updateVolunteers!.Invoke(recruitment, new object[] { settlement });

        if (!rootSettlement.IsTown) return;

        // Clear any cached troop from the previous culture before forcing the normal native refresh.
        var mercenaryData = recruitment.GetMercenaryData(rootSettlement.Town);
        mercenaryData.ChangeMercenaryType(null, 0);
        updateMercenaries!.Invoke(recruitment, new object[] { rootSettlement.Town, true });
    }

    private static void RefreshTownWanderer(Settlement rootSettlement, CultureObject targetCulture)
    {
        if (!rootSettlement.IsTown || rootSettlement.IsUnderSiege) return;

        var companions = FindBehavior(TorCompanionsBehaviorType);
        if (companions == null) return;

        var type = companions.GetType();
        var remove = type.GetMethod("RemoveWanderer", BindingFlags.Instance | BindingFlags.NonPublic);
        var spawn = type.GetMethod("SpawnWanderer", BindingFlags.Instance | BindingFlags.NonPublic);

        var wanderers = rootSettlement.HeroesWithoutParty
            .Where(h => h != null && h.IsWanderer && h.CompanionOf == null)
            .ToList();

        foreach (var wanderer in wanderers.Where(h => h.Culture != targetCulture))
            remove!.Invoke(companions, new object[] { wanderer });

        var hasCorrect = rootSettlement.HeroesWithoutParty
            .Any(h => h != null && h.IsWanderer && h.CompanionOf == null && h.Culture == targetCulture);

        if (!hasCorrect)
        {
            var args = new object[] { rootSettlement, null };
            spawn!.Invoke(companions, args);
            if (args[1] is Hero created && created.Culture != targetCulture)
                throw new InvalidOperationException($"TOR spawned wanderer culture '{created.Culture?.StringId}', expected '{targetCulture.StringId}'.");
        }
    }

    private static void RefreshHomeCaravans(Settlement rootSettlement)
    {
        var assimilation = FindBehavior(TorAssimilationBehaviorType);
        if (assimilation == null) return;

        var dailyTickParty = assimilation.GetType().GetMethod("DailyTickParty", BindingFlags.Instance | BindingFlags.NonPublic);
        if (dailyTickParty == null) return;

        var affected = new HashSet<Settlement>(GetAffectedSettlements(rootSettlement));
        foreach (var party in MobileParty.All.ToList())
        {
            if (party == null || !party.IsCaravan || party.HomeSettlement == null) continue;
            if (!affected.Contains(party.HomeSettlement)) continue;
            dailyTickParty.Invoke(assimilation, new object[] { party });
        }
    }

    private static bool ValidateNotableOccupations(CultureObject culture, Settlement settlement, out string reason)
    {
        foreach (var notable in settlement.Notables)
        {
            if (notable == null) continue;
            if (!culture.NotableTemplates.Any(template => template != null && template.Occupation == notable.Occupation))
            {
                reason = $"Culture '{culture.StringId}' has no notable template for occupation '{notable.Occupation}' required by {settlement.Name}.";
                return false;
            }
        }

        reason = string.Empty;
        return true;
    }

    private static bool HasCompanionTemplate(CampaignBehaviorBase companions, string cultureId)
    {
        var field = companions.GetType().GetField("_companionTemplates", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field?.GetValue(companions) is not IDictionary dictionary || !dictionary.Contains(cultureId))
            return false;

        var value = dictionary[cultureId];
        if (value is ICollection collection) return collection.Count > 0;
        if (value is IEnumerable enumerable)
        {
            foreach (var _ in enumerable) return true;
        }
        return false;
    }

    private static CampaignBehaviorBase FindBehavior(string fullTypeName)
        => Campaign.Current?.CampaignBehaviorManager?
            .GetBehaviors<CampaignBehaviorBase>()
            .FirstOrDefault(x => string.Equals(x.GetType().FullName, fullTypeName, StringComparison.Ordinal));

    private static IEnumerable<Settlement> GetAffectedSettlements(Settlement rootSettlement)
    {
        if (rootSettlement == null) yield break;
        yield return rootSettlement;
        if (rootSettlement.BoundVillages == null) yield break;
        foreach (var village in rootSettlement.BoundVillages)
            if (village?.Settlement != null)
                yield return village.Settlement;
    }

    private static bool IsCurrentTorPlayableCulture(string cultureId)
        => GetCurrentTorPlayableCultureIds().Contains(cultureId, StringComparer.Ordinal);

    private static IReadOnlyList<string> GetCurrentTorPlayableCultureIds()
    {
        try
        {
            var type = Type.GetType("TOR_Core.Utilities.TORConstants+Cultures, TOR_Core", false);
            var field = type?.GetField("All", BindingFlags.Public | BindingFlags.Static);
            if (field?.GetValue(null) is IEnumerable<string> values)
                return values.Distinct(StringComparer.Ordinal).ToArray();
        }
        catch
        {
            // Fail over to the exact TOR 1.3.15 set pinned by this module.
        }

        return KnownPlayableCultures.OrderBy(x => x, StringComparer.Ordinal).ToArray();
    }
}
