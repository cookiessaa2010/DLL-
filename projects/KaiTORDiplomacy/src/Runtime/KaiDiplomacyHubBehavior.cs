using System;
using System.Collections.Generic;
using System.Linq;
using KaiTOR.Diplomacy.Decisions;
using KaiTOR.Diplomacy.UI;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Stable player-facing KaiTOR hub. It deliberately does not depend on private
/// KingdomDiplomacyVM fields, so core diplomacy/family/culture actions remain reachable
/// even when a native/TOR screen patch cannot open its follow-up UI.
/// </summary>
public sealed class KaiDiplomacyHubBehavior : CampaignBehaviorBase
{
    private const string HubMenuId = "kaitor_diplomacy_hub";
    private static readonly int[] NapDurations = { 30, 60, 90, 180 };

    private static string _returnMenuId = "town";
    private static Kingdom _selectedProposalTarget;
    private static Kingdom _selectedBreakTarget;
    private static int _selectedDurationDays = KaiDiplomacyBehavior.DefaultNapDays;

    public override void RegisterEvents()
    {
        CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
    }

    public override void SyncData(IDataStore dataStore)
    {
        // UI selection state is session-only.
    }

    private void OnSessionLaunched(CampaignGameStarter starter)
    {
        ResetSelections();

        starter.AddGameMenu(
            HubMenuId,
            "Управление державой KaiTOR: договоры, семейные дела, население владений и состояние систем.",
            _ => KaiRuntimeLog.Write("UI_HUB_OPEN", $"return={_returnMenuId}"),
            GameMenu.MenuOverlayType.None,
            GameMenu.MenuFlags.None,
            null);

        AddEntry(starter, "town", "kaitor_hub_town", 8);
        AddEntry(starter, "town_outside", "kaitor_hub_town_outside", 8);
        AddEntry(starter, "castle", "kaitor_hub_castle", 8);

        starter.AddGameMenuOption(
            HubMenuId,
            "kaitor_hub_treaty_overview",
            "Договоры и доверие",
            BasicCondition,
            _ => ShowTreatyOverview(),
            false,
            0);

        starter.AddGameMenuOption(
            HubMenuId,
            "kaitor_hub_proposal_target",
            "Выбрать державу для пакта",
            ProposalTargetCondition,
            _ => ShowProposalTargets(),
            false,
            1);

        starter.AddGameMenuOption(
            HubMenuId,
            "kaitor_hub_proposal_duration",
            "Выбрать срок пакта",
            DurationCondition,
            _ => ShowDurationChoices(),
            false,
            2);

        starter.AddGameMenuOption(
            HubMenuId,
            "kaitor_hub_proposal_submit",
            "Внести предложение о пакте",
            ProposalSubmitCondition,
            _ => SubmitNapProposal(),
            false,
            3);

        starter.AddGameMenuOption(
            HubMenuId,
            "kaitor_hub_break_target",
            "Выбрать действующий пакт",
            BreakTargetCondition,
            _ => ShowBreakTargets(),
            false,
            4);

        starter.AddGameMenuOption(
            HubMenuId,
            "kaitor_hub_break_submit",
            "Вынести разрыв пакта на совет",
            BreakSubmitCondition,
            _ => SubmitBreakProposal(),
            false,
            5);

        starter.AddGameMenuOption(
            HubMenuId,
            "kaitor_hub_family",
            "Семейные дела",
            BasicCondition,
            _ => OpenFamilySection(),
            false,
            10);

        starter.AddGameMenuOption(
            HubMenuId,
            "kaitor_hub_culture",
            "Народность владения",
            CultureCondition,
            _ => OpenCultureSection(),
            false,
            11);

        starter.AddGameMenuOption(
            HubMenuId,
            "kaitor_hub_council",
            "{=kaitor_diplomacy_ui_council}Council",
            BasicCondition,
            _ => ShowCouncil(),
            false,
            12);

        starter.AddGameMenuOption(
            HubMenuId,
            "kaitor_hub_systems",
            "Состояние систем KaiTOR",
            BasicCondition,
            _ => ShowSystemStatus(),
            false,
            13);

        starter.AddGameMenuOption(
            HubMenuId,
            "kaitor_hub_back",
            "Назад",
            BackCondition,
            _ => ReturnToPreviousMenu(),
            true,
            99);
    }

