using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using KaiTOR.Diplomacy.Decisions;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Diplomacy;
using TaleWorlds.Core;
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
        }
        catch
        {
            // The native diplomacy screen must remain usable even if the optional action cannot be added.
        }
    }

    private static void AddProposalAction(KingdomDiplomacyVM vm, KaiDiplomacyBehavior diplomacy, Kingdom source, Kingdom target)
    {
        var unresolved = source.UnresolvedDecisions
            .OfType<KaiNonAggressionPactDecision>()
            .FirstOrDefault(d => d.TargetKingdom == target && !d.ShouldBeCancelled());

        if (unresolved != null)
        {
            vm.Actions.Add(new KingdomDiplomacyProposalActionItemVM(
                new TextObject("Рассмотреть пакт о ненападении"),
                new TextObject($"Совет уже обсуждает предложение о пакте с {target.Name}. Откройте решение, чтобы принять участие в голосовании."),
                0,
                GetNativeProposalAvailability(vm, 0, out var hint),
                hint,
                () => ForceDecision(vm, unresolved)));
            return;
        }

        var allowed = diplomacy.CanCreateNonAggressionPact(source, target, KaiDiplomacyBehavior.DefaultNapDays, out var ruleReason);
        var nativeAllowed = GetNativeProposalAvailability(vm, KaiDiplomacyBehavior.NapProposalInfluenceCost, out var nativeReason);
        var acceptance = diplomacy.GetNapAcceptanceScore(source, target);
        if (allowed && acceptance < 0)
        {
            allowed = false;
            ruleReason = $"{target.Name} сейчас не готово принять такое предложение. Улучшите отношения или дождитесь более благоприятной обстановки.";
        }

        var readiness = DescribeReadiness(acceptance);
        var explanation = new TextObject(
            $"Предложить {target.Name} взаимный отказ от обычного объявления войны. Срок договора выбирается перед внесением решения. " +
            $"Отношение другой стороны к переговорам: {readiness}. Стоимость инициативы — {KaiDiplomacyBehavior.NapProposalInfluenceCost} влияния.");

        var enabled = nativeAllowed && allowed;
        var hint = enabled
            ? TextObject.GetEmpty()
            : (!nativeAllowed ? nativeReason : new TextObject(string.IsNullOrWhiteSpace(ruleReason) ? "Сейчас это предложение недоступно." : ruleReason));

        vm.Actions.Add(new KingdomDiplomacyProposalActionItemVM(
            new TextObject("Предложить пакт о ненападении"),
            explanation,
            KaiDiplomacyBehavior.NapProposalInfluenceCost,
            enabled,
            hint,
            () => ShowDurationSelection(vm, diplomacy, source, target)));
    }

    private static void AddActivePactAction(KingdomDiplomacyVM vm, KaiDiplomacyBehavior diplomacy, Kingdom source, Kingdom target)
    {
        var unresolved = source.UnresolvedDecisions
            .OfType<KaiBreakNonAggressionPactDecision>()
            .FirstOrDefault(d => d.TargetKingdom == target && !d.ShouldBeCancelled());

        if (unresolved != null)
        {
            vm.Actions.Add(new KingdomDiplomacyProposalActionItemVM(
                new TextObject("Рассмотреть разрыв пакта"),
                new TextObject($"Совет уже обсуждает досрочный разрыв пакта с {target.Name}."),
                0,
                GetNativeProposalAvailability(vm, 0, out var hint),
                hint,
                () => ForceDecision(vm, unresolved)));
            return;
        }

        var remaining = diplomacy.GetRemainingDays(source, target);
        var nativeAllowed = GetNativeProposalAvailability(vm, KaiDiplomacyBehavior.NapBreakInfluenceCost, out var nativeReason);
        var decision = new KaiBreakNonAggressionPactDecision(Clan.PlayerClan, target);
        var canBreak = decision.CanMakeDecision(out var breakReason, true);
        var enabled = nativeAllowed && canBreak;
        var hint = enabled ? TextObject.GetEmpty() : (!nativeAllowed ? nativeReason : breakReason);

        vm.Actions.Add(new KingdomDiplomacyProposalActionItemVM(
            new TextObject("Разорвать пакт о ненападении"),
            new TextObject(
                $"Пакт с {target.Name} действует ещё {remaining} дн. Досрочный разрыв ухудшит отношения правящих домов и на 10 дней закроет путь к новому пакту. " +
                $"Стоимость инициативы — {KaiDiplomacyBehavior.NapBreakInfluenceCost} влияния."),
            KaiDiplomacyBehavior.NapBreakInfluenceCost,
            enabled,
            hint,
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
                $"Выберите срок, на который {source.Name} предложит {target.Name} взаимный отказ от войны. После внесения инициативы её рассмотрит совет державы.",
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
                        ShowMessage("Недостаточно влияния", $"Для внесения предложения требуется {KaiDiplomacyBehavior.NapProposalInfluenceCost} влияния.");
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
                "Досрочный разрыв договора",
                $"Вынести на совет предложение о разрыве пакта с {target.Name}? Если разрыв будет утверждён, отношения правящих домов ухудшатся.",
                true,
                true,
                "Вынести на совет",
                "Отмена",
                () =>
                {
                    var source = Clan.PlayerClan?.Kingdom;
                    if (source == null) return;
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
        catch { }

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
            force?.Invoke(decision);
        }
        catch { }
    }

    private static string DescribeReadiness(int score)
    {
        if (score >= 50) return "очень благоприятное";
        if (score >= 20) return "благоприятное";
        if (score >= 0) return "осторожно положительное";
        if (score >= -20) return "прохладное";
        return "отрицательное";
    }

    private static void ShowMessage(string title, string text)
    {
        InformationManager.ShowInquiry(new InquiryData(title, text, true, false, "Хорошо", string.Empty, null, null), false, false);
    }
}
