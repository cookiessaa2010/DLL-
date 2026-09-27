using System;
using System.Linq;
using KaiTOR.Diplomacy.Decisions;
using TaleWorlds.CampaignSystem;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Ruler conversation frontend for the existing NAP decision backend. It never creates
/// treaty state directly: choosing a duration only places KaiNonAggressionPactDecision
/// into the player's kingdom council.
/// </summary>
public sealed class KaiDiplomacyConversationBehavior : CampaignBehaviorBase
{
    private static readonly int[] NapDurations = { 30, 60, 90, 180 };

    public override void RegisterEvents()
    {
        CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
    }

    public override void SyncData(IDataStore dataStore) { }

    private void OnSessionLaunched(CampaignGameStarter starter)
    {
        starter.AddPlayerLine(
            "kaitor_diplomacy_talk_open",
            "hero_main_options",
            "kaitor_diplomacy_talk_reply",
            "Я хочу обсудить отношения между нашими державами.",
            CanOpenDiplomacyTalk,
            null,
            115,
            null,
            null);

        starter.AddDialogLine(
            "kaitor_diplomacy_talk_reply",
            "kaitor_diplomacy_talk_reply",
            "kaitor_diplomacy_talk_options",
            "Я слушаю. Какой договор вы хотите предложить?",
            CanOpenDiplomacyTalk,
            null,
            100,
            null);

        AddDurationLine(starter, 30, 120);
        AddDurationLine(starter, 60, 119);
        AddDurationLine(starter, 90, 118);
        AddDurationLine(starter, 180, 117);

        // Return to the normal lord conversation flow. Returning to hero_main_options
        // caused Bannerlord to immediately select kaitor_diplomacy_talk_open again
        // when it was the only valid player line, creating an infinite dialogue loop.
        starter.AddPlayerLine(
            "kaitor_diplomacy_talk_back",
            "kaitor_diplomacy_talk_options",
            "lord_pretalk",
            "Пока ничего. Вернёмся к этому позже.",
            null,
            null,
            90,
            null,
            null);
    }

    private static void AddDurationLine(CampaignGameStarter starter, int days, int priority)
    {
        starter.AddPlayerLine(
            "kaitor_diplomacy_nap_" + days,
            "kaitor_diplomacy_talk_options",
            "close_window",
            $"Предлагаю пакт о ненападении на {days} дней.",
            () => CanPropose(days),
            () => QueueDecision(days),
            priority,
            null,
            null);
    }

    private static bool CanOpenDiplomacyTalk()
    {
        if (!TryGetDiplomacyContext(out _, out _, out var diplomacy))
            return false;
        if (!diplomacy.RuntimeEnabled)
            return false;

        // Do not open an empty submenu. The entry is visible only if at least one
        // treaty duration is currently valid for this ruler.
        return NapDurations.Any(CanPropose);
    }

    private static bool TryGetDiplomacyContext(out Kingdom source, out Kingdom target, out KaiDiplomacyBehavior diplomacy)
    {
        source = null;
        target = null;
        diplomacy = null;

        var targetHero = Hero.OneToOneConversationHero;
        source = Clan.PlayerClan?.Kingdom;
        if (Campaign.Current == null || targetHero == null || source == null)
            return false;
        if (Clan.PlayerClan.IsUnderMercenaryService)
            return false;

        target = targetHero.MapFaction as Kingdom;
        if (target == null || target == source || target.IsEliminated || target.Leader != targetHero)
            return false;

        diplomacy = Campaign.Current.GetCampaignBehavior<KaiDiplomacyBehavior>();
        return diplomacy != null;
    }

    private static bool CanPropose(int days)
    {
        if (!TryGetDiplomacyContext(out var source, out var target, out var diplomacy))
            return false;
        if (!diplomacy.RuntimeEnabled)
            return false;
        if (Clan.PlayerClan.Influence < KaiDiplomacyBehavior.NapProposalInfluenceCost)
            return false;
        if (source.UnresolvedDecisions
            .OfType<KaiNonAggressionPactDecision>()
            .Any(d => d.TargetKingdom == target && !d.ShouldBeCancelled()))
            return false;
        if (!diplomacy.CanCreateNonAggressionPact(source, target, days, out _))
            return false;

        return diplomacy.GetNapAcceptanceScore(source, target) >= 0;
    }

    private static void QueueDecision(int days)
    {
        if (!TryGetDiplomacyContext(out var source, out var target, out var diplomacy))
            return;

        string reason = string.Empty;
        if (!diplomacy.RuntimeEnabled ||
            !diplomacy.CanCreateNonAggressionPact(source, target, days, out reason) ||
            Clan.PlayerClan.Influence < KaiDiplomacyBehavior.NapProposalInfluenceCost)
        {
            KaiRuntimeLog.Write("DIPLOMACY_DIALOGUE_FAILED", $"target={target.StringId}; days={days}; reason={reason}");
            return;
        }

        var decision = new KaiNonAggressionPactDecision(Clan.PlayerClan, target, days);
        source.AddDecision(decision, false);

        KaiRuntimeLog.Write(
            "DIPLOMACY_DIALOGUE_NAP",
            $"source={source.StringId}; target={target.StringId}; days={days}; cost={KaiDiplomacyBehavior.NapProposalInfluenceCost}");
    }
}