    private static void AddEntry(CampaignGameStarter starter, string menuId, string optionId, int index)
    {
        starter.AddGameMenuOption(
            menuId,
            optionId,
            "KaiTOR: Дипломатия и династия",
            EntryCondition,
            _ => OpenPrimaryUi(),
            false,
            index);
    }

    private static bool EntryCondition(MenuCallbackArgs args)
    {
        args.optionLeaveType = GameMenuOption.LeaveType.Manage;
        if (Hero.MainHero == null || Clan.PlayerClan == null)
            return false;

        args.IsEnabled = true;
        args.Tooltip = new TextObject("Дипломатия, семейные дела, династия и население KaiTOR.");
        return true;
    }

    private static bool BasicCondition(MenuCallbackArgs args)
    {
        args.optionLeaveType = GameMenuOption.LeaveType.Manage;
        args.IsEnabled = Hero.MainHero != null && Clan.PlayerClan?.Kingdom != null;
        return true;
    }

    private static bool ProposalTargetCondition(MenuCallbackArgs args)
    {
        args.optionLeaveType = GameMenuOption.LeaveType.Manage;
        args.Text = new TextObject(_selectedProposalTarget == null
            ? "Выбрать державу для пакта"
            : $"Держава: {_selectedProposalTarget.Name}");
        args.IsEnabled = GetDiplomacy() is { RuntimeEnabled: true };
        if (!args.IsEnabled)
            args.Tooltip = new TextObject("Дополнительная дипломатия KaiTOR сейчас недоступна.");
        return true;
    }

    private static bool DurationCondition(MenuCallbackArgs args)
    {
        args.optionLeaveType = GameMenuOption.LeaveType.Manage;
        args.Text = new TextObject($"Срок пакта: {_selectedDurationDays} дней");
        args.IsEnabled = _selectedProposalTarget != null;
        if (!args.IsEnabled)
            args.Tooltip = new TextObject("Сначала выберите державу.");
        return true;
    }

    private static bool ProposalSubmitCondition(MenuCallbackArgs args)
    {
        args.optionLeaveType = GameMenuOption.LeaveType.Manage;
        var diplomacy = GetDiplomacy();
        var source = Clan.PlayerClan?.Kingdom;

        if (diplomacy == null || !diplomacy.RuntimeEnabled || source == null || _selectedProposalTarget == null)
        {
            args.IsEnabled = false;
            args.Tooltip = new TextObject("Сначала выберите державу и убедитесь, что дипломатия KaiTOR доступна.");
            return true;
        }

        if (!diplomacy.CanCreateNonAggressionPact(source, _selectedProposalTarget, _selectedDurationDays, out var reason))
        {
            args.IsEnabled = false;
            args.Tooltip = new TextObject(reason);
            return true;
        }

        if (diplomacy.GetNapAcceptanceScore(source, _selectedProposalTarget) < 0)
        {
            args.IsEnabled = false;
            args.Tooltip = new TextObject($"{_selectedProposalTarget.Name} сейчас не готово принять такое предложение.");
            return true;
        }

        if (Clan.PlayerClan.Influence < KaiDiplomacyBehavior.NapProposalInfluenceCost)
        {
            args.IsEnabled = false;
            args.Tooltip = new TextObject($"Требуется {KaiDiplomacyBehavior.NapProposalInfluenceCost} влияния.");
            return true;
        }

        args.IsEnabled = true;
        args.Tooltip = new TextObject($"Вынести на совет пакт о ненападении на {_selectedDurationDays} дней.");
        return true;
    }

