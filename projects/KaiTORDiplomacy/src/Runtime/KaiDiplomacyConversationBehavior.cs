using System;
using System.Linq;
using KaiTOR.Diplomacy.Decisions;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Ruler conversation frontend for the existing NAP decision backend. It never creates
/// treaty state directly: choosing a duration only places KaiNonAggressionPactDecision
/// into the player's kingdom council.
/// </summary>
public sealed class KaiDiplomacyConversationBehavior : CampaignBehaviorBase
{
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

        starter.AddPlayerLine(
            "kaitor_diplomacy_talk_back",
            "kaitor_diplomacy_talk_options",
            "hero_main_options",
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
        var targetHero = Hero.OneToOneConversationHero;
        var source = Clan.PlayerClan?.Kingdom;
        if (Campaign.Current == null || targetHero == null || source == null)
            return false;
        if (Clan.PlayerClan.IsUnderMercenaryService)
            return false;

        var target = targetHero.MapFaction as Kingdom;
        if (target == null || target == source || target.IsEliminated || target.Leader != targetHero)
            return false;

        var diplomacy = Campaign.Current.GetCampaignBehavior<KaiDiplomacyBehavior>();
        return diplomacy?.RuntimeEnabled == true;
    }

    private static bool CanPropose(int days)
    {
        if (!CanOpenDiplomacyTalk())
            return false;

        var source = Clan.PlayerClan.Kingdom;
        var target = Hero.OneToOneConversationHero.MapFaction as Kingdom;
        var diplomacy = Campaign.Current.GetCampaignBehavior<KaiDiplomacyBehavior>();
        if (target == null || diplomacy == null)
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
        var source = Clan.PlayerClan?.Kingdom;
        var target = Hero.OneToOneConversationHero?.MapFaction as Kingdom;
        var diplomacy = Campaign.Current?.GetCampaignBehavior<KaiDiplomacyBehavior>();
        if (source == null || target == null || diplomacy == null)
            return;

        if (!diplomacy.CanCreateNonAggressionPact(source, target, days, out var reason) ||
            Clan.PlayerClan.Influence < KaiDiplomacyBehavior.NapProposalInfluenceCost)
        {
            if (string.IsNullOrWhiteSpace(reason) && Clan.PlayerClan.Influence < KaiDiplomacyBehavior.NapProposalInfluenceCost)
                reason = $"Недостаточно влияния: требуется {KaiDiplomacyBehavior.NapProposalInfluenceCost}.";

            KaiRuntimeLog.Write("DIPLOMACY_DIALOGUE_FAILED", $"target={target.StringId}; days={days}; reason={reason}");
            ShowQuick(string.IsNullOrWhiteSpace(reason) ? "Предложение сейчас недоступно." : reason);
            return;
        }

        var decision = new KaiNonAggressionPactDecision(Clan.PlayerClan, target, days);
        source.AddDecision(decision, false);

        KaiRuntimeLog.Write(
            "DIPLOMACY_DIALOGUE_NAP",
            $"source={source.StringId}; target={target.StringId}; days={days}; cost={KaiDiplomacyBehavior.NapProposalInfluenceCost}");

        ShowQuick($"Предложение пакта с {target.Name} на {days} дней вынесено на совет.");
    }

    private static void ShowQuick(string text)
    {
        MBInformationManager.AddQuickInformation(
            new TextObject(text ?? string.Empty),
            3500,
            null,
            null,
            string.Empty);
    }
}
