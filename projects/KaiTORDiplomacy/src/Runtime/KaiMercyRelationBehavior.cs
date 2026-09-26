using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Guarantees an exact total +50 relation gain for the player's deliberate mercy choice
/// when releasing a defeated lord (subject only to Bannerlord's -100..100 relation cap).
/// Bannerlord/TOR can amplify ChangeRelationAction.ApplyPlayerRelation via perks/effects,
/// so after preserving the normal action/relative side effects we normalize the target
/// lord's final personal relation to the exact intended value.

/// </summary>
public sealed class KaiMercyRelationBehavior : CampaignBehaviorBase
{
    public const int PostBattleMercyRelationBonus = 50;

    private static int _dialogReleaseDepth;

    public override void RegisterEvents()
    {
        CampaignEvents.HeroPrisonerReleased.AddNonSerializedListener(this, OnHeroPrisonerReleased);
    }

    public override void SyncData(IDataStore dataStore)
    {
    }

    internal readonly struct MercyReleaseState
    {
        internal MercyReleaseState(Hero hero, int relationBefore)
        {
            Hero = hero;
            RelationBefore = relationBefore;
        }

        internal Hero Hero { get; }
        internal int RelationBefore { get; }
    }

    internal static MercyReleaseState BeginDialogRelease()
    {
        _dialogReleaseDepth++;
        var hero = Hero.OneToOneConversationHero;
        var relationBefore = hero != null && Hero.MainHero != null ? Hero.MainHero.GetRelation(hero) : 0;

        KaiRuntimeLog.Write(
            "MERCY_RELEASE_BEGIN",
            $"hero={hero?.StringId ?? "null"}; relationBefore={relationBefore}; depth={_dialogReleaseDepth}");

        return new MercyReleaseState(hero, relationBefore);
    }

    internal static void CompleteDialogRelease(MercyReleaseState state, string source)
    {
        try
        {
            var hero = state.Hero;
            if (hero == null || hero == Hero.MainHero || !hero.IsLord || Hero.MainHero == null)
                return;

            var relationAfterVanilla = Hero.MainHero.GetRelation(hero);
            var vanillaDelta = relationAfterVanilla - state.RelationBefore;
            var requestedTarget = Math.Clamp(
                state.RelationBefore + PostBattleMercyRelationBonus,
                -100,
                100);
            var topUp = Math.Max(0, requestedTarget - relationAfterVanilla);

            if (topUp > 0)
            {
                // Preserve Bannerlord/TOR relation-action side effects (including relatives),
                // then normalize the direct relation below because the action may be amplified
                // by perks/effects and therefore is not numerically exact.
                ChangeRelationAction.ApplyPlayerRelation(
                    hero,
                    topUp,
                    affectRelatives: true,
                    showQuickNotification: false);
            }

            var relationAfterAction = Hero.MainHero.GetRelation(hero);
            var relationAfter = NormalizeDirectRelation(hero, requestedTarget);
            var actualDelta = relationAfter - state.RelationBefore;
            var targetDelta = requestedTarget - state.RelationBefore;

            KaiRuntimeLog.Write(
                "MERCY_RELEASE",
                $"hero={hero.StringId}; name={hero.Name}; source={source}; relationBefore={state.RelationBefore}; relationAfterVanilla={relationAfterVanilla}; vanillaDelta={vanillaDelta}; topUp={topUp}; relationAfterAction={relationAfterAction}; requestedTarget={requestedTarget}; relationAfter={relationAfter}; actualDelta={actualDelta}; targetDelta={targetDelta}; requestedDelta={PostBattleMercyRelationBonus}");

            MBInformationManager.AddQuickInformation(
                new TextObject($"{hero.Name}: милосердие +{actualDelta} к отношениям."),
                2500,
                hero.CharacterObject,
                null,
                string.Empty);
        }
        catch (Exception ex)
        {
            KaiRuntimeLog.Exception("MERCY_RELEASE_FAILED", ex, $"source={source}");
        }
        finally
        {
            _dialogReleaseDepth = Math.Max(0, _dialogReleaseDepth - 1);
        }
    }

    private static int NormalizeDirectRelation(Hero hero, int targetRelation)
    {
        if (hero == null || Hero.MainHero == null)
            return 0;

        targetRelation = Math.Clamp(targetRelation, -100, 100);

        // Set from the same side that GetRelation() is read from.
        Hero.MainHero.SetPersonalRelation(hero, targetRelation);
        var relationAfter = Hero.MainHero.GetRelation(hero);

        // Defensive fallback for builds/mod combinations where the pair is stored
        // from the counterpart side.
        if (relationAfter != targetRelation)
        {
            hero.SetPersonalRelation(Hero.MainHero, targetRelation);
            relationAfter = Hero.MainHero.GetRelation(hero);
        }

        return relationAfter;
    }

