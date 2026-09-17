using System;
using System.Collections.Generic;
using System.Linq;
using KaiTOR.Diplomacy.Models;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.BarterSystem;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Native-facing diplomacy entry points. Treaty items are injected into Bannerlord's
/// ordinary barter screen when two kingdom rulers negotiate, while political marriage
/// starts from the existing lord diplomacy conversation and then uses the same barter UI.
/// No town/castle diplomacy menu is created here.
/// </summary>
public sealed class KaiDiplomaticNegotiationBehavior : CampaignBehaviorBase
{
    private static readonly int[] NapDurations = { 30, 60, 90, 180 };

    public override void RegisterEvents()
    {
        CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        CampaignEvents.BarterablesRequested.AddNonSerializedListener(this, OnBarterablesRequested);
    }

    public override void SyncData(IDataStore dataStore)
    {
    }

    private void OnSessionLaunched(CampaignGameStarter starter)
    {
        starter.AddPlayerLine(
            "kaitor_diplomacy_status_with_ruler",
            "lord_talk_speak_diplomacy_2",
            "lord_talk_speak_diplomacy_2",
            "Каковы сейчас дипломатические обязательства между нашими державами?",
            CanTalkToForeignRuler,
            ShowDiplomaticStatus,
            120,
            null,
            null);

        starter.AddPlayerLine(
            "kaitor_diplomacy_break_nap_with_ruler",
            "lord_talk_speak_diplomacy_2",
            "lord_talk_speak_diplomacy_2",
            "Я хочу обсудить расторжение нашего пакта о ненападении.",
            CanBreakNapWithConversationRuler,
            ConfirmBreakNap,
            120,
            null,
            null);

        starter.AddPlayerLine(
            "kaitor_political_marriage_with_ruler",
            "lord_talk_speak_diplomacy_2",
            "lord_talk_speak_diplomacy_2",
            "Я хочу предложить политический брачный союз между нашими правящими домами.",
            CanTalkToForeignRuler,
            BeginPoliticalMarriageSelection,
            120,
            null,
            null);
    }

    private void OnBarterablesRequested(BarterData args)
    {
        if (args == null || args.OffererHero != Hero.MainHero || args.OtherHero == null)
            return;

        if (args.GetBarterables().Any(item => item is KaiPoliticalMarriageBarterable))
            return;

        var playerKingdom = Clan.PlayerClan?.Kingdom;
        var targetKingdom = args.OtherHero.MapFaction as Kingdom;
        if (!IsRulerNegotiation(playerKingdom, targetKingdom, args.OtherHero))
            return;

        var diplomacy = Campaign.Current?.GetCampaignBehavior<KaiDiplomacyBehavior>();
        if (diplomacy == null || !diplomacy.RuntimeEnabled)
            return;

        foreach (var days in NapDurations)
        {
            if (!diplomacy.CanCreateNonAggressionPact(playerKingdom, targetKingdom, days, out _))
                continue;

            args.AddBarterable<OtherBarterGroup>(
                new KaiNonAggressionPactBarterable(Hero.MainHero, PartyBase.MainParty, playerKingdom, targetKingdom, days),
                false);
        }
    }

    private static bool CanTalkToForeignRuler()
    {
        var playerKingdom = Clan.PlayerClan?.Kingdom;
        var otherHero = Hero.OneToOneConversationHero;
        var targetKingdom = otherHero?.MapFaction as Kingdom;
        return IsRulerNegotiation(playerKingdom, targetKingdom, otherHero);
    }

    private static bool CanBreakNapWithConversationRuler()
    {
        if (!CanTalkToForeignRuler())
            return false;

        var diplomacy = Campaign.Current?.GetCampaignBehavior<KaiDiplomacyBehavior>();
        var playerKingdom = Clan.PlayerClan?.Kingdom;
        var targetKingdom = Hero.OneToOneConversationHero?.MapFaction as Kingdom;
        return diplomacy?.IsNonAggressionPactActive(playerKingdom, targetKingdom) == true;
    }

    private static bool IsRulerNegotiation(Kingdom playerKingdom, Kingdom targetKingdom, Hero otherHero)
    {
        if (Hero.MainHero == null || Clan.PlayerClan == null || playerKingdom == null || targetKingdom == null)
            return false;
        if (playerKingdom == targetKingdom || playerKingdom.IsEliminated || targetKingdom.IsEliminated)
            return false;
        if (playerKingdom.Leader != Hero.MainHero || playerKingdom.RulingClan != Clan.PlayerClan)
            return false;
        return targetKingdom.Leader == otherHero;
    }

