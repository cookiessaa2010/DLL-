using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Lightweight in-game diplomacy front end using Bannerlord native menus/inquiries.
/// Internal module identifiers stay private; player-facing UI is presented as a normal game system.
/// </summary>
public sealed class KaiDiplomacyOfficeBehavior : CampaignBehaviorBase
{
    private const int DefaultNapDays = 90;
    private static readonly int[] NapDurations = { 30, 60, 90, 180 };

    public override void RegisterEvents()
    {
        CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
    }

    public override void SyncData(IDataStore dataStore)
    {
    }

    private void OnSessionLaunched(CampaignGameStarter starter)
    {
        AddOfficeMenuOption(starter, "town", "kaitor_diplomacy_office_town", 8);
        AddOfficeMenuOption(starter, "town_outside", "kaitor_diplomacy_office_town_outside", 8);
        AddOfficeMenuOption(starter, "castle", "kaitor_diplomacy_office_castle", 8);
    }

    private void AddOfficeMenuOption(CampaignGameStarter starter, string menuId, string optionId, int index)
    {
        starter.AddGameMenuOption(
            menuId,
            optionId,
            "Дипломатия",
            OfficeCondition,
            _ => ShowOffice(),
            false,
            index);
    }

    private bool OfficeCondition(MenuCallbackArgs args)
    {
        var diplomacy = Campaign.Current?.GetCampaignBehavior<KaiDiplomacyBehavior>();
        if (diplomacy == null || !diplomacy.RuntimeEnabled) return false;
        if (Clan.PlayerClan?.Kingdom == null) return false;
        args.optionLeaveType = GameMenuOption.LeaveType.Manage;
        return true;
    }

    private void ShowOffice()
    {
        var diplomacy = Campaign.Current?.GetCampaignBehavior<KaiDiplomacyBehavior>();
        var playerKingdom = Clan.PlayerClan?.Kingdom;
        if (diplomacy == null || playerKingdom == null) return;

        var canRule = IsPlayerRuler(playerKingdom);
        var options = new List<InquiryElement>
        {
            new("ledger", "Договоры и дипломатическое доверие", null),
            new("offer", "Предложить пакт о ненападении", null, canRule, canRule ? string.Empty : "Подписывать межгосударственные договоры может только правящий клан."),
            new("break", "Разорвать пакт о ненападении", null, canRule && HasActivePlayerPact(diplomacy, playerKingdom), canRule ? "Нет активного пакта, который можно разорвать." : "Разрывать межгосударственные договоры может только правящий клан."),
            new("marriage", "Брачные союзы", null),
            new("culture", "Смена народности поселений", null),
        };

        MBInformationManager.ShowMultiSelectionInquiry(
            new MultiSelectionInquiryData(
                "Дипломатия",
                $"Королевство: {playerKingdom.Name}. Ваша роль: {(canRule ? "правитель" : "вассал — межгосударственные договоры доступны только для просмотра")}.",
                options,
                true,
                1,
                1,
                "Открыть",
                "Закрыть",
                selected =>
                {
                    if (selected.Count == 0) return;
                    switch (selected[0].Identifier as string)
                    {
                        case "ledger": ShowLedger(diplomacy, playerKingdom); break;
                        case "offer": ShowNapTargets(diplomacy, playerKingdom); break;
                        case "break": ShowBreakTargets(diplomacy, playerKingdom); break;
                        case "marriage": ShowMarriageRules(); break;
                        case "culture": ShowCultureSupport(); break;
                    }
                },
                null),
            true,
            true);
    }

    private static void ShowLedger(KaiDiplomacyBehavior diplomacy, Kingdom playerKingdom)
    {
        var lines = new List<string>();
        foreach (var other in Kingdom.All.Where(k => k != null && k != playerKingdom && !k.IsEliminated).OrderBy(k => k.Name.ToString()))
        {
            var nap = diplomacy.IsNonAggressionPactActive(playerKingdom, other);
            var remaining = diplomacy.GetRemainingDays(playerKingdom, other);
            var trust = diplomacy.GetTrust(playerKingdom, other);
            var breaches = diplomacy.GetBreachCount(playerKingdom, other);
            var cooldown = diplomacy.GetNapCooldownRemainingDays(playerKingdom, other);
            if (!nap && trust == 0 && breaches == 0 && cooldown == 0) continue;

            lines.Add($"{other.Name}: пакт={(nap ? remaining + " дн." : "нет")}, доверие={trust}, нарушений={breaches}, блокировка={cooldown} дн.");
        }

        if (lines.Count == 0) lines.Add("У вашего королевства пока нет истории дипломатических договоров.");
        ShowText("Дипломатический журнал", string.Join("\n", lines));
    }

    private static void ShowNapTargets(KaiDiplomacyBehavior diplomacy, Kingdom playerKingdom)
    {
        var targets = new List<InquiryElement>();
        foreach (var other in Kingdom.All.Where(k => k != null && k != playerKingdom && !k.IsEliminated).OrderBy(k => k.Name.ToString()))
        {
            var allowed = diplomacy.CanCreateNonAggressionPact(playerKingdom, other, DefaultNapDays, out var reason);
            var score = diplomacy.GetNapAcceptanceScore(playerKingdom, other);
            var title = $"{other.Name}  [готовность {score:+#;-#;0}]";
            targets.Add(new InquiryElement(other, title, null, allowed, allowed ? string.Empty : reason));
        }

        MBInformationManager.ShowMultiSelectionInquiry(
            new MultiSelectionInquiryData(
                "Предложить пакт о ненападении",
                "Выберите королевство. Готовность к договору зависит от дипломатического доверия и отношений между правящими кланами. Особые ограничения мира проверяются в первую очередь.",
                targets,
                true,
                1,
                1,
                "Выбрать",
                "Отмена",
                selected =>
                {
                    if (selected.Count == 0 || selected[0].Identifier is not Kingdom target) return;
                    ShowNapDurations(diplomacy, playerKingdom, target);
                },
                null),
            true,
            true);
    }

