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
            new("ledger", "Договоры и отношения", null),
            new("offer", "Предложить пакт о ненападении", null, canRule, canRule ? string.Empty : "Заключать договоры от имени державы может только её правитель."),
            new("break", "Разорвать пакт о ненападении", null, canRule && HasActivePlayerPact(diplomacy, playerKingdom), canRule ? "Сейчас нет действующего пакта, который можно разорвать." : "Разрывать договоры от имени державы может только её правитель."),
            new("marriage", "Династические браки", null),
            new("culture", "Смена народности поселений", null),
        };

        var intro = canRule
            ? $"Отсюда решаются дипломатические дела державы {playerKingdom.Name}."
            : $"Вы можете ознакомиться с дипломатическими делами державы {playerKingdom.Name}. Решения о межгосударственных договорах принимает правитель.";

        MBInformationManager.ShowMultiSelectionInquiry(
            new MultiSelectionInquiryData(
                "Дипломатия",
                intro,
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

            var pactText = nap ? $"пакт о ненападении, ещё {remaining} дн." : "действующего пакта нет";
            var historyText = breaches > 0 ? $"; прежних нарушений договора: {breaches}" : string.Empty;
            var cooldownText = cooldown > 0 ? $"; новые переговоры возможны через {cooldown} дн." : string.Empty;
            lines.Add($"{other.Name}: {pactText}; отношения — {DescribeTrust(trust)}{historyText}{cooldownText}.");
        }

        if (lines.Count == 0) lines.Add("У вашего королевства пока нет заключённых пактов и заметной истории таких договоров.");
        ShowText("Дипломатический журнал", string.Join("\n", lines));
    }

    private static void ShowNapTargets(KaiDiplomacyBehavior diplomacy, Kingdom playerKingdom)
    {
        var targets = new List<InquiryElement>();
        foreach (var other in Kingdom.All.Where(k => k != null && k != playerKingdom && !k.IsEliminated).OrderBy(k => k.Name.ToString()))
        {
            var allowed = diplomacy.CanCreateNonAggressionPact(playerKingdom, other, DefaultNapDays, out var reason);
            var score = diplomacy.GetNapAcceptanceScore(playerKingdom, other);
            var title = $"{other.Name} — {DescribeReadiness(score)}";
            targets.Add(new InquiryElement(other, title, null, allowed, allowed ? string.Empty : ToPlayerFacingDiplomacyReason(reason)));
        }

        MBInformationManager.ShowMultiSelectionInquiry(
            new MultiSelectionInquiryData(
                "Предложить пакт о ненападении",
                "Выберите державу, к которой будут направлены послы. На ответ влияют прежние отношения, соблюдение договоров и текущая обстановка.",
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
            .Select(days => new InquiryElement(days, $"{days} дней", null))
            .ToList();

        MBInformationManager.ShowMultiSelectionInquiry(
            new MultiSelectionInquiryData(
                $"Пакт с {target.Name}",
                $"Послы оценивают отношение {target.Name} к предложению как {DescribeReadiness(diplomacy.GetNapAcceptanceScore(playerKingdom, target))}. Выберите срок договора.",
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
            ShowText("Посольство не отправлено", ToPlayerFacingDiplomacyReason(ruleReason));
            return;
        }

        var score = diplomacy.GetNapAcceptanceScore(playerKingdom, target);
        if (score < 0)
        {
            ShowText("Предложение отклонено", $"{target.Name} не желает связывать себя таким договором. Возможно, со временем или после улучшения отношений переговоры станут успешнее.");
            return;
        }

        if (diplomacy.TryCreateNonAggressionPact(playerKingdom, target, days, out _))
            ShowText("Договор заключён", $"{playerKingdom.Name} и {target.Name} заключили пакт о ненападении на {days} дней.");
        else
            ShowText("Переговоры сорвались", "Пока послы вели переговоры, обстоятельства изменились. Договор заключить не удалось.");
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
            ShowText("Разрыв договора", "У вашего королевства нет действующих пактов о ненападении.");
            return;
        }

        MBInformationManager.ShowMultiSelectionInquiry(
            new MultiSelectionInquiryData(
                "Разорвать пакт о ненападении",
                "Досрочный разрыв договора серьёзно подорвёт доверие другой стороны и на некоторое время закроет путь к новому соглашению.",
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
                        $"Разорвать пакт с {target.Name}? Этот шаг ухудшит отношения и затруднит скорое заключение нового договора.",
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
        var relatives = Clan.PlayerClan?.Heroes
            .Where(h => h != null && h != Hero.MainHero && h.IsAlive && h.CanMarry())
            .OrderBy(h => h.Name.ToString())
            .Select(h => h.Name.ToString())
            .Take(8)
            .ToArray() ?? Array.Empty<string>();

        var relativeText = relatives.Length > 0
            ? "Сейчас брачный союз можно искать для членов вашего рода: " + string.Join(", ", relatives) + "."
            : "Среди членов вашего рода сейчас нет свободных кандидатов для династического брака.";

        ShowText(
            "Династические браки",
            "Брак может скрепить отношения между двумя знатными домами. Поговорите с главой другого клана, чтобы предложить союз для себя или подходящего члена семьи. Условия брака обсуждаются при заключении соглашения.\n\n" +
            "Другие дома также могут первыми направить к вам гонца с предложением о браке. Такое предложение появится отдельным уведомлением на карте, и решение останется за вами.\n\n" +
            relativeText);
    }

    private static void ShowCultureSupport()
    {
        ShowText(
            "Смена народности поселений",
            "В принадлежащем вашему клану городе или замке можно начать переселение и постепенно утвердить народность вашего рода. Реформа стоит 100 000 динаров и требует клан не ниже 3-го уровня. Во время осады проводить её нельзя.\n\n" +
            "После завершения изменятся связанные деревни, местная знать, набор рекрутов, ополчение, караваны, наёмники и другие жители, связанные с жизнью поселения. Именные лорды чужих кланов и уникальные персонажи останутся прежними.");
    }

    private static string DescribeReadiness(int score)
    {
        if (score >= 50) return "настроены очень благосклонно";
        if (score >= 20) return "настроены благосклонно";
        if (score >= 0) return "готовы выслушать предложение";
        if (score >= -20) return "относятся к договору прохладно";
        return "не желают такого соглашения";
    }

    private static string DescribeTrust(int trust)
    {
        if (trust >= 50) return "очень доверительные";
        if (trust >= 20) return "хорошие";
        if (trust > -20) return "настороженные";
        if (trust > -50) return "плохие";
        return "крайне враждебные";
    }

    private static string ToPlayerFacingDiplomacyReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return "Сейчас отправить такое посольство невозможно.";
        if (reason.IndexOf("war", StringComparison.OrdinalIgnoreCase) >= 0) return "Нельзя заключить пакт о ненападении, пока между державами идёт война.";
        if (reason.IndexOf("cooldown", StringComparison.OrdinalIgnoreCase) >= 0) return "После прежнего разрыва договора другая сторона пока не готова возвращаться к переговорам.";
        if (reason.IndexOf("alliance", StringComparison.OrdinalIgnoreCase) >= 0 || reason.IndexOf("allowed", StringComparison.OrdinalIgnoreCase) >= 0) return "Обстоятельства и законы этих держав сейчас не позволяют заключить такой договор.";
        return reason;
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