    private static bool BreakTargetCondition(MenuCallbackArgs args)
    {
        args.optionLeaveType = GameMenuOption.LeaveType.Manage;
        args.Text = new TextObject(_selectedBreakTarget == null
            ? "Выбрать действующий пакт"
            : $"Действующий пакт: {_selectedBreakTarget.Name}");

        var diplomacy = GetDiplomacy();
        var source = Clan.PlayerClan?.Kingdom;
        args.IsEnabled = diplomacy != null &&
                         diplomacy.RuntimeEnabled &&
                         source != null &&
                         Kingdom.All.Any(k => k != null && k != source && diplomacy.IsNonAggressionPactActive(source, k));
        if (!args.IsEnabled)
            args.Tooltip = new TextObject("У вашей державы нет действующих пактов о ненападении.");
        return true;
    }

    private static bool BreakSubmitCondition(MenuCallbackArgs args)
    {
        args.optionLeaveType = GameMenuOption.LeaveType.Manage;
        var diplomacy = GetDiplomacy();
        var source = Clan.PlayerClan?.Kingdom;

        args.IsEnabled = diplomacy != null &&
                         diplomacy.RuntimeEnabled &&
                         source != null &&
                         _selectedBreakTarget != null &&
                         diplomacy.IsNonAggressionPactActive(source, _selectedBreakTarget) &&
                         Clan.PlayerClan.Influence >= KaiDiplomacyBehavior.NapBreakInfluenceCost;

        if (!args.IsEnabled)
            args.Tooltip = new TextObject(_selectedBreakTarget == null
                ? "Сначала выберите действующий пакт."
                : $"Для предложения о разрыве требуется {KaiDiplomacyBehavior.NapBreakInfluenceCost} влияния.");
        return true;
    }

    private static bool CultureCondition(MenuCallbackArgs args)
    {
        args.optionLeaveType = GameMenuOption.LeaveType.Manage;
        var settlement = Settlement.CurrentSettlement;
        args.IsEnabled = settlement != null &&
                         settlement.IsFortification &&
                         settlement.OwnerClan == Clan.PlayerClan;
        if (!args.IsEnabled)
            args.Tooltip = new TextObject("Изменять народность можно только в собственном городе или замке.");
        return true;
    }

    private static bool BackCondition(MenuCallbackArgs args)
    {
        args.optionLeaveType = GameMenuOption.LeaveType.Leave;
        return true;
    }

    private static void OpenPrimaryUi()
    {
        var current = Campaign.Current?.CurrentMenuContext?.GameMenu?.StringId;
        if (!string.IsNullOrWhiteSpace(current) && !string.Equals(current, HubMenuId, StringComparison.Ordinal))
            _returnMenuId = current;

        if (KaiTORDiplomacyScreen.TryOpen())
        {
            KaiRuntimeLog.Write("UI_PRIMARY_OPEN", $"screen={KaiTORDiplomacyScreen.MovieName}; return={_returnMenuId}");
            return;
        }

        KaiRuntimeLog.Write("UI_PRIMARY_FALLBACK", $"return={_returnMenuId}");
        TrySwitch(HubMenuId, "hub_entry_fallback");
    }

    public static string OpenHubFromExternalUi()
    {
        if (Campaign.Current == null)
            return "Кампания не запущена.";

        var current = Campaign.Current.CurrentMenuContext?.GameMenu?.StringId;
        if (!string.IsNullOrWhiteSpace(current) && !string.Equals(current, HubMenuId, StringComparison.Ordinal))
            _returnMenuId = current;

        return TrySwitch(HubMenuId, "external_ui_fallback")
            ? "Классическое меню KaiTOR открыто."
            : "Не удалось открыть классическое меню KaiTOR.";
    }

    private static void ReturnToPreviousMenu()
    {
        ResetSelections();
        TrySwitch(string.IsNullOrWhiteSpace(_returnMenuId) ? "town" : _returnMenuId, "hub_back");
    }

