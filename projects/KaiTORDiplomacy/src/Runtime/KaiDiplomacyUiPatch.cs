using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using KaiTOR.Diplomacy.Decisions;
using KaiTOR.Diplomacy.UI;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Diplomacy;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace KaiTOR.Diplomacy.Runtime;

[HarmonyPatch(typeof(KingdomDiplomacyVM), "OnSetPeaceItem")]
internal static class KaiDiplomacyUiPatch
{
    private static readonly MethodInfo ProposalEnabledMethod = AccessTools.Method(
        typeof(KingdomDiplomacyVM),
        "GetAreProposalActionsEnabledWithReason");

    private static readonly FieldInfo ForceDecisionField = AccessTools.Field(
        typeof(KingdomDiplomacyVM),
        "_forceDecision");

    [HarmonyPostfix]
    private static void AddNonAggressionActions(KingdomTruceItemVM item, KingdomDiplomacyVM __instance)
    {
        try
        {
            var diplomacy = Campaign.Current?.GetCampaignBehavior<KaiDiplomacyBehavior>();
            var source = Clan.PlayerClan?.Kingdom;
            var target = item?.Faction2 as Kingdom;
            if (diplomacy == null || !diplomacy.RuntimeEnabled || source == null || target == null) return;
            if (Clan.PlayerClan.IsUnderMercenaryService) return;

            if (diplomacy.IsNonAggressionPactActive(source, target))
                AddActivePactAction(__instance, diplomacy, source, target);
            else
                AddProposalAction(__instance, diplomacy, source, target);

            AddDashboardAction(__instance, target);
        }
        catch (Exception ex)
        {
            // Native diplomacy remains usable even when the optional action cannot be inserted.
            KaiRuntimeLog.Exception("DIPLOMACY_UI_FAILED", ex, "stage=OnSetPeaceItem_postfix");
        }
    }

    private static void AddDashboardAction(KingdomDiplomacyVM vm, Kingdom target)
    {
        if (vm?.Actions == null)
            return;

        const string actionName = "Открыть панель KaiTOR";
        if (vm.Actions.Any(x => string.Equals(x?.Name, actionName, StringComparison.Ordinal)))
            return;

        vm.Actions.Add(new KingdomDiplomacyProposalActionItemVM(
            new TextObject(actionName),
            new TextObject("Открыть сводную панель договоров, династии и состояния владений KaiTOR."),
            0,
            true,
            TextObject.GetEmpty(),
            () =>
            {
                if (KaiTORDiplomacyScreen.TryOpen(target))
                {
                    KaiRuntimeLog.Write("DIPLOMACY_DASHBOARD_OPEN", $"target={target?.StringId ?? "none"}");
                    return;
                }

                ShowMessage(
                    "Панель KaiTOR",
                    "Панель уже открыта или не удалось открыть её сейчас. Событие записано в KaiTOR.log.");
                KaiRuntimeLog.Write("DIPLOMACY_DASHBOARD_FAILED", $"target={target?.StringId ?? "none"}");
            }));
    }