    private static void ShowNapDurations(KaiDiplomacyBehavior diplomacy, Kingdom playerKingdom, Kingdom target)
    {
        var durations = NapDurations
            .Select(days => new InquiryElement(days, $"{days} дней кампании", null))
            .ToList();

        MBInformationManager.ShowMultiSelectionInquiry(
            new MultiSelectionInquiryData(
                $"Пакт с {target.Name}",
                $"Текущая готовность к договору: {diplomacy.GetNapAcceptanceScore(playerKingdom, target)}. При значении ниже 0 предложение обычно будет отклонено.",
                durations,
                true,
                1,
                1,
                "Предложить",
                "Отмена",
                selected =>
                {
                    if (selected.Count == 0 || selected[0].Identifier is not int days) return;
                    ResolvePlayerNapProposal(diplomacy, playerKingdom, target, days);
                },
                null),
            true,
            true);
    }

    private static void ResolvePlayerNapProposal(KaiDiplomacyBehavior diplomacy, Kingdom playerKingdom, Kingdom target, int days)
    {
        if (!diplomacy.CanCreateNonAggressionPact(playerKingdom, target, days, out var ruleReason))
        {
            ShowText("Предложение недоступно", ruleReason);
            return;
        }

        var score = diplomacy.GetNapAcceptanceScore(playerKingdom, target);
        if (score < 0)
        {
            ShowText("Предложение отклонено", $"{target.Name} отклоняет пакт. Готовность к договору: {score}. Улучшите отношения или восстановите дипломатическое доверие.");
            return;
        }

        if (diplomacy.TryCreateNonAggressionPact(playerKingdom, target, days, out _))
            ShowText("Договор подписан", $"{playerKingdom.Name} и {target.Name} заключили пакт о ненападении на {days} дней.");
        else
            ShowText("Не удалось заключить договор", "Условия договора изменились. Попробуйте снова.");
    }

    private static void ShowBreakTargets(KaiDiplomacyBehavior diplomacy, Kingdom playerKingdom)
    {
        var targets = Kingdom.All
            .Where(k => k != null && k != playerKingdom && !k.IsEliminated && diplomacy.IsNonAggressionPactActive(playerKingdom, k))
            .OrderBy(k => k.Name.ToString())
            .Select(k => new InquiryElement(k, $"{k.Name} — осталось {diplomacy.GetRemainingDays(playerKingdom, k)} дн.", null))
            .ToList();

        if (targets.Count == 0)
        {
            ShowText("Разрыв договора", "У вашего королевства нет активных пактов о ненападении.");
            return;
        }

        MBInformationManager.ShowMultiSelectionInquiry(
            new MultiSelectionInquiryData(
                "Разорвать пакт о ненападении",
                "Добровольный разрыв пакта снижает доверие на 10 и запрещает заключать новый пакт с этим королевством в течение 10 дней.",
                targets,
                true,
                1,
                1,
                "Разорвать",
                "Отмена",
                selected =>
                {
                    if (selected.Count == 0 || selected[0].Identifier is not Kingdom target) return;
                    InformationManager.ShowInquiry(new InquiryData(
                        "Подтверждение разрыва договора",
                        $"Разорвать пакт с {target.Name}? Доверие: -10. Новый пакт будет недоступен 10 дней.",
                        true,
                        true,
                        "Разорвать пакт",
                        "Отмена",
                        () =>
                        {
                            var broken = diplomacy.BreakNonAggressionPact(playerKingdom, target);
                            ShowText("Договор", broken ? $"Пакт с {target.Name} разорван." : "Этот пакт уже не действует.");
                        },
                        null), false, false);
                },
                null),
            true,
            true);
    }

    private static void ShowMarriageRules()
    {
        ShowText(
            "Брачные союзы",
            "Брачные предложения снова используют штатную систему Bannerlord: разговор с главой клана, выбор подходящей пары и стандартный экран торга за условия брака. ИИ также может сам присылать предложения о браке членам вашего клана.\n\n" +
            "Беременность, естественное старение и отдельная женская ветка дворфов пока не включаются. Для дворфов женские персонажи и беременность остаются отключены до отдельной проверки совместимости.");
    }

    private static void ShowCultureSupport()
    {
        var behavior = Campaign.Current?.GetCampaignBehavior<KaiCultureAssimilationBehavior>();
        var lines = behavior?.DescribeCultureSupport().ToArray() ?? Array.Empty<string>();
        ShowText(
            "Смена народности поселений",
            "Стоимость: 100 000 динаров. Требуется уровень клана 3+ и собственный город или замок. В меню собственного города появляется пункт «Сменить народность поселения». Поддерживаемая народность должна содержать необходимые шаблоны рекрутов, ополчения, наёмников таверны, караванов и странников.\n\n" +
            (lines.Length == 0 ? "Сведения о поддерживаемых народностях недоступны." : string.Join("\n", lines)));
    }

    private static bool HasActivePlayerPact(KaiDiplomacyBehavior diplomacy, Kingdom playerKingdom)
        => Kingdom.All.Any(k => k != null && k != playerKingdom && diplomacy.IsNonAggressionPactActive(playerKingdom, k));

    private static bool IsPlayerRuler(Kingdom kingdom)
        => kingdom?.RulingClan == Clan.PlayerClan && Clan.PlayerClan?.Leader == Hero.MainHero;

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
}
