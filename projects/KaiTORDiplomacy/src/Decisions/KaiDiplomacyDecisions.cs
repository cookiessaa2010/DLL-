using System;
using System.Collections.Generic;
using System.Linq;
using KaiTOR.Diplomacy.Runtime;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.Core.ImageIdentifiers;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;

namespace KaiTOR.Diplomacy.Decisions;

public sealed class KaiNonAggressionPactDecision : KingdomDecision
{
    [SaveableProperty(1)]
    public Kingdom TargetKingdom { get; private set; }

    [SaveableProperty(2)]
    public int DurationDays { get; private set; }

    public KaiNonAggressionPactDecision(Clan proposerClan, Kingdom targetKingdom, int durationDays)
        : base(proposerClan)
    {
        TargetKingdom = targetKingdom;
        DurationDays = durationDays;
    }

    public override bool IsAllowed() => GetBehavior()?.RuntimeEnabled == true;
    public override int GetProposalInfluenceCost() => KaiDiplomacyBehavior.NapProposalInfluenceCost;

    public override TextObject GetGeneralTitle() => T($"Пакт о ненападении с {TargetKingdom?.Name}");
    public override TextObject GetSupportTitle() => T($"Голосование по пакту о ненападении с {TargetKingdom?.Name}");
    public override TextObject GetChooseTitle() => T($"Решение о пакте с {TargetKingdom?.Name}");

    public override TextObject GetSupportDescription()
        => T($"Совет державы решает, следует ли заключить с {TargetKingdom?.Name} пакт о ненападении на {DurationDays} дней. Пока договор действует, обычное объявление войны между державами будет невозможно.");

    public override TextObject GetChooseDescription()
        => T($"Как правитель, вы должны решить, заключать ли с {TargetKingdom?.Name} пакт о ненападении на {DurationDays} дней.");

    public override IEnumerable<DecisionOutcome> DetermineInitialCandidates()
    {
        yield return new KaiNonAggressionPactDecisionOutcome(true, Kingdom, TargetKingdom, DurationDays);
        yield return new KaiNonAggressionPactDecisionOutcome(false, Kingdom, TargetKingdom, DurationDays);
    }

    public override Clan DetermineChooser() => Kingdom.RulingClan;

    public override void DetermineSponsors(MBReadOnlyList<DecisionOutcome> possibleOutcomes)
    {
        foreach (var outcome in possibleOutcomes)
        {
            if (outcome is KaiNonAggressionPactDecisionOutcome nap && nap.ShouldStart)
                outcome.SetSponsor(ProposerClan);
            else
                AssignDefaultSponsor(outcome);
        }
    }

    public override float DetermineSupport(Clan clan, DecisionOutcome possibleOutcome)
    {
        var nap = possibleOutcome as KaiNonAggressionPactDecisionOutcome;
        if (nap == null) return 0f;
        if (clan == Clan.PlayerClan && clan == ProposerClan)
            return nap.ShouldStart ? 100f : 0f;

        var behavior = GetBehavior();
        if (behavior == null) return 0f;
        var score = behavior.GetNapCouncilSupportScore(Kingdom, TargetKingdom, clan);
        return nap.ShouldStart ? score : 100f - score;
    }

    public override bool CanMakeDecision(out TextObject reason, bool includeReason = false)
    {
        var behavior = GetBehavior();
        var ruleReason = string.Empty;
        if (behavior == null || !behavior.CanCreateNonAggressionPact(Kingdom, TargetKingdom, DurationDays, out ruleReason))
        {
            reason = includeReason ? T(string.IsNullOrWhiteSpace(ruleReason) ? "Сейчас этот договор заключить нельзя." : ruleReason) : TextObject.GetEmpty();
            return false;
        }

        if (TargetKingdom != Clan.PlayerClan?.Kingdom && behavior.GetNapAcceptanceScore(Kingdom, TargetKingdom) < 0)
        {
            reason = includeReason ? T($"{TargetKingdom.Name} сейчас не готово принять такое предложение.") : TextObject.GetEmpty();
            return false;
        }

        reason = TextObject.GetEmpty();
        return true;
    }

    protected override bool ShouldBeCancelledInternal()
    {
        return !CanMakeDecision(out _, false);
    }

    public override void ApplyChosenOutcome(DecisionOutcome chosenOutcome)
    {
        if (chosenOutcome is KaiNonAggressionPactDecisionOutcome nap && nap.ShouldStart)
            GetBehavior()?.TryCreateNonAggressionPact(Kingdom, TargetKingdom, DurationDays, out _);
    }