    private static void AddProposalAction(KingdomDiplomacyVM vm, KaiDiplomacyBehavior diplomacy, Kingdom source, Kingdom target)
    {
        var unresolved = source.UnresolvedDecisions
            .OfType<KaiNonAggressionPactDecision>()
            .FirstOrDefault(d => d.TargetKingdom == target && !d.ShouldBeCancelled());

        if (unresolved != null)
        {
            var canReview = GetNativeProposalAvailability(vm, 0, out var reviewHint);
            vm.Actions.Add(new KingdomDiplomacyProposalActionItemVM(
                new TextObject("Рассмотреть пакт о ненападении"),
                new TextObject($"Совет уже рассматривает пакт с {target.Name}."),
                0,
                canReview,
                reviewHint,
                () => ForceDecision(vm, unresolved)));
            return;
        }

        var allowed = diplomacy.CanCreateNonAggressionPact(source, target, KaiDiplomacyBehavior.DefaultNapDays, out var ruleReason);
        var nativeAllowed = GetNativeProposalAvailability(vm, KaiDiplomacyBehavior.NapProposalInfluenceCost, out var nativeReason);
        var acceptance = diplomacy.GetNapAcceptanceScore(source, target);
        if (allowed && acceptance < 0)
        {
            allowed = false;
            ruleReason = $"{target.Name} сейчас не готово принять такое предложение.";
        }

        var explanation = new TextObject($"Предложить {target.Name} пакт о ненападении. Срок выбирается перед голосованием.");

        var enabled = nativeAllowed && allowed;
        var actionHint = enabled
            ? TextObject.GetEmpty()
            : (!nativeAllowed ? nativeReason : new TextObject(string.IsNullOrWhiteSpace(ruleReason) ? "Сейчас это предложение недоступно." : ruleReason));

        vm.Actions.Add(new KingdomDiplomacyProposalActionItemVM(
            new TextObject("Предложить пакт о ненападении"),
            explanation,
            KaiDiplomacyBehavior.NapProposalInfluenceCost,
            enabled,
            actionHint,
            () => ShowDurationSelection(vm, diplomacy, source, target)));
    }

    private static void AddActivePactAction(KingdomDiplomacyVM vm, KaiDiplomacyBehavior diplomacy, Kingdom source, Kingdom target)
    {
        var unresolved = source.UnresolvedDecisions
            .OfType<KaiBreakNonAggressionPactDecision>()
            .FirstOrDefault(d => d.TargetKingdom == target && !d.ShouldBeCancelled());

        if (unresolved != null)
        {
            var canReview = GetNativeProposalAvailability(vm, 0, out var reviewHint);
            vm.Actions.Add(new KingdomDiplomacyProposalActionItemVM(
                new TextObject("Рассмотреть разрыв пакта"),
                new TextObject($"Совет обсуждает разрыв пакта с {target.Name}."),
                0,
                canReview,
                reviewHint,
                () => ForceDecision(vm, unresolved)));
            return;
        }

        var remaining = diplomacy.GetRemainingDays(source, target);
        var nativeAllowed = GetNativeProposalAvailability(vm, KaiDiplomacyBehavior.NapBreakInfluenceCost, out var nativeReason);
        var decision = new KaiBreakNonAggressionPactDecision(Clan.PlayerClan, target);
        var canBreak = decision.CanMakeDecision(out var breakReason, true);
        var enabled = nativeAllowed && canBreak;
        var actionHint = enabled ? TextObject.GetEmpty() : (!nativeAllowed ? nativeReason : breakReason);

        vm.Actions.Add(new KingdomDiplomacyProposalActionItemVM(
            new TextObject("Разорвать пакт о ненападении"),
            new TextObject($"Пакт с {target.Name}: ещё {remaining} дн. Досрочный разрыв ухудшит отношения."),
            KaiDiplomacyBehavior.NapBreakInfluenceCost,
            enabled,
            actionHint,
            () => ConfirmBreakDecision(vm, target)));
    }

    private static void ShowDurationSelection(KingdomDiplomacyVM vm, KaiDiplomacyBehavior diplomacy, Kingdom source, Kingdom target)
    {
        var durations = new[] { 30, 60, 90, 180 }
            .Select(days => new InquiryElement(days, $"{days} дней", null, true, string.Empty))
            .ToList();

        MBInformationManager.ShowMultiSelectionInquiry(
            new MultiSelectionInquiryData(
                "Срок пакта о ненападении",
                $"Выберите срок договора с {target.Name}.",
                durations,
                true,
                1,
                1,
                "Внести предложение",
                "Отмена",
                selected =>
                {
                    if (selected.Count == 0 || selected[0].Identifier is not int days) return;
                    if (!diplomacy.CanCreateNonAggressionPact(source, target, days, out var reason))
                    {
                        ShowMessage("Предложение недоступно", reason);
                        return;
                    }
                    if (Clan.PlayerClan.Influence < KaiDiplomacyBehavior.NapProposalInfluenceCost)
                    {
                        ShowMessage("Недостаточно влияния", $"Требуется {KaiDiplomacyBehavior.NapProposalInfluenceCost} влияния.");
                        return;
                    }

                    var decision = new KaiNonAggressionPactDecision(Clan.PlayerClan, target, days);
                    source.AddDecision(decision, false);
                    ForceDecision(vm, decision);
                },
                null),
            true,
            true);
    }

