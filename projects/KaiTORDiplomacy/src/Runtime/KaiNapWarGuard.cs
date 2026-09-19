using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Last-line NAP guard. The permission-model wrapper blocks ordinary war decisions,
/// while these prefixes also cover TOR paths that invoke DeclareWarAction or
/// FactionManager.DeclareWar directly.
/// </summary>
internal static class KaiNapWarGuard
{
    private static int _blockedCount;
    private static string _lastBlock = "none";

    public static int BlockedCount => _blockedCount;
    public static string LastBlock => _lastBlock;

    public static bool ShouldBlock(IFaction firstFaction, IFaction secondFaction, string path)
    {
        if (Campaign.Current == null ||
            firstFaction is not Kingdom first ||
            secondFaction is not Kingdom second ||
            first == second)
            return false;

        // Existing wars are never touched: the guard only prevents creation of a new war.
        if (FactionManager.IsAtWarAgainstFaction(first, second))
            return false;

        var diplomacy = Campaign.Current.GetCampaignBehavior<KaiDiplomacyBehavior>();
        if (diplomacy?.RuntimeEnabled != true || !diplomacy.IsNonAggressionPactActive(first, second))
            return false;

        var remaining = diplomacy.GetRemainingDays(first, second);
        _blockedCount++;
        _lastBlock = $"{first.StringId}<->{second.StringId}; path={path}; remaining={remaining}";

        KaiRuntimeLog.Write(
            "NAP_WAR_GUARD_BLOCK",
            $"source={first.StringId}; target={second.StringId}; path={path}; remaining={remaining}");

        return true;
    }

    public static string Describe()
        => $"NAP guard: blocked={_blockedCount}; last={_lastBlock}";
}

[HarmonyPatch(typeof(DeclareWarAction), "ApplyInternal")]
internal static class KaiDeclareWarActionGuardPatch
{
    private static bool Prefix(
        IFaction faction1,
        IFaction faction2,
        DeclareWarAction.DeclareWarDetail declareWarDetail)
    {
        if (faction1 is Kingdom first &&
            faction2 is Kingdom second &&
            IsMandatoryWorldWar(declareWarDetail))
        {
            var diplomacy = Campaign.Current?.GetCampaignBehavior<KaiDiplomacyBehavior>();
            if (diplomacy?.IsNonAggressionPactActive(first, second) == true)
            {
                diplomacy.ForceBreakNonAggressionPactForWar(
                    first,
                    second,
                    "mandatory:" + declareWarDetail);

                return true;
            }
        }

        return !KaiNapWarGuard.ShouldBlock(
            faction1,
            faction2,
            "DeclareWarAction.ApplyInternal:" + declareWarDetail);
    }

    private static bool IsMandatoryWorldWar(DeclareWarAction.DeclareWarDetail detail)
        => detail == DeclareWarAction.DeclareWarDetail.CausedByKingdomCreation ||
           detail == DeclareWarAction.DeclareWarDetail.CausedByRebellion ||
           detail == DeclareWarAction.DeclareWarDetail.CausedByClaimOnThrone;
}

[HarmonyPatch(typeof(FactionManager), nameof(FactionManager.DeclareWar))]
internal static class KaiFactionManagerWarGuardPatch
{
    private static bool Prefix(IFaction faction1, IFaction faction2)
        => !KaiNapWarGuard.ShouldBlock(
            faction1,
            faction2,
            "FactionManager.DeclareWar:direct");
}