    private static void ShowDiplomaticStatus()
    {
        var playerKingdom = Clan.PlayerClan?.Kingdom;
        var targetKingdom = Hero.OneToOneConversationHero?.MapFaction as Kingdom;
        var diplomacy = Campaign.Current?.GetCampaignBehavior<KaiDiplomacyBehavior>();
        var dynastic = Campaign.Current?.GetCampaignBehavior<KaiPoliticalMarriageBehavior>();
        if (playerKingdom == null || targetKingdom == null || diplomacy == null)
            return;

        var nap = diplomacy.IsNonAggressionPactActive(playerKingdom, targetKingdom)
            ? $"Пакт о ненападении: действует ещё {diplomacy.GetRemainingDays(playerKingdom, targetKingdom)} дн."
            : "Пакт о ненападении: отсутствует.";
        var bond = dynastic?.HasActiveBond(playerKingdom, targetKingdom) == true
            ? "Династический союз: действует."
            : "Династический союз: отсутствует.";
        var cooldown = diplomacy.GetNapCooldownRemainingDays(playerKingdom, targetKingdom);
        var cooldownText = cooldown > 0
            ? $"\nНовые пакты недоступны ещё {cooldown} дн."
            : string.Empty;

        ShowText(
            "Дипломатические отношения",
            $"{playerKingdom.Name} — {targetKingdom.Name}\n\n" +
            $"{nap}\n{bond}\n" +
            $"Доверие: {diplomacy.GetTrust(playerKingdom, targetKingdom):+0;-0;0}.\n" +
            $"Нарушений договоров: {diplomacy.GetBreachCount(playerKingdom, targetKingdom)}.{cooldownText}\n\n" +
            "Новый пакт о ненападении можно добавить в обычном окне обмена с правителем.");
    }

    private static void ConfirmBreakNap()
    {
        var playerKingdom = Clan.PlayerClan?.Kingdom;
        var targetKingdom = Hero.OneToOneConversationHero?.MapFaction as Kingdom;
        var diplomacy = Campaign.Current?.GetCampaignBehavior<KaiDiplomacyBehavior>();
        if (playerKingdom == null || targetKingdom == null || diplomacy == null)
            return;

        InformationManager.ShowInquiry(new InquiryData(
            "Расторгнуть пакт о ненападении",
            $"Досрочно расторгнуть пакт между {playerKingdom.Name} и {targetKingdom.Name}?\n\n" +
            "Это снизит дипломатическое доверие на 10 и закроет новый пакт на 10 дней.",
            true,
            true,
            "Расторгнуть",
            "Отмена",
            () =>
            {
                var removed = diplomacy.BreakNonAggressionPact(playerKingdom, targetKingdom);
                InformationManager.DisplayMessage(new InformationMessage(
                    removed ? "Пакт о ненападении расторгнут." : "Пакт уже не действует."));
            },
            null), false, false);
    }

    private static void BeginPoliticalMarriageSelection()
    {
        var playerKingdom = Clan.PlayerClan?.Kingdom;
        var targetKingdom = Hero.OneToOneConversationHero?.MapFaction as Kingdom;
        var behavior = Campaign.Current?.GetCampaignBehavior<KaiPoliticalMarriageBehavior>();
        if (behavior == null || !behavior.CanStartPoliticalMarriage(playerKingdom, targetKingdom, out var reason))
        {
            ShowText("Политический брачный союз", string.IsNullOrWhiteSpace(reason) ? "Сейчас такое соглашение невозможно." : reason);
            return;
        }

        var candidates = KaiPoliticalMarriageBehavior.GetRulingHouseCandidates(playerKingdom, allowMainHero: true)
            .Select(hero => new InquiryElement(
                hero,
                $"{hero.Name}, {Math.Max(0, (int)hero.Age)} лет",
                null,
                true,
                hero == Hero.MainHero ? "Правитель державы" : $"Правящий дом: {playerKingdom.RulingClan.Name}"))
            .ToList();

        if (candidates.Count == 0)
        {
            ShowText("Политический брачный союз", "В вашем правящем доме сейчас нет свободного кандидата для политического брака.");
            return;
        }

        MBInformationManager.ShowMultiSelectionInquiry(
            new MultiSelectionInquiryData(
                "Политический брачный союз",
                "Выберите представителя вашего правящего дома.",
                candidates,
                true,
                1,
                1,
                "Далее",
                "Отмена",
                selected =>
                {
                    if (selected.Count == 0 || selected[0].Identifier is not Hero ownCandidate)
                        return;
                    InformationManager.HideInquiry();
                    ShowTargetMarriageCandidates(playerKingdom, targetKingdom, ownCandidate);
                },
                null),
            true,
            true);
    }