    private static void ConfirmBreakDecision(KingdomDiplomacyVM vm, Kingdom target)
    {
        InformationManager.ShowInquiry(
            new InquiryData(
                "Разорвать пакт",
                $"Вынести на совет разрыв пакта с {target.Name}?",
                true,
                true,
                "Вынести на совет",
                "Отмена",
                () =>
                {
                    var source = Clan.PlayerClan?.Kingdom;
                    if (source == null)
                    {
                        ShowMessage("Разрыв пакта недоступен", "Ваш клан больше не входит в состав державы.");
                        return;
                    }
                    if (Clan.PlayerClan.Influence < KaiDiplomacyBehavior.NapBreakInfluenceCost)
                    {
                        ShowMessage("Недостаточно влияния", $"Требуется {KaiDiplomacyBehavior.NapBreakInfluenceCost} влияния.");
                        return;
                    }
                    var decision = new KaiBreakNonAggressionPactDecision(Clan.PlayerClan, target);
                    source.AddDecision(decision, false);
                    ForceDecision(vm, decision);
                },
                null),
            false,
            false);
    }

    private static bool GetNativeProposalAvailability(KingdomDiplomacyVM vm, int influenceCost, out TextObject reason)
    {
        reason = TextObject.GetEmpty();
        try
        {
            if (ProposalEnabledMethod != null)
            {
                object[] args = { (float)influenceCost, TextObject.GetEmpty() };
                var result = ProposalEnabledMethod.Invoke(vm, args);
                reason = args[1] as TextObject ?? TextObject.GetEmpty();
                if (result is bool enabled) return enabled;
            }
        }
        catch (Exception ex)
        {
            KaiRuntimeLog.Exception("DIPLOMACY_UI_FAILED", ex, "stage=native_proposal_availability");
        }

        if (Clan.PlayerClan?.Kingdom == null)
        {
            reason = new TextObject("Ваш клан не входит в состав державы.");
            return false;
        }
        if (Clan.PlayerClan.IsUnderMercenaryService)
        {
            reason = new TextObject("Наёмный клан не может вносить межгосударственные договоры.");
            return false;
        }
        if (Clan.PlayerClan.Influence < influenceCost)
        {
            reason = new TextObject($"Для этого решения требуется {influenceCost} влияния.");
            return false;
        }
        return true;
    }

    private static void ForceDecision(KingdomDiplomacyVM vm, KingdomDecision decision)
    {
        try
        {
            var force = ForceDecisionField?.GetValue(vm) as Action<KingdomDecision>;
            if (force == null)
            {
                KaiRuntimeLog.Write(
                    "DIPLOMACY_UI_FALLBACK",
                    $"stage=force_decision; decision={decision?.GetType().Name ?? "null"}; reason=force_delegate_missing");
                ShowMessage(
                    "Решение внесено",
                    "Предложение уже добавлено в список решений королевства, но нативное окно совета не удалось открыть автоматически. Откройте экран королевства и раздел решений.");
                return;
            }

            force.Invoke(decision);
        }
        catch (Exception ex)
        {
            KaiRuntimeLog.Exception("DIPLOMACY_UI_FAILED", ex, "stage=force_decision");
            ShowMessage(
                "Решение не открылось",
                "Предложение сохранено в журнале ошибок. Откройте раздел решений королевства и проверьте, было ли оно добавлено.");
        }
    }

    private static void ShowMessage(string title, string text)
    {
        InformationManager.ShowInquiry(new InquiryData(title, text, true, false, "Хорошо", string.Empty, null, null), false, false);
    }
}
