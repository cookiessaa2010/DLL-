using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.ObjectSystem;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Refreshes TOR service NPCs whose identity is driven by settlement/owner culture.
/// Landmark services (Nuln engineer, Altdorf prestige noble, fixed cult priests,
/// shrines, Dawi Karak guilds and Lithanel envoys) remain TOR-owned and are never
/// cloned into arbitrary settlements.
/// </summary>
internal static class TorCulturalServiceBridge
{
    private const string SpellTrainerBehaviorType =
        "TOR_Core.CampaignMechanics.SpellTrainers.SpellTrainerInTownBehavior";
    private const string EnchanterBehaviorType =
        "TOR_Core.CampaignMechanics.SpellTrainers.EnchanterTownBehavior";
    private const string OathGoldBehaviorType =
        "TOR_Core.CampaignMechanics.Menagery.OathGoldBehavior";
    private const string EonirEnvoyBehaviorType =
        "TOR_Core.CampaignMechanics.Menagery.EonirFavorEnvoyTownBehavior";
    private const string BountyMasterBehaviorType =
        "TOR_Core.CampaignMechanics.BountyMaster.BountyMasterCampaignBehavior";
    private const string TeefBehaviorType =
        "TOR_Core.CampaignMechanics.CustomResourceBehavior.TeefBehavior";

    private const string EmpireAlchemistTemplate = "tor_enchanter_empire_0";
    private const string EmpireBountyMasterTemplate = "tor_bountymaster_empire_0";
    private const string GreenskinQuartermasterTemplate = "tor_kwartamasta_greenskins_0";

    public static bool Validate(CultureObject targetCulture, Settlement settlement, out string reason)
    {
        reason = string.Empty;
        if (settlement == null) return true;

        // Kwartamastas are an owner-culture service for both towns and castles.
        // TOR normally fixes them on ownership changes and weekly ticks; KaiTOR forces
        // the same validation immediately after paid cultural conversion.
        var teef = FindBehavior(TeefBehaviorType);
        if (teef == null ||
            GetMethod(teef, "ValidateKwartaMasters") == null ||
            GetMethod(teef, "GetKwartamastaForSettlement") == null)
        {
            reason = "TOR Greenskin Kwartamasta refresh contract was not found.";
            return false;
        }

        if (targetCulture.StringId == "aserai" &&
            MBObjectManager.Instance.GetObject<CharacterObject>(GreenskinQuartermasterTemplate) == null)
        {
            reason = "TOR Greenskin Kwartamasta template was not found.";
            return false;
        }

        if (!settlement.IsTown) return true;

        // The Empire bounty master is culture-driven and can be missed when an Empire
        // owner captures a non-Empire town because TOR's owner-change callback runs
        // before delayed settlement assimilation changes Settlement.Culture.
        var bounty = FindBehavior(BountyMasterBehaviorType);
        if (bounty == null ||
            GetField(bounty, "_settlementToBountyMasterMap") == null ||
            GetMethod(bounty, "GetBountyMasterForTown") == null ||
            GetMethod(bounty, "CreateBountyMaster") == null)
        {
            reason = "TOR Empire Bounty Master refresh contract was not found.";
            return false;
        }

        if (targetCulture.StringId == "empire" &&
            MBObjectManager.Instance.GetObject<CharacterObject>(EmpireBountyMasterTemplate) == null)
        {
            reason = "TOR Empire Bounty Master template was not found.";
            return false;
        }

        var spellTrainer = FindBehavior(SpellTrainerBehaviorType);
        if (spellTrainer == null ||
            GetField(spellTrainer, "_settlementToTrainerMap") == null ||
            GetMethod(spellTrainer, "CreateTrainer") == null)
        {
            reason = "TOR SpellTrainerInTownBehavior refresh contract was not found.";
            return false;
        }

        var enchanter = FindBehavior(EnchanterBehaviorType);
        if (enchanter == null ||
            GetField(enchanter, "_settlementToEnchanterMap") == null ||
            GetMethod(enchanter, "CreateEnchanter") == null)
        {
            reason = "TOR EnchanterTownBehavior refresh contract was not found.";
            return false;
        }

        var cultureMapField = GetField(enchanter, "_cultureToTemplateMap");
        var cultureMap = cultureMapField?.GetValue(enchanter) as IDictionary;
        if (cultureMap == null || !cultureMap.Contains(targetCulture.StringId))
        {
            reason = $"TOR has no enchanter/alchemist service mapping for culture '{targetCulture.StringId}'.";
            return false;
        }

        // Dawi guild services are intentionally geography-gated by TOR's IsDwarfKarak().
        if (targetCulture.StringId == "sturgia" && IsTorSettlementKind("IsDwarfKarak", settlement))
        {
            var oathGold = FindBehavior(OathGoldBehaviorType);
            if (oathGold == null || GetMethod(oathGold, "SpawnGuildmastersIfNeeded") == null)
            {
                reason = "TOR Dawi Karak guild refresh contract was not found.";
                return false;
            }
        }

        // Eonir envoys are a Lithanel landmark, not a generic Eonir-town service.
        if (targetCulture.StringId == "eonir" && IsTorSettlementKind("IsTorLithanel", settlement))
        {
            var envoys = FindBehavior(EonirEnvoyBehaviorType);
            if (envoys == null || GetMethod(envoys, "EnforceEnvoyLocation") == null)
            {
                reason = "TOR Lithanel envoy refresh contract was not found.";
                return false;
            }
        }

        return true;
    }