    public override void ApplySecondaryEffects(MBReadOnlyList<DecisionOutcome> possibleOutcomes, DecisionOutcome chosenOutcome) { }
    public override TextObject GetSecondaryEffects() => TextObject.GetEmpty();

    public override TextObject GetChosenOutcomeText(DecisionOutcome chosenOutcome, SupportStatus supportStatus, bool isShortVersion = false)
    {
        var accepted = chosenOutcome is KaiNonAggressionPactDecisionOutcome nap && nap.ShouldStart;
        if (accepted)
            return T($"{Kingdom.Leader?.Name} утвердил пакт о ненападении с {TargetKingdom?.Name} на {DurationDays} дней.");
        return T($"{Kingdom.Leader?.Name} отклонил предложение о пакте с {TargetKingdom?.Name}.");
    }

    public override DecisionOutcome GetQueriedDecisionOutcome(MBReadOnlyList<DecisionOutcome> possibleOutcomes)
        => possibleOutcomes.FirstOrDefault(x => x is KaiNonAggressionPactDecisionOutcome nap && nap.ShouldStart);

    private static KaiDiplomacyBehavior GetBehavior() => Campaign.Current?.GetCampaignBehavior<KaiDiplomacyBehavior>();
    private static TextObject T(string value) => new(value ?? string.Empty);
}

public sealed class KaiNonAggressionPactDecisionOutcome : DecisionOutcome
{
    [SaveableProperty(1)] public bool ShouldStart { get; private set; }
    [SaveableProperty(2)] public Kingdom SourceKingdom { get; private set; }
    [SaveableProperty(3)] public Kingdom TargetKingdom { get; private set; }
    [SaveableProperty(4)] public int DurationDays { get; private set; }

    public KaiNonAggressionPactDecisionOutcome(bool shouldStart, Kingdom source, Kingdom target, int durationDays)
    {
        ShouldStart = shouldStart;
        SourceKingdom = source;
        TargetKingdom = target;
        DurationDays = durationDays;
    }

    public override TextObject GetDecisionTitle() => new(ShouldStart ? "Поддержать" : "Отклонить");
    public override TextObject GetDecisionDescription() => new(ShouldStart
        ? $"Поддержать пакт о ненападении с {TargetKingdom?.Name} на {DurationDays} дней."
        : $"Отказаться от пакта о ненападении с {TargetKingdom?.Name}.");
    public override string GetDecisionLink() => null;
    public override ImageIdentifier GetDecisionImageIdentifier() => null;
}

public sealed class KaiBreakNonAggressionPactDecision : KingdomDecision
{
    [SaveableProperty(1)]
    public Kingdom TargetKingdom { get; private set; }

    public KaiBreakNonAggressionPactDecision(Clan proposerClan, Kingdom targetKingdom)
        : base(proposerClan)
    {
        TargetKingdom = targetKingdom;
    }

    public override bool IsAllowed() => GetBehavior()?.RuntimeEnabled == true;
    public override int GetProposalInfluenceCost() => KaiDiplomacyBehavior.NapBreakInfluenceCost;
    public override TextObject GetGeneralTitle() => T($"Разрыв пакта с {TargetKingdom?.Name}");
    public override TextObject GetSupportTitle() => T($"Голосование о разрыве пакта с {TargetKingdom?.Name}");
    public override TextObject GetChooseTitle() => T($"Решение о разрыве пакта с {TargetKingdom?.Name}");

    public override TextObject GetSupportDescription()
        => T($"Совет решает, следует ли досрочно разорвать пакт о ненападении с {TargetKingdom?.Name}. Разрыв ухудшит отношения между правящими домами и на время закроет путь к новому договору.");

    public override TextObject GetChooseDescription()
        => T($"Как правитель, вы должны решить, разрывать ли действующий пакт с {TargetKingdom?.Name}.");

    public override IEnumerable<DecisionOutcome> DetermineInitialCandidates()
    {
        yield return new KaiBreakNonAggressionPactDecisionOutcome(true, Kingdom, TargetKingdom);
        yield return new KaiBreakNonAggressionPactDecisionOutcome(false, Kingdom, TargetKingdom);
    }

    public override Clan DetermineChooser() => Kingdom.RulingClan;

    public override void DetermineSponsors(MBReadOnlyList<DecisionOutcome> possibleOutcomes)
    {
        foreach (var outcome in possibleOutcomes)
        {
            if (outcome is KaiBreakNonAggressionPactDecisionOutcome br && br.ShouldBreak)
                outcome.SetSponsor(ProposerClan);
            else
                AssignDefaultSponsor(outcome);
        }
    }