    private static void ShowTargetMarriageCandidates(Kingdom playerKingdom, Kingdom targetKingdom, Hero ownCandidate)
    {
        var marriageModel = Campaign.Current?.Models?.MarriageModel;
        if (marriageModel == null)
            return;

        var candidates = KaiPoliticalMarriageBehavior.GetRulingHouseCandidates(targetKingdom, allowMainHero: false)
            .Where(hero => marriageModel.IsCoupleSuitableForMarriage(ownCandidate, hero))
            .Select(hero => new InquiryElement(
                hero,
                $"{hero.Name}, {Math.Max(0, (int)hero.Age)} лет",
                null,
                true,
                $"Правящий дом: {targetKingdom.RulingClan.Name}"))
            .ToList();

        if (candidates.Count == 0)
        {
            ShowText(
                "Политический брачный союз",
                $"В правящем доме {targetKingdom.Name} нет свободного кандидата, совместимого с {ownCandidate.Name}.");
            return;
        }

        MBInformationManager.ShowMultiSelectionInquiry(
            new MultiSelectionInquiryData(
                "Политический брачный союз",
                $"Кого из правящего дома {targetKingdom.Name} предложить в брак с {ownCandidate.Name}?",
                candidates,
                true,
                1,
                1,
                "Выбрать",
                "Отмена",
                selected =>
                {
                    if (selected.Count == 0 || selected[0].Identifier is not Hero targetCandidate)
                        return;
                    InformationManager.HideInquiry();
                    ConfirmPoliticalMarriage(playerKingdom, targetKingdom, ownCandidate, targetCandidate);
                },
                null),
            true,
            true);
    }

    private static void ConfirmPoliticalMarriage(Kingdom playerKingdom, Kingdom targetKingdom, Hero ownCandidate, Hero targetCandidate)
    {
        var childless = !TorFamilySafety.CanUseVanillaPregnancy(ownCandidate, targetCandidate);
        var fertilityText = childless
            ? "\n\nОсобенность союза: эта пара не сможет иметь биологических детей. Брак сохранит династические и политические права, но кровных наследников от этой пары не будет."
            : string.Empty;

        var body = $"Предложить политический брак: {ownCandidate.Name} и {targetCandidate.Name}?\n\n" +
                   $"Обязательная выплата правящему дому {targetKingdom.Name}: {KaiPoliticalMarriageBehavior.PoliticalMarriageCost:N0} динаров.\n" +
                   $"После заключения: +{KaiPoliticalMarriageBehavior.DynasticTrustBonus} дипломатического доверия, " +
                   $"+{KaiPoliticalMarriageBehavior.DynasticRulerRelationBonus} отношений правителей и пакт о ненападении на {KaiPoliticalMarriageBehavior.DynasticNapDays} дней." +
                   fertilityText +
                   "\n\nСделка будет показана в обычном окне обмена.";

        InformationManager.ShowInquiry(new InquiryData(
            "Политический брачный союз",
            body,
            true,
            true,
            "Перейти к обмену",
            "Отмена",
            () => StartPoliticalMarriageBarter(playerKingdom, targetKingdom, ownCandidate, targetCandidate),
            null), false, false);
    }

    private static void StartPoliticalMarriageBarter(Kingdom playerKingdom, Kingdom targetKingdom, Hero ownCandidate, Hero targetCandidate)
    {
        var behavior = Campaign.Current?.GetCampaignBehavior<KaiPoliticalMarriageBehavior>();
        var targetRuler = targetKingdom?.Leader;
        if (behavior == null || targetRuler == null ||
            !behavior.CanStartPoliticalMarriage(playerKingdom, targetKingdom, out var reason))
        {
            ShowText("Политический брачный союз", string.IsNullOrWhiteSpace(reason) ? "Условия соглашения изменились." : reason);
            return;
        }

        var politicalMarriage = new KaiPoliticalMarriageBarterable(
            Hero.MainHero,
            PartyBase.MainParty,
            playerKingdom,
            targetKingdom,
            ownCandidate,
            targetCandidate);

        BarterManager.Instance.StartBarterOffer(
            Hero.MainHero,
            targetRuler,
            PartyBase.MainParty,
            targetRuler.PartyBelongedTo?.Party,
            null,
            null,
            0,
            false,
            new Barterable[] { politicalMarriage });
    }