    private static void ShowTreatyOverview()
    {
        var diplomacy = GetDiplomacy();
        var source = Clan.PlayerClan?.Kingdom;
        if (diplomacy == null || source == null)
        {
            ShowText("Договоры и доверие", "Дипломатическая система сейчас недоступна.");
            return;
        }

        var lines = new List<string>();
        foreach (var target in Kingdom.All
                     .Where(k => k != null && !k.IsEliminated && k != source)
                     .OrderBy(k => k.Name?.ToString() ?? string.Empty, StringComparer.Ordinal))
        {
            var active = diplomacy.IsNonAggressionPactActive(source, target);
            var remaining = diplomacy.GetRemainingDays(source, target);
            var trust = diplomacy.GetTrust(source, target);
            var breaches = diplomacy.GetBreachCount(source, target);
            var cooldown = diplomacy.GetNapCooldownRemainingDays(source, target);

            if (!active && trust == 0 && breaches == 0 && cooldown == 0)
                continue;

            lines.Add(active
                ? $"• {target.Name}: пакт ещё {remaining} дн.; доверие {trust}; нарушений {breaches}."
                : $"• {target.Name}: договора нет; доверие {trust}; нарушений {breaches}; новый пакт через {cooldown} дн.");
        }

        ShowText(
            "Договоры и доверие",
            lines.Count == 0
                ? "У вашей державы пока нет истории пактов о ненападении."
                : string.Join("\n", lines));
    }

    private static void ShowProposalTargets()
    {
        var diplomacy = GetDiplomacy();
        var source = Clan.PlayerClan?.Kingdom;
        if (diplomacy == null || !diplomacy.RuntimeEnabled || source == null)
        {
            ShowText("Пакт о ненападении", "Дополнительная дипломатия KaiTOR сейчас недоступна.");
            return;
        }

        var elements = Kingdom.All
            .Where(k => k != null && !k.IsEliminated && k != source)
            .OrderBy(k => k.Name?.ToString() ?? string.Empty, StringComparer.Ordinal)
            .Select(k =>
            {
                var enabled = diplomacy.CanCreateNonAggressionPact(source, k, _selectedDurationDays, out var reason) &&
                              diplomacy.GetNapAcceptanceScore(source, k) >= 0;
                var hint = enabled
                    ? $"Доверие: {diplomacy.GetTrust(source, k)}."
                    : (string.IsNullOrWhiteSpace(reason) ? "Эта держава сейчас не готова к пакту." : reason);
                return new InquiryElement(k, k.Name.ToString(), null, enabled, hint);
            })
            .ToList();

        if (elements.Count == 0)
        {
            ShowText("Пакт о ненападении", "Не найдено доступных держав.");
            return;
        }

        MBInformationManager.ShowMultiSelectionInquiry(
            new MultiSelectionInquiryData(
                "Выберите державу",
                "Выберите державу для предложения о пакте о ненападении.",
                elements,
                true,
                1,
                1,
                "Выбрать",
                "Отмена",
                selected =>
                {
                    if (selected.Count == 0 || selected[0].Identifier is not Kingdom kingdom)
                        return;
                    _selectedProposalTarget = kingdom;
                    KaiRuntimeLog.Write("UI_HUB_TARGET", $"type=proposal; kingdom={kingdom.StringId}");
                    ShowQuick($"Для переговоров выбрана держава: {kingdom.Name}.");
                },
                null),
            true,
            true);
    }

    private static void ShowDurationChoices()
    {
        var elements = NapDurations
            .Select(days => new InquiryElement(days, $"{days} дней", null, true, string.Empty))
            .ToList();

        MBInformationManager.ShowMultiSelectionInquiry(
            new MultiSelectionInquiryData(
                "Срок пакта",
                "Выберите срок пакта о ненападении.",
                elements,
                true,
                1,
                1,
                "Выбрать",
                "Отмена",
                selected =>
                {
                    if (selected.Count == 0 || selected[0].Identifier is not int days)
                        return;
                    _selectedDurationDays = days;
                    KaiRuntimeLog.Write("UI_HUB_DURATION", $"days={days}");
                    ShowQuick($"Срок пакта: {days} дней.");
                },
                null),
            true,
            true);
    }