    public override float DetermineSupport(Clan clan, DecisionOutcome possibleOutcome)
    {
        var br = possibleOutcome as KaiBreakNonAggressionPactDecisionOutcome;
        if (br == null) return 0f;
        if (clan == Clan.PlayerClan && clan == ProposerClan)
            return br.ShouldBreak ? 100f : 0f;

        var behavior = GetBehavior();
        var score = behavior?.GetNapBreakCouncilSupportScore(Kingdom, TargetKingdom, clan) ?? 0f;
        return br.ShouldBreak ? score : 100f - score;
    }

    public override bool CanMakeDecision(out TextObject reason, bool includeReason = false)
    {
        var behavior = GetBehavior();
        if (behavior == null || !behavior.IsNonAggressionPactActive(Kingdom, TargetKingdom))
        {
            reason = includeReason ? T("Между державами нет действующего пакта о ненападении.") : TextObject.GetEmpty();
            return false;
        }
        reason = TextObject.GetEmpty();
        return true;
    }

    protected override bool ShouldBeCancelledInternal() => !CanMakeDecision(out _, false);

    public override void ApplyChosenOutcome(DecisionOutcome chosenOutcome)
    {
        if (chosenOutcome is KaiBreakNonAggressionPactDecisionOutcome br && br.ShouldBreak)
            GetBehavior()?.BreakNonAggressionPact(Kingdom, TargetKingdom);
    }

    public override void ApplySecondaryEffects(MBReadOnlyList<DecisionOutcome> possibleOutcomes, DecisionOutcome chosenOutcome) { }
    public override TextObject GetSecondaryEffects() => TextObject.GetEmpty();

    public override TextObject GetChosenOutcomeText(DecisionOutcome chosenOutcome, SupportStatus supportStatus, bool isShortVersion = false)
    {
        var broken = chosenOutcome is KaiBreakNonAggressionPactDecisionOutcome br && br.ShouldBreak;
        return broken
            ? T($"{Kingdom.Leader?.Name} утвердил досрочный разрыв пакта с {TargetKingdom?.Name}.")
            : T($"{Kingdom.Leader?.Name} сохранил пакт о ненападении с {TargetKingdom?.Name}.");
    }

    public override DecisionOutcome GetQueriedDecisionOutcome(MBReadOnlyList<DecisionOutcome> possibleOutcomes)
        => possibleOutcomes.FirstOrDefault(x => x is KaiBreakNonAggressionPactDecisionOutcome br && br.ShouldBreak);

    private static KaiDiplomacyBehavior GetBehavior() => Campaign.Current?.GetCampaignBehavior<KaiDiplomacyBehavior>();
    private static TextObject T(string value) => new(value ?? string.Empty);
}

public sealed class KaiBreakNonAggressionPactDecisionOutcome : DecisionOutcome
{
    [SaveableProperty(1)] public bool ShouldBreak { get; private set; }
    [SaveableProperty(2)] public Kingdom SourceKingdom { get; private set; }
    [SaveableProperty(3)] public Kingdom TargetKingdom { get; private set; }

    public KaiBreakNonAggressionPactDecisionOutcome(bool shouldBreak, Kingdom source, Kingdom target)
    {
        ShouldBreak = shouldBreak;
        SourceKingdom = source;
        TargetKingdom = target;
    }

    public override TextObject GetDecisionTitle() => new(ShouldBreak ? "Разорвать" : "Сохранить");
    public override TextObject GetDecisionDescription() => new(ShouldBreak
        ? $"Досрочно разорвать пакт о ненападении с {TargetKingdom?.Name}."
        : $"Сохранить действующий пакт о ненападении с {TargetKingdom?.Name}.");
    public override string GetDecisionLink() => null;
    public override ImageIdentifier GetDecisionImageIdentifier() => null;
}

public sealed class KaiDiplomacySaveableTypeDefiner : SaveableTypeDefiner
{
    public KaiDiplomacySaveableTypeDefiner() : base(986430) { }

    protected override void DefineClassTypes()
    {
        AddClassDefinition(typeof(KaiNonAggressionPactDecision), 1);
        AddClassDefinition(typeof(KaiNonAggressionPactDecisionOutcome), 2);
        AddClassDefinition(typeof(KaiBreakNonAggressionPactDecision), 3);
        AddClassDefinition(typeof(KaiBreakNonAggressionPactDecisionOutcome), 4);
    }
}