    public static bool Refresh(Settlement settlement, CultureObject targetCulture, out string reason)
    {
        reason = string.Empty;
        if (settlement == null) return true;
        if (!Validate(targetCulture, settlement, out reason)) return false;

        try
        {
            RefreshGreenskinQuartermaster(settlement, targetCulture);

            if (!settlement.IsTown) return true;

            RefreshEmpireBountyMaster(settlement, targetCulture);

            // Order matters. Several TOR enchanters reuse a spell-trainer/guild/envoy hero.
            // Rebuild the trainer mapping first, then remap the enchanter/alchemist service.
            RefreshSpellTrainer(settlement);
            RefreshEnchanter(settlement);
            RefreshDawiKarakGuildsIfApplicable(settlement, targetCulture);
            RefreshEonirEnvoysIfApplicable(settlement, targetCulture);
            return true;
        }
        catch (Exception ex)
        {
            reason = "TOR cultural service refresh failed: " + ex.GetBaseException().Message;
            return false;
        }
    }

    public static string Describe(CultureObject culture)
    {
        if (culture == null) return "services: culture missing";

        var issues = new System.Collections.Generic.List<string>();
        var spell = FindBehavior(SpellTrainerBehaviorType);
        if (spell == null || GetMethod(spell, "CreateTrainer") == null) issues.Add("spell trainer hook");

        var ench = FindBehavior(EnchanterBehaviorType);
        if (ench == null || GetMethod(ench, "CreateEnchanter") == null)
        {
            issues.Add("enchanter hook");
        }
        else
        {
            var map = GetField(ench, "_cultureToTemplateMap")?.GetValue(ench) as IDictionary;
            if (map == null || !map.Contains(culture.StringId)) issues.Add("enchanter/alchemist mapping");
        }

        if (culture.StringId == "empire")
        {
            var bounty = FindBehavior(BountyMasterBehaviorType);
            if (bounty == null || GetMethod(bounty, "CreateBountyMaster") == null ||
                GetField(bounty, "_settlementToBountyMasterMap") == null ||
                MBObjectManager.Instance.GetObject<CharacterObject>(EmpireBountyMasterTemplate) == null)
            {
                issues.Add("Empire bounty master");
            }
        }

        if (culture.StringId == "aserai")
        {
            var teef = FindBehavior(TeefBehaviorType);
            if (teef == null || GetMethod(teef, "ValidateKwartaMasters") == null ||
                MBObjectManager.Instance.GetObject<CharacterObject>(GreenskinQuartermasterTemplate) == null)
            {
                issues.Add("Greenskin Kwartamasta");
            }
        }

        return issues.Count == 0 ? "services FULL" : "services BLOCKED: " + string.Join(", ", issues);
    }

    private static void RefreshGreenskinQuartermaster(Settlement settlement, CultureObject targetCulture)
    {
        var behavior = FindBehavior(TeefBehaviorType)
            ?? throw new InvalidOperationException("TeefBehavior is missing.");

        var validate = GetMethod(behavior, "ValidateKwartaMasters")
            ?? throw new MissingMethodException(TeefBehaviorType, "ValidateKwartaMasters");
        validate.Invoke(behavior, Array.Empty<object>());

        if (targetCulture.StringId != "aserai") return;

        var get = GetMethod(behavior, "GetKwartamastaForSettlement")
            ?? throw new MissingMethodException(TeefBehaviorType, "GetKwartamastaForSettlement");
        var quartermaster = get.Invoke(behavior, new object[] { settlement }) as Hero;
        if (quartermaster == null)
            throw new InvalidOperationException($"TOR did not create a Greenskin Kwartamasta for {settlement.StringId}.");

        quartermaster.SupporterOf = settlement.OwnerClan;
    }

