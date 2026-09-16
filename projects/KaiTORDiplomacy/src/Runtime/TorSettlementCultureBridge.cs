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

    private static readonly HashSet<string> KnownPlayableCultures = new(StringComparer.Ordinal)
    {
        "empire", "vlandia", "khuzait", "mousillon", "battania", "eonir", "sturgia", "aserai",
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
            if (rootSettlement.Owner?.Culture != targetCulture)
            {
                reason = $"Town owner culture '{rootSettlement.Owner?.Culture?.StringId ?? "<null>"}' does not match clan target culture '{targetCulture.StringId}'.";
                return false;
            }

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

            // TOR wanderer refresh is intentionally NOT a hard gate. TOR has changed the
            // visibility/name of these implementation methods between builds. The town's
            // main culture must never be blocked merely because an optional immediate
            // wanderer refresh cannot be invoked. TOR's own enter/weekly maintenance will
            // reconcile the wanderer later when the bridge is unavailable.

            if (!TorCulturalServiceBridge.Validate(targetCulture, rootSettlement, out reason))
                return false;

            if (!TorMarketCultureBridge.Validate(targetCulture, rootSettlement, out reason))
                return false;
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
            RefreshTownWandererBestEffort(rootSettlement, targetCulture);
            RefreshHomeCaravans(rootSettlement);
            if (!TorCulturalServiceBridge.Refresh(rootSettlement, targetCulture, out reason))
                return false;
            if (!TorMarketCultureBridge.Refresh(rootSettlement, targetCulture, out reason))
                return false;
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
            if (companions == null || !HasCompanionTemplate(companions, cultureId)) issues.Add("wanderer template (deferred refresh)");

            var services = TorCulturalServiceBridge.Describe(culture);
            if (!string.Equals(services, "services FULL", StringComparison.Ordinal)) issues.Add(services);

            var market = TorMarketCultureBridge.Describe(culture);
            if (!string.Equals(market, "market FULL", StringComparison.Ordinal)) issues.Add(market);

            yield return issues.Count == 0
                ? $"{cultureId}: FULL"
                : $"{cultureId}: BLOCKED/DEFERRED ({string.Join(", ", issues)})";
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

        var mercenaryData = recruitment.GetMercenaryData(rootSettlement.Town);
        mercenaryData.ChangeMercenaryType(null, 0);
        updateMercenaries!.Invoke(recruitment, new object[] { rootSettlement.Town, true });
    }

    private static void RefreshTownWandererBestEffort(Settlement rootSettlement, CultureObject targetCulture)
    {
        if (!rootSettlement.IsTown || rootSettlement.IsUnderSiege) return;

        try
        {
            var companions = FindBehavior(TorCompanionsBehaviorType);
            if (companions == null || !HasCompanionTemplate(companions, targetCulture.StringId)) return;

            var type = companions.GetType();
            var remove = type.GetMethod("RemoveWanderer", BindingFlags.Instance | BindingFlags.NonPublic);
            var spawn = type.GetMethod("SpawnWanderer", BindingFlags.Instance | BindingFlags.NonPublic);
            if (remove == null || spawn == null) return;

            var wanderers = rootSettlement.HeroesWithoutParty
                .Where(h => h != null && h.IsWanderer && h.CompanionOf == null)
                .ToList();

            foreach (var wanderer in wanderers.Where(h => h.Culture != targetCulture))
                remove.Invoke(companions, new object[] { wanderer });

            var hasCorrect = rootSettlement.HeroesWithoutParty
                .Any(h => h != null && h.IsWanderer && h.CompanionOf == null && h.Culture == targetCulture);

            if (!hasCorrect)
            {
                var args = new object[] { rootSettlement, null };
                spawn.Invoke(companions, args);
            }
        }
        catch
        {
            // Optional only. TOR will reconcile town wanderers through its own
            // settlement-enter/weekly behavior. Never fail the culture conversion here.
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
        }

        return KnownPlayableCultures.OrderBy(x => x, StringComparer.Ordinal).ToArray();
    }
}