    private static void ShowText(string title, string body)
    {
        InformationManager.ShowInquiry(new InquiryData(
            title,
            body,
            true,
            false,
            "ОК",
            string.Empty,
            null,
            null), false, false);
    }

    private sealed class KaiNonAggressionPactBarterable : Barterable
    {
        private readonly Kingdom _playerKingdom;
        private readonly Kingdom _targetKingdom;
        private readonly int _days;

        public KaiNonAggressionPactBarterable(Hero owner, PartyBase ownerParty, Kingdom playerKingdom, Kingdom targetKingdom, int days)
            : base(owner, ownerParty)
        {
            _playerKingdom = playerKingdom;
            _targetKingdom = targetKingdom;
            _days = days;
        }

        public override string StringID => $"kaitor_nap_{_days}";

        public override TextObject Name => new($"Пакт о ненападении на {_days} дней");

        public override int GetUnitValueForFaction(IFaction faction)
        {
            if (faction != _targetKingdom && faction != _targetKingdom?.RulingClan)
                return 0;

            var diplomacy = Campaign.Current?.GetCampaignBehavior<KaiDiplomacyBehavior>();
            if (diplomacy == null)
                return -1000000;

            var acceptance = diplomacy.GetNapAcceptanceScore(_playerKingdom, _targetKingdom);
            var longTermPremium = Math.Max(0, _days - 90) * 200;
            return (acceptance - 10) * 1000 - longTermPremium;
        }

        public override bool IsCompatible(Barterable barterable)
            => barterable is not KaiNonAggressionPactBarterable;

        public override ImageIdentifier GetVisualIdentifier() => null;

        public override void Apply()
        {
            var diplomacy = Campaign.Current?.GetCampaignBehavior<KaiDiplomacyBehavior>();
            if (diplomacy == null)
                return;

            var success = diplomacy.TryCreateNonAggressionPact(_playerKingdom, _targetKingdom, _days, out var reason);
            InformationManager.DisplayMessage(new InformationMessage(
                success ? reason : "Пакт о ненападении не заключён: " + reason));
        }
    }

    private sealed class KaiPoliticalMarriageBarterable : Barterable
    {
        private readonly Kingdom _playerKingdom;
        private readonly Kingdom _targetKingdom;
        private readonly Hero _playerCandidate;
        private readonly Hero _targetCandidate;

        public KaiPoliticalMarriageBarterable(
            Hero owner,
            PartyBase ownerParty,
            Kingdom playerKingdom,
            Kingdom targetKingdom,
            Hero playerCandidate,
            Hero targetCandidate)
            : base(owner, ownerParty)
        {
            _playerKingdom = playerKingdom;
            _targetKingdom = targetKingdom;
            _playerCandidate = playerCandidate;
            _targetCandidate = targetCandidate;
        }

        public override string StringID => "kaitor_political_marriage";

        public override TextObject Name => new(
            $"Политический брак: {_playerCandidate.Name} и {_targetCandidate.Name} — {KaiPoliticalMarriageBehavior.PoliticalMarriageCost:N0} динаров");

        public override int GetUnitValueForFaction(IFaction faction)
        {
            if (faction == _targetKingdom || faction == _targetKingdom?.RulingClan)
                return KaiPoliticalMarriageBehavior.PoliticalMarriageCost;
            if (faction == _playerKingdom || faction == _playerKingdom?.RulingClan)
                return -KaiPoliticalMarriageBehavior.PoliticalMarriageCost;
            return 0;
        }

        public override bool IsCompatible(Barterable barterable)
            => barterable is not KaiPoliticalMarriageBarterable && barterable is not MarriageBarterable;

        public override ImageIdentifier GetVisualIdentifier()
            => _targetCandidate?.CharacterObject == null
                ? null
                : new ImageIdentifier(CharacterCode.CreateFrom(_targetCandidate.CharacterObject));

        public override string GetEncyclopediaLink()
            => _targetCandidate?.EncyclopediaLink ?? string.Empty;

        public override void Apply()
        {
            var behavior = Campaign.Current?.GetCampaignBehavior<KaiPoliticalMarriageBehavior>();
            if (behavior == null)
                return;

            var success = behavior.TryFinalizePoliticalMarriage(
                _playerKingdom,
                _targetKingdom,
                _playerCandidate,
                _targetCandidate,
                out var result);

            InformationManager.DisplayMessage(new InformationMessage(
                success ? result : "Политический брак не заключён: " + result));
        }
    }
}