    private static void RefreshEmpireBountyMaster(Settlement settlement, CultureObject targetCulture)
    {
        var behavior = FindBehavior(BountyMasterBehaviorType)
            ?? throw new InvalidOperationException("BountyMasterCampaignBehavior is missing.");

        var map = GetRequiredMap(behavior, "_settlementToBountyMasterMap");
        var get = GetMethod(behavior, "GetBountyMasterForTown")
            ?? throw new MissingMethodException(BountyMasterBehaviorType, "GetBountyMasterForTown");
        var oldHero = get.Invoke(behavior, new object[] { settlement }) as Hero;

        if (targetCulture.StringId != "empire")
        {
            if (oldHero != null) DisableHeroAction.Apply(oldHero);
            map.Remove(settlement.StringId);
            return;
        }

        if (oldHero == null)
        {
            // A stale map entry would make TOR's Dictionary.Add in CreateBountyMaster throw.
            map.Remove(settlement.StringId);
            var create = GetMethod(behavior, "CreateBountyMaster")
                ?? throw new MissingMethodException(BountyMasterBehaviorType, "CreateBountyMaster");
            create.Invoke(behavior, new object[] { settlement });
            oldHero = get.Invoke(behavior, new object[] { settlement }) as Hero;
        }

        if (oldHero == null || oldHero.Template?.StringId != EmpireBountyMasterTemplate)
            throw new InvalidOperationException($"TOR did not create the Empire Bounty Master for {settlement.StringId}.");

        oldHero.SupporterOf = settlement.OwnerClan;
    }

    private static void RefreshSpellTrainer(Settlement settlement)
    {
        var behavior = FindBehavior(SpellTrainerBehaviorType)
            ?? throw new InvalidOperationException("SpellTrainerInTownBehavior is missing.");

        var map = GetRequiredMap(behavior, "_settlementToTrainerMap");
        // Do not kill the old trainer here: several TOR systems intentionally reuse
        // spell-trainer heroes as enchanters. Removing only this settlement mapping is safe.
        map.Remove(settlement.StringId);

        var create = GetMethod(behavior, "CreateTrainer")
            ?? throw new MissingMethodException(SpellTrainerBehaviorType, "CreateTrainer");
        create.Invoke(behavior, new object[] { settlement });
    }

    private static void RefreshEnchanter(Settlement settlement)
    {
        var behavior = FindBehavior(EnchanterBehaviorType)
            ?? throw new InvalidOperationException("EnchanterTownBehavior is missing.");

        var map = GetRequiredMap(behavior, "_settlementToEnchanterMap");
        Hero oldHero = null;
        if (map.Contains(settlement.StringId))
        {
            var get = GetMethod(behavior, "GetEnchanterForTown");
            oldHero = get?.Invoke(behavior, new object[] { settlement }) as Hero;
        }

        map.Remove(settlement.StringId);

        // Empire alchemists are per-town NPCs. Shared vampire/Bretonnian/Asrai/Eonir/Dawi/
        // Greenskin service heroes must never be killed because other towns reference them.
        if (oldHero?.Template?.StringId == EmpireAlchemistTemplate && oldHero.CurrentSettlement == settlement)
        {
            KillCharacterAction.ApplyByRemove(oldHero);
        }

        var create = GetMethod(behavior, "CreateEnchanter")
            ?? throw new MissingMethodException(EnchanterBehaviorType, "CreateEnchanter");
        create.Invoke(behavior, new object[] { settlement });
    }

    private static void RefreshDawiKarakGuildsIfApplicable(Settlement settlement, CultureObject targetCulture)
    {
        if (targetCulture.StringId != "sturgia" || !IsTorSettlementKind("IsDwarfKarak", settlement)) return;
        var behavior = FindBehavior(OathGoldBehaviorType);
        GetMethod(behavior, "SpawnGuildmastersIfNeeded")?.Invoke(behavior, Array.Empty<object>());
    }

    private static void RefreshEonirEnvoysIfApplicable(Settlement settlement, CultureObject targetCulture)
    {
        if (targetCulture.StringId != "eonir" || !IsTorSettlementKind("IsTorLithanel", settlement)) return;
        var behavior = FindBehavior(EonirEnvoyBehaviorType);
        GetMethod(behavior, "EnforceEnvoyLocation")?.Invoke(behavior, Array.Empty<object>());
    }

    private static IDictionary GetRequiredMap(CampaignBehaviorBase behavior, string fieldName)
    {
        var map = GetField(behavior, fieldName)?.GetValue(behavior) as IDictionary;
        if (map == null) throw new MissingFieldException(behavior.GetType().FullName, fieldName);
        return map;
    }

    private static FieldInfo GetField(CampaignBehaviorBase behavior, string name)
        => behavior?.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static MethodInfo GetMethod(CampaignBehaviorBase behavior, string name)
        => behavior?.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static CampaignBehaviorBase FindBehavior(string fullTypeName)
        => Campaign.Current?.CampaignBehaviorManager?
            .GetBehaviors<CampaignBehaviorBase>()
            .FirstOrDefault(x => string.Equals(x.GetType().FullName, fullTypeName, StringComparison.Ordinal));

    private static bool IsTorSettlementKind(string methodName, Settlement settlement)
    {
        var extensions = Type.GetType("TOR_Core.Extensions.SettlementExtensions, TOR_Core", false);
        var method = extensions?.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public);
        if (method == null) return false;
        return method.Invoke(null, new object[] { settlement }) is bool result && result;
    }
}
