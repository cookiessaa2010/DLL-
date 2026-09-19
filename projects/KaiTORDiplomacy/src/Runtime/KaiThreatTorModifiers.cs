using System;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Threat is an additive reputation modifier only. TOR hard rejections and model ownership
/// remain intact.
/// </summary>
[HarmonyPatch]
internal static class KaiThreatAlliancePatch
{
    private static MethodBase TargetMethod()
    {
        var type = AccessTools.TypeByName("TOR_Core.Models.TORAllianceModel");
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

        var threat = Campaign.Current?.GetCampaignBehavior<KaiThreatBehavior>()?.GetThreat(proposingKingdom) ?? 0f;
        if (threat <= 0.01f)
            return;

        __result.Add(
            -(threat * 0.35f),
            new TextObject("{=kaitor_diplomacy_threat_modifier}Expansion threat"));
    }
}

[HarmonyPatch]
internal static class KaiThreatTradePatch
{
    private static MethodBase TargetMethod()
    {
        var type = AccessTools.TypeByName("TOR_Core.Models.TORTradeAgreementModel");
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
        if (kingdom == null || targetKingdom == null || __result <= 0f)
            return;

        var threat = Campaign.Current?.GetCampaignBehavior<KaiThreatBehavior>()?.GetThreat(kingdom) ?? 0f;
        __result = Math.Max(0f, __result - threat * 0.25f);
    }
}

[HarmonyPatch]
internal static class KaiThreatWarPatch
{
    private static MethodBase TargetMethod()
    {
        var type = AccessTools.TypeByName("TOR_Core.Models.TORDiplomacyModel");
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

        var threat = Campaign.Current?.GetCampaignBehavior<KaiThreatBehavior>()?.GetThreat(second) ?? 0f;
        __result += threat * 0.45f;
    }
}

[HarmonyPatch]
internal static class KaiThreatPeacePatch
{
    private static MethodBase TargetMethod()
    {
        var type = AccessTools.TypeByName("TOR_Core.Models.TORDiplomacyModel");
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

        var threat = Campaign.Current?.GetCampaignBehavior<KaiThreatBehavior>()?.GetThreat(second) ?? 0f;
        __result -= threat * 0.25f;
    }
}