    private static void OnHeroPrisonerReleased(
        Hero prisoner,
        PartyBase formerCaptorParty,
        IFaction capturerFaction,
        EndCaptivityDetail detail,
        bool showNotification)
    {
        if (prisoner == null || prisoner == Hero.MainHero || !prisoner.IsLord)
            return;

        var isMainPartyCaptor = formerCaptorParty == PartyBase.MainParty;

        KaiRuntimeLog.Write(
            "MERCY_RELEASE_EVENT",
            $"hero={prisoner.StringId}; detail={detail}; formerCaptor={formerCaptorParty?.Name?.ToString() ?? "null"}; mainPartyCaptor={isMainPartyCaptor}; dialogDepth={_dialogReleaseDepth}");

        // The lord-conversation paths are handled by exact Harmony patches below.
        // Suppress the event fallback while one of those methods is executing,
        // otherwise the same deliberate release could be rewarded twice.
        if (_dialogReleaseDepth > 0)
            return;

        // Fallback for a deliberate release from the player's prisoner roster.
        // Automatic post-battle cleanup is intentionally not rewarded here.
        if (detail != EndCaptivityDetail.ReleasedByChoice || !isMainPartyCaptor)
            return;

        var relationBefore = Hero.MainHero?.GetRelation(prisoner) ?? 0;
        var requestedTarget = Math.Clamp(
            relationBefore + PostBattleMercyRelationBonus,
            -100,
            100);
        var requestedActionDelta = Math.Max(0, requestedTarget - relationBefore);

        if (requestedActionDelta > 0)
        {
            ChangeRelationAction.ApplyPlayerRelation(
                prisoner,
                requestedActionDelta,
                affectRelatives: true,
                showQuickNotification: false);
        }

        var relationAfterAction = Hero.MainHero?.GetRelation(prisoner) ?? relationBefore;
        var relationAfter = NormalizeDirectRelation(prisoner, requestedTarget);
        var actualDelta = relationAfter - relationBefore;
        var targetDelta = requestedTarget - relationBefore;

        KaiRuntimeLog.Write(
            "MERCY_RELEASE",
            $"hero={prisoner.StringId}; name={prisoner.Name}; source=ReleasedByChoiceEvent; detail={detail}; relationBefore={relationBefore}; requestedActionDelta={requestedActionDelta}; relationAfterAction={relationAfterAction}; requestedTarget={requestedTarget}; relationAfter={relationAfter}; actualDelta={actualDelta}; targetDelta={targetDelta}; requestedDelta={PostBattleMercyRelationBonus}");

        MBInformationManager.AddQuickInformation(
            new TextObject($"{prisoner.Name}: милосердие +{actualDelta} к отношениям."),
            2500,
            prisoner.CharacterObject,
            null,
            string.Empty);
    }
}

[HarmonyPatch(
    typeof(LordConversationsCampaignBehavior),
    nameof(LordConversationsCampaignBehavior.conversation_talk_lord_defeat_to_lord_release_on_consequence))]
internal static class KaiPostBattleMercyReleasePatch
{
    [HarmonyPrefix]
    private static void Prefix(out KaiMercyRelationBehavior.MercyReleaseState __state)
        => __state = KaiMercyRelationBehavior.BeginDialogRelease();

    [HarmonyPostfix]
    private static void Postfix(KaiMercyRelationBehavior.MercyReleaseState __state)
        => KaiMercyRelationBehavior.CompleteDialogRelease(__state, "defeated_lord_release");
}

[HarmonyPatch(
    typeof(LordConversationsCampaignBehavior),
    nameof(LordConversationsCampaignBehavior.conversation_talk_lord_freed_to_lord_release_on_consequence))]
internal static class KaiFreedPrisonerMercyReleasePatch
{
    [HarmonyPrefix]
    private static void Prefix(out KaiMercyRelationBehavior.MercyReleaseState __state)
        => __state = KaiMercyRelationBehavior.BeginDialogRelease();

    [HarmonyPostfix]
    private static void Postfix(KaiMercyRelationBehavior.MercyReleaseState __state)
        => KaiMercyRelationBehavior.CompleteDialogRelease(__state, "freed_prisoner_release");
}
