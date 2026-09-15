using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace KaiTOR.Diplomacy.Runtime;

public sealed class KaiCultureAssimilationBehavior : CampaignBehaviorBase
{
    public const int AssimilationCost = 100000;
    public const int AssimilationDurationDays = 30;

    private Dictionary<string, string> _targetCultureBySettlement = new();
    private Dictionary<string, string> _sponsorClanBySettlement = new();
    private Dictionary<string, double> _completionDayBySettlement = new();
    private bool _runtimeEnabled;

    public override void RegisterEvents()
    {
        CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        CampaignEvents.DailyTickSettlementEvent.AddNonSerializedListener(this, OnDailyTickSettlement);
        CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnSettlementOwnerChanged);
    }

    public override void SyncData(IDataStore dataStore)
    {
        dataStore.SyncData("kaitor_diplomacy_assimilation_target_culture", ref _targetCultureBySettlement);
        dataStore.SyncData("kaitor_diplomacy_assimilation_sponsor_clan", ref _sponsorClanBySettlement);
        dataStore.SyncData("kaitor_diplomacy_assimilation_completion_day", ref _completionDayBySettlement);

        _targetCultureBySettlement ??= new Dictionary<string, string>();
        _sponsorClanBySettlement ??= new Dictionary<string, string>();
        _completionDayBySettlement ??= new Dictionary<string, double>();
    }

    private void OnSessionLaunched(CampaignGameStarter starter)
    {
        _runtimeEnabled = TorCompatibilityGate.TryValidate(out _);
        RegisterMenuOptions(starter);
        ReconcileProjects();
    }

    private void RegisterMenuOptions(CampaignGameStarter starter)
    {
        starter.AddGameMenuOption(
            "town",
            "kaitor_assimilate_town",
            "KaiTOR: Assimilate settlement to your culture (100,000 denars)",
            AssimilationMenuCondition,
            AssimilationMenuConsequence,
            false,
            7);

        starter.AddGameMenuOption(
            "castle",
            "kaitor_assimilate_castle",
            "KaiTOR: Assimilate settlement to your culture (100,000 denars)",
            AssimilationMenuCondition,
            AssimilationMenuConsequence,
            false,
            7);
    }

    private bool AssimilationMenuCondition(MenuCallbackArgs args)
    {
        var settlement = Settlement.CurrentSettlement;
        if (!_runtimeEnabled || settlement == null || !settlement.IsFortification)
            return false;

        if (settlement.OwnerClan != Clan.PlayerClan)
            return false;

        args.optionLeaveType = GameMenuOption.LeaveType.Manage;

        var targetCulture = Hero.MainHero?.Culture;
        if (targetCulture == null)
        {
            args.IsEnabled = false;
            args.Tooltip = new TextObject("KaiTOR could not determine your culture.");
            return true;
        }

        if (settlement.Culture == targetCulture)
        {
            args.IsEnabled = false;
            args.Tooltip = new TextObject("This settlement already has your culture.");
            return true;
        }

        if (HasActiveProject(settlement, out var remainingDays, out var activeCulture))
        {
            args.IsEnabled = false;
            args.Tooltip = new TextObject($"Assimilation to {activeCulture} is already active: {remainingDays} day(s) remaining.");
            return true;
        }

        if (Hero.MainHero.Gold < AssimilationCost)
        {
            args.IsEnabled = false;
            args.Tooltip = new TextObject($"You need {AssimilationCost:N0} denars to start assimilation.");
            return true;
        }

        args.Tooltip = new TextObject(
            $"Start a {AssimilationDurationDays}-day cultural assimilation project toward {targetCulture.Name}. " +
            "If the settlement is lost before completion, the project is cancelled and TOR ownership assimilation takes over.");
        return true;
    }

    private void AssimilationMenuConsequence(MenuCallbackArgs args)
    {
        var settlement = Settlement.CurrentSettlement;
        if (settlement == null) return;

        var targetCulture = Hero.MainHero?.Culture;
        if (targetCulture == null) return;

        var title = "KaiTOR cultural assimilation";
        var body = $"Convert {settlement.Name} toward {targetCulture.Name}?\n\n" +
                   $"Cost: {AssimilationCost:N0} denars\n" +
                   $"Duration: {AssimilationDurationDays} campaign days\n\n" +
                   "On completion the settlement, its bound villages and local notable pool will switch culture. " +
                   "If another clan captures the settlement first, this project is cancelled and normal TOR assimilation takes over.";

        InformationManager.ShowInquiry(
            new InquiryData(
                title,
                body,
                true,
                true,
                "Start",
                "Cancel",
                () =>
                {
                    if (TryStartAssimilation(settlement, targetCulture, out var reason))
                    {
                        InformationManager.DisplayMessage(new InformationMessage(reason));
                    }
                    else
                    {
                        InformationManager.DisplayMessage(new InformationMessage("KaiTOR assimilation refused: " + reason));
                    }
                },
                null,
                string.Empty,
                0f,
                null,
                null,
                null),
            false,
            false);
    }

    public bool TryStartAssimilation(Settlement settlement, CultureObject targetCulture, out string reason)
    {
        reason = string.Empty;

        if (!_runtimeEnabled)
        {
            reason = "TOR compatibility gate is not active.";
            return false;
        }

        if (settlement == null || !settlement.IsFortification)
        {
            reason = "A town or castle is required.";
            return false;
        }

        if (settlement.OwnerClan != Clan.PlayerClan)
        {
            reason = "You can only assimilate settlements owned by your clan.";
            return false;
        }

        if (targetCulture == null)
        {
            reason = "Target culture is unavailable.";
            return false;
        }

        if (settlement.Culture == targetCulture)
        {
            reason = "The settlement already has the target culture.";
            return false;
        }

        if (HasActiveProject(settlement, out _, out _))
        {
            reason = "An assimilation project is already active here.";
            return false;
        }

        if (Hero.MainHero == null || Hero.MainHero.Gold < AssimilationCost)
        {
            reason = $"You need {AssimilationCost:N0} denars.";
            return false;
        }

        GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, null, AssimilationCost, false);

        var id = settlement.StringId;
        _targetCultureBySettlement[id] = targetCulture.StringId;
        _sponsorClanBySettlement[id] = Clan.PlayerClan.StringId;
        _completionDayBySettlement[id] = CampaignTime.Now.ToDays + AssimilationDurationDays;

        reason = $"Assimilation started in {settlement.Name}: {targetCulture.Name}, {AssimilationDurationDays} days, {AssimilationCost:N0} denars paid.";
        return true;
    }

    public bool HasActiveProject(Settlement settlement, out int remainingDays, out string targetCultureName)
    {
        remainingDays = 0;
        targetCultureName = string.Empty;
        if (settlement == null) return false;

        var id = settlement.StringId;
        if (!_targetCultureBySettlement.TryGetValue(id, out var cultureId) ||
            !_completionDayBySettlement.TryGetValue(id, out var completionDay))
        {
            return false;
        }

        var culture = MBObjectManager.Instance.GetObject<CultureObject>(cultureId);
        targetCultureName = culture?.Name?.ToString() ?? cultureId;
        remainingDays = Math.Max(0, (int)Math.Ceiling(completionDay - CampaignTime.Now.ToDays));
        return true;
    }

    public IEnumerable<string> DescribeProjects()
    {
        foreach (var settlementId in _targetCultureBySettlement.Keys.OrderBy(x => x, StringComparer.Ordinal))
        {
            var settlement = Settlement.All.FirstOrDefault(x => string.Equals(x.StringId, settlementId, StringComparison.Ordinal));
            if (settlement == null) continue;
            if (!HasActiveProject(settlement, out var days, out var cultureName)) continue;
            yield return $"{settlement.StringId} = {settlement.Name}: -> {cultureName}, {days} day(s) remaining";
        }
    }

    private void OnDailyTickSettlement(Settlement settlement)
    {
        if (!_runtimeEnabled || settlement == null) return;
        var id = settlement.StringId;
        if (!_completionDayBySettlement.TryGetValue(id, out var completionDay)) return;

        if (!_sponsorClanBySettlement.TryGetValue(id, out var sponsorClanId) ||
            settlement.OwnerClan == null ||
            !string.Equals(settlement.OwnerClan.StringId, sponsorClanId, StringComparison.Ordinal))
        {
            CancelProject(id, settlement, "ownership changed");
            return;
        }

        if (CampaignTime.Now.ToDays < completionDay) return;

        if (!_targetCultureBySettlement.TryGetValue(id, out var targetCultureId))
        {
            RemoveProject(id);
            return;
        }

        var targetCulture = MBObjectManager.Instance.GetObject<CultureObject>(targetCultureId);
        if (targetCulture == null)
        {
            CancelProject(id, settlement, "target culture no longer exists");
            return;
        }

        CompleteAssimilation(settlement, targetCulture);
        RemoveProject(id);

        if (settlement.OwnerClan == Clan.PlayerClan)
        {
            InformationManager.DisplayMessage(new InformationMessage(
                $"KaiTOR: cultural assimilation completed in {settlement.Name}. New culture: {targetCulture.Name}."));
        }
    }

    private void OnSettlementOwnerChanged(
        Settlement settlement,
        bool openToClaim,
        Hero newOwner,
        Hero oldOwner,
        Hero capturerHero,
        ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
    {
        if (settlement == null) return;
        var id = settlement.StringId;
        if (!_completionDayBySettlement.ContainsKey(id)) return;

        if (!_sponsorClanBySettlement.TryGetValue(id, out var sponsorClanId) ||
            settlement.OwnerClan == null ||
            !string.Equals(settlement.OwnerClan.StringId, sponsorClanId, StringComparison.Ordinal))
        {
            CancelProject(id, settlement, "the settlement was captured");
        }
    }

    private static void CompleteAssimilation(Settlement settlement, CultureObject targetCulture)
    {
        ApplyCultureToSettlementAndNotables(settlement, targetCulture);

        if (settlement.BoundVillages == null) return;
        foreach (var village in settlement.BoundVillages)
        {
            if (village?.Settlement == null) continue;
            ApplyCultureToSettlementAndNotables(village.Settlement, targetCulture);
        }
    }

    private static void ApplyCultureToSettlementAndNotables(Settlement settlement, CultureObject targetCulture)
    {
        settlement.Culture = targetCulture;

        foreach (var notable in settlement.Notables.ToList())
        {
            if (notable == null || notable.Culture == targetCulture) continue;

            var occupation = notable.Occupation;
            KillCharacterAction.ApplyByRemove(notable);
            var replacement = HeroCreator.CreateNotable(occupation, settlement);
            if (replacement != null)
            {
                EnterSettlementAction.ApplyForCharacterOnly(replacement, settlement);
            }
        }
    }

    private void ReconcileProjects()
    {
        var ids = _completionDayBySettlement.Keys.ToArray();
        foreach (var id in ids)
        {
            var settlement = Settlement.All.FirstOrDefault(x => string.Equals(x.StringId, id, StringComparison.Ordinal));
            if (settlement == null)
            {
                RemoveProject(id);
                continue;
            }

            if (!_sponsorClanBySettlement.TryGetValue(id, out var sponsorClanId) ||
                settlement.OwnerClan == null ||
                !string.Equals(settlement.OwnerClan.StringId, sponsorClanId, StringComparison.Ordinal))
            {
                RemoveProject(id);
            }
        }
    }

    private void CancelProject(string id, Settlement settlement, string reason)
    {
        var wasPlayerOwned = settlement?.OwnerClan == Clan.PlayerClan;
        RemoveProject(id);
        if (wasPlayerOwned)
        {
            InformationManager.DisplayMessage(new InformationMessage(
                $"KaiTOR: assimilation in {settlement.Name} cancelled because {reason}."));
        }
    }

    private void RemoveProject(string id)
    {
        _targetCultureBySettlement.Remove(id);
        _sponsorClanBySettlement.Remove(id);
        _completionDayBySettlement.Remove(id);
    }
}
