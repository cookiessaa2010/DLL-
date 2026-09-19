using System;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Additive dynastic modifiers for TOR model results.
/// Hard TOR failures are never overridden and no TOR model instance is replaced.
/// </summary>
internal static class KaiDynasticTorModifier
{
    private const string TorDiplomacy = "TOR_Core.Models.TORDiplomacyModel";
    private const string TorAlliance = "TOR_Core.Models.TORAllianceModel";
    private const string TorTrade = "TOR_Core.Models.TORTradeAgreementModel";

    public static Type DiplomacyType => AccessTools.TypeByName(TorDiplomacy);
    public static Type AllianceType => AccessTools.TypeByName(TorAlliance);
    public static Type TradeType => AccessTools.TypeByName(TorTrade);

    public static float Strength(Kingdom first, Kingdom second)
        => Campaign.Current?.GetCampaignBehavior<KaiDynasticMarriageBehavior>()
            ?.GetDynasticStrength(first, second) ?? 0f;

    public static void Log(Kingdom first, Kingdom second, string path, float strength, float modifier)
    {
        if (strength <= 0.01f || Math.Abs(modifier) <= 0.01f)
            return;

        KaiRuntimeLog.Write(
            "DYNASTIC_MODIFIER",
            $"kingdoms={first.StringId}/{second.StringId}; path={path}; strength={strength:0.0}; modifier={modifier:0.0}");
    }
}

[HarmonyPatch]
internal static class KaiDynasticAllianceScorePatch
{
    private static MethodBase TargetMethod()
    {
        var type = KaiDynasticTorModifier.AllianceType;
        return type == null ? null : AccessTools.Method(
            type,
            "GetScoreOfStartingAlliance",
            new[]
            {
                typeof(Kingdom),
                typeof(Kingdom),
                typeof(IFaction),
                typeof(TextObject).MakeByRefType(),
                typeof(bool)
            });
    }

    private static void Postfix(
        Kingdom proposingKingdom,
        Kingdom targetKingdom,
        ref ExplainedNumber __result)
    {
        if (proposingKingdom == null || targetKingdom == null || __result.ResultNumber <= -5000f)
            return;

        var strength = KaiDynasticTorModifier.Strength(proposingKingdom, targetKingdom);
        var modifier = strength * 0.80f;
        if (modifier <= 0.01f)
            return;

        __result.Add(
            modifier,
            new TextObject("{=kaitor_diplomacy_dynastic_modifier}Dynastic ties"));

        KaiDynasticTorModifier.Log(
            proposingKingdom,
            targetKingdom,
            "TOR.Alliance",
            strength,
            modifier);
    }
}

[HarmonyPatch]
internal static class KaiDynasticTradeScorePatch
{
    private static MethodBase TargetMethod()
    {
        var type = KaiDynasticTorModifier.TradeType;
        return type == null ? null : AccessTools.Method(
            type,
            "GetScoreOfStartingTradeAgreement",
            new[]
            {
                typeof(Kingdom),
                typeof(Kingdom),
                typeof(Clan),
                typeof(TextObject).MakeByRefType(),
                typeof(bool)
            });
    }

    private static void Postfix(
        Kingdom kingdom,
        Kingdom targetKingdom,
        ref float __result)
    {
        // TOR returns 0 for hard/distance rejection. Dynastic ties never override it.
        if (kingdom == null || targetKingdom == null || __result <= 0f)
            return;

        var strength = KaiDynasticTorModifier.Strength(kingdom, targetKingdom);
        var modifier = strength * 0.40f;
        if (modifier <= 0.01f)
            return;

        __result += modifier;
        KaiDynasticTorModifier.Log(kingdom, targetKingdom, "TOR.Trade", strength, modifier);
    }
}

[HarmonyPatch]
internal static class KaiDynasticWarScorePatch
{
    private static MethodBase TargetMethod()
    {
        var type = KaiDynasticTorModifier.DiplomacyType;
        return type == null ? null : AccessTools.Method(
            type,
            "GetScoreOfDeclaringWar",
            new[]
            {
                typeof(IFaction),
                typeof(IFaction),
                typeof(Clan),
                typeof(TextObject).MakeByRefType(),
                typeof(bool)
            });
    }

    private static void Postfix(
        IFaction factionDeclaresWar,
        IFaction factionDeclaredWar,
        ref float __result)
    {
        if (factionDeclaresWar is not Kingdom first ||
            factionDeclaredWar is not Kingdom second ||
            __result <= -50000f)
            return;

        var strength = KaiDynasticTorModifier.Strength(first, second);
        var modifier = -(strength * 1.20f);
        if (strength <= 0.01f)
            return;

        __result += modifier;
        KaiDynasticTorModifier.Log(first, second, "TOR.War", strength, modifier);
    }
}

[HarmonyPatch]
internal static class KaiDynasticPeaceScorePatch
{
    private static MethodBase TargetMethod()
    {
        var type = KaiDynasticTorModifier.DiplomacyType;
        return type == null ? null : AccessTools.Method(
            type,
            "GetScoreOfDeclaringPeace",
            new[] { typeof(IFaction), typeof(IFaction) });
    }

    private static void Postfix(
        IFaction factionDeclaresPeace,
        IFaction factionDeclaredPeace,
        ref float __result)
    {
        if (factionDeclaresPeace is not Kingdom first ||
            factionDeclaredPeace is not Kingdom second ||
            __result <= -50000f)
            return;

        var strength = KaiDynasticTorModifier.Strength(first, second);
        var modifier = strength * 0.80f;
        if (modifier <= 0.01f)
            return;

        __result += modifier;
        KaiDynasticTorModifier.Log(first, second, "TOR.Peace", strength, modifier);
    }
}

[HarmonyPatch]
internal static class KaiDynasticPeaceForClanScorePatch
{
    private static MethodBase TargetMethod()
    {
        var type = KaiDynasticTorModifier.DiplomacyType;
        return type == null ? null : AccessTools.Method(
            type,
            "GetScoreOfDeclaringPeaceForClan",
            new[]
            {
                typeof(IFaction),
                typeof(IFaction),
                typeof(Clan),
                typeof(TextObject).MakeByRefType(),
                typeof(bool)
            });
    }

    private static void Postfix(
        IFaction factionDeclaresPeace,
        IFaction factionDeclaredPeace,
        ref float __result)
    {
        if (factionDeclaresPeace is not Kingdom first ||
            factionDeclaredPeace is not Kingdom second ||
            __result <= -50000f)
            return;

        var strength = KaiDynasticTorModifier.Strength(first, second);
        var modifier = strength * 0.80f;
        if (modifier <= 0.01f)
            return;

        __result += modifier;
        KaiDynasticTorModifier.Log(first, second, "TOR.PeaceClan", strength, modifier);
    }
}