    private static void SubmitNapProposal()
    {
        var diplomacy = GetDiplomacy();
        var source = Clan.PlayerClan?.Kingdom;
        var target = _selectedProposalTarget;
        if (diplomacy == null || source == null || target == null)
        {
            ShowQuick("Не удалось подготовить предложение о пакте.");
            return;
        }

        if (!diplomacy.CanCreateNonAggressionPact(source, target, _selectedDurationDays, out var reason))
        {
            ShowQuick(reason);
            return;
        }

        if (Clan.PlayerClan.Influence < KaiDiplomacyBehavior.NapProposalInfluenceCost)
        {
            ShowQuick($"Требуется {KaiDiplomacyBehavior.NapProposalInfluenceCost} влияния.");
            return;
        }

        if (source.UnresolvedDecisions
            .OfType<KaiNonAggressionPactDecision>()
            .Any(d => d.TargetKingdom == target && !d.ShouldBeCancelled()))
        {
            ShowQuick($"Совет уже рассматривает пакт с {target.Name}.");
            return;
        }

        source.AddDecision(
            new KaiNonAggressionPactDecision(Clan.PlayerClan, target, _selectedDurationDays),
            false);

        KaiRuntimeLog.Write(
            "UI_HUB_NAP_SUBMIT",
            $"source={source.StringId}; target={target.StringId}; days={_selectedDurationDays}");

        ShowText(
            "Предложение внесено",
            $"Предложение о пакте с {target.Name} на {_selectedDurationDays} дней внесено на совет державы. " +
            "Даже если нативный экран дипломатии не открыл голосование автоматически, решение уже находится в списке решений королевства.");
    }

    private static void ShowBreakTargets()
    {
        var diplomacy = GetDiplomacy();
        var source = Clan.PlayerClan?.Kingdom;
        if (diplomacy == null || source == null)
            return;

        var elements = Kingdom.All
            .Where(k => k != null && k != source && diplomacy.IsNonAggressionPactActive(source, k))
            .OrderBy(k => k.Name?.ToString() ?? string.Empty, StringComparer.Ordinal)
            .Select(k => new InquiryElement(
                k,
                k.Name.ToString(),
                null,
                true,
                $"Осталось {diplomacy.GetRemainingDays(source, k)} дн.; доверие {diplomacy.GetTrust(source, k)}."))
            .ToList();

        if (elements.Count == 0)
        {
            ShowText("Разрыв пакта", "У вашей державы нет действующих пактов о ненападении.");
            return;
        }

        MBInformationManager.ShowMultiSelectionInquiry(
            new MultiSelectionInquiryData(
                "Выберите действующий пакт",
                "После выбора разрыв можно будет вынести на совет отдельной кнопкой.",
                elements,
                true,
                1,
                1,
                "Выбрать",
                "Отмена",
                selected =>
                {
                    if (selected.Count == 0 || selected[0].Identifier is not Kingdom kingdom)
                        return;
                    _selectedBreakTarget = kingdom;
                    KaiRuntimeLog.Write("UI_HUB_TARGET", $"type=break; kingdom={kingdom.StringId}");
                    ShowQuick($"Для рассмотрения выбран пакт с {kingdom.Name}.");
                },
                null),
            true,
            true);
    }

    private static void SubmitBreakProposal()
    {
        var diplomacy = GetDiplomacy();
        var source = Clan.PlayerClan?.Kingdom;
        var target = _selectedBreakTarget;

        if (diplomacy == null || source == null || target == null ||
            !diplomacy.IsNonAggressionPactActive(source, target))
        {
            ShowQuick("Выбранный пакт больше не действует.");
            return;
        }

        if (Clan.PlayerClan.Influence < KaiDiplomacyBehavior.NapBreakInfluenceCost)
        {
            ShowQuick($"Требуется {KaiDiplomacyBehavior.NapBreakInfluenceCost} влияния.");
            return;
        }

        if (source.UnresolvedDecisions
            .OfType<KaiBreakNonAggressionPactDecision>()
            .Any(d => d.TargetKingdom == target && !d.ShouldBeCancelled()))
        {
            ShowQuick($"Совет уже рассматривает разрыв пакта с {target.Name}.");
            return;
        }

        source.AddDecision(new KaiBreakNonAggressionPactDecision(Clan.PlayerClan, target), false);
        KaiRuntimeLog.Write("UI_HUB_NAP_BREAK_SUBMIT", $"source={source.StringId}; target={target.StringId}");

        ShowText(
            "Разрыв внесён на совет",
            $"Предложение о досрочном разрыве пакта с {target.Name} внесено в список решений королевства.");
    }

    private static void OpenFamilySection()
    {
        var result = KaiFamilyAffairsBehavior.OpenFamilyMenuFromConsole();
        if (!result.StartsWith("Меню ", StringComparison.Ordinal))
            ShowQuick(result);
    }

    private static void OpenCultureSection()
    {
        var behavior = Campaign.Current?.GetCampaignBehavior<KaiCultureAssimilationBehavior>();
        if (behavior == null)
        {
            ShowQuick("Система народности поселений не загружена.");
            return;
        }

        behavior.OpenCultureChangeDialog();
    }

    private static void ShowCouncil()
    {
        var advisor = Campaign.Current?.GetCampaignBehavior<KaiAdvisorBehavior>();
        if (advisor == null)
        {
            ShowText(
                new TextObject("{=kaitor_diplomacy_ui_council}Council").ToString(),
                new TextObject("{=kaitor_diplomacy_ui_council_unavailable}The council is currently unavailable.").ToString());
            return;
        }

        var lines = advisor.GetAdvice().Take(10).ToArray();
        ShowText(
            new TextObject("{=kaitor_diplomacy_ui_council}Council").ToString(),
            lines.Length == 0
                ? new TextObject("{=kaitor_diplomacy_ui_council_clear}The council sees no immediate strategic warning.").ToString()
                : string.Join("\n\n", lines.Select(x => "• " + x)));
    }

    private static void ShowSystemStatus()
    {
        var diplomacy = GetDiplomacy();
        var dawi = Campaign.Current?.GetCampaignBehavior<KaiDawiWomenBehavior>();
        var vampire = Campaign.Current?.GetCampaignBehavior<KaiVampirePopulationBehavior>();
        var greenskin = Campaign.Current?.GetCampaignBehavior<KaiGreenskinPopulationBehavior>();
        var realm = Campaign.Current?.GetCampaignBehavior<KaiRealmHouseGrowthBehavior>();

        var lines = new List<string>
        {
            $"Дипломатия: {(diplomacy?.RuntimeEnabled == true ? "работает" : "недоступна")}.",
            $"Женщины-гномы: {(dawi != null ? "модуль загружен" : "модуль не загружен")}.",
            $"Население вампиров: {(vampire != null ? "включено" : "не загружено")}.",
            $"Население зеленокожих: {(greenskin != null ? "включено" : "не загружено")}.",
            $"Рост домов AI: {(realm != null ? "модуль загружен" : "модуль не загружен")}."
        };

        if (dawi != null)
            lines.AddRange(dawi.DescribeStatus().Take(3));

        ShowText("Состояние систем KaiTOR", string.Join("\n", lines));
    }

    private static KaiDiplomacyBehavior GetDiplomacy()
        => Campaign.Current?.GetCampaignBehavior<KaiDiplomacyBehavior>();

    private static bool TrySwitch(string menuId, string stage)
    {
        try
        {
            var before = Campaign.Current?.CurrentMenuContext?.GameMenu?.StringId ?? "none";
            KaiRuntimeLog.Write("UI_HUB_NAV_BEGIN", $"stage={stage}; from={before}; to={menuId}");
            GameMenu.SwitchToMenu(menuId);
            var after = Campaign.Current?.CurrentMenuContext?.GameMenu?.StringId ?? "none";
            KaiRuntimeLog.Write("UI_HUB_NAV_OK", $"stage={stage}; requested={menuId}; current={after}");
            return true;
        }
        catch (Exception ex)
        {
            KaiRuntimeLog.Exception("UI_HUB_NAV_FAILED", ex, $"stage={stage}; to={menuId}");
            ShowQuick("Не удалось открыть раздел KaiTOR. Ошибка записана в журнал.");
            return false;
        }
    }

    private static void ResetSelections()
    {
        _selectedProposalTarget = null;
        _selectedBreakTarget = null;
        _selectedDurationDays = KaiDiplomacyBehavior.DefaultNapDays;
    }

    private static void ShowText(string title, string body)
    {
        InformationManager.ShowInquiry(
            new InquiryData(title, body, true, false, "Хорошо", string.Empty, null, null),
            false,
            false);
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
