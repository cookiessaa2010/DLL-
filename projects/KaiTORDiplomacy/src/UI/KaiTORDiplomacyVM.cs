using System;
using System.Linq;
using KaiTOR.Diplomacy.Decisions;
using KaiTOR.Diplomacy.Runtime;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace KaiTOR.Diplomacy.UI;

public sealed class KaiTORDiplomacyVM : ViewModel, IDisposable
{
    private static readonly int[] NapDurations = { 30, 60, 90, 180 };

    private Kingdom _selectedKingdom;
    private int _selectedDurationDays = KaiDiplomacyBehavior.DefaultNapDays;
    private string _actionResultText = string.Empty;
    private bool _disposed;

    public KaiTORDiplomacyVM(Kingdom initialKingdom = null)
    {
        _selectedKingdom = initialKingdom;
        Refresh();
    }

    public event Action CloseRequested;
    public event Action CultureRequested;

    [DataSourceProperty] public string TitleText => KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_title", "Diplomacy and Dynasty");
    [DataSourceProperty] public string SubtitleText => KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_subtitle", "Houses, treaties and the fate of your realm.");
    [DataSourceProperty] public string RefreshText => KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_refresh", "Refresh");
    [DataSourceProperty] public string CloseText => KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_close", "Close");
    [DataSourceProperty] public string SelectKingdomHeaderText => KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_select_kingdom", "Select a realm");
    [DataSourceProperty] public string ChooseRealmText => KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_choose_realm", "Choose realm");
    [DataSourceProperty] public string ProposeNapText => KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_propose_nap", "Propose non-aggression pact");
    [DataSourceProperty] public string BreakNapText => KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_break_nap", "Propose ending pact");
    [DataSourceProperty] public string DurationButtonText => KaiTORDiplomacyUiText.Format(
        "kaitor_diplomacy_ui_duration",
        "Duration: {DAYS} days",
        ("DAYS", _selectedDurationDays));
    [DataSourceProperty] public string OpenCultureText => KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_open_culture", "Change settlement culture");
    [DataSourceProperty] public string SelectedRealmHeaderText => KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_selected_realm", "Selected realm");
    [DataSourceProperty] public string FamilyHeaderText => KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_family_header", "House and dynasty");
    [DataSourceProperty] public string PopulationHeaderText => KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_population_header", "Realm");
    [DataSourceProperty] public string CultureHeaderText => KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_culture_header", "Settlement");

    [DataSourceProperty]
    public string OverviewText
    {
        get
        {
            var hero = Hero.MainHero;
            var clan = Clan.PlayerClan;
            var kingdom = clan?.Kingdom;

            if (hero == null || clan == null)
                return KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_no_campaign", "Campaign data is not available.");

            var realm = kingdom?.Name?.ToString() ?? KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_no_realm", "No realm");
            var diplomacy = Campaign.Current?.GetCampaignBehavior<KaiDiplomacyBehavior>();
            var activePacts = diplomacy == null || kingdom == null
                ? 0
                : Kingdom.All.Count(k => k != null && k != kingdom && diplomacy.IsNonAggressionPactActive(kingdom, k));

            return KaiTORDiplomacyUiText.Format(
                "kaitor_diplomacy_ui_overview_format",
                "House: {CLAN}\nRealm: {REALM}\nInfluence: {INFLUENCE}\nTreasury: {GOLD}\nActive non-aggression pacts: {PACTS}",
                ("CLAN", clan.Name),
                ("REALM", realm),
                ("INFLUENCE", clan.Influence.ToString("0")),
                ("GOLD", hero.Gold.ToString("N0")),
                ("PACTS", activePacts));
        }
    }

    [DataSourceProperty]
    public string SelectedKingdomText
    {
        get
        {
            if (_selectedKingdom == null)
                return KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_no_kingdom_selected", "No realm selected.");

            var diplomacy = Campaign.Current?.GetCampaignBehavior<KaiDiplomacyBehavior>();
            var source = Clan.PlayerClan?.Kingdom;
            if (diplomacy == null || source == null)
                return _selectedKingdom.Name.ToString();

            var active = diplomacy.IsNonAggressionPactActive(source, _selectedKingdom);
            var pactText = active
                ? KaiTORDiplomacyUiText.Format(
                    "kaitor_diplomacy_ui_pact_active",
                    "Pact active: {DAYS} days remaining",
                    ("DAYS", diplomacy.GetRemainingDays(source, _selectedKingdom)))
                : KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_pact_none", "No active pact");

            return KaiTORDiplomacyUiText.Format(
                "kaitor_diplomacy_ui_selected_kingdom_format",
                "{REALM}\n{PACT}\nTrust: {TRUST}\nBreaches: {BREACHES}\nCooldown: {COOLDOWN} days",
                ("REALM", _selectedKingdom.Name),
                ("PACT", pactText),
                ("TRUST", diplomacy.GetTrust(source, _selectedKingdom)),
                ("BREACHES", diplomacy.GetBreachCount(source, _selectedKingdom)),
                ("COOLDOWN", diplomacy.GetNapCooldownRemainingDays(source, _selectedKingdom)));
        }
    }

    [DataSourceProperty]
    public string FamilySummaryText
    {
        get
        {
            var hero = Hero.MainHero;
            if (hero == null)
                return KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_no_campaign", "Campaign data is not available.");

            var spouse = hero.Spouse?.IsAlive == true
                ? hero.Spouse.Name.ToString()
                : KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_none", "None");
            var livingChildren = hero.Children.Count(x => x != null && x.IsAlive);
            var marriedChildren = hero.Children.Count(x => x != null && x.IsAlive && x.Spouse?.IsAlive == true);

            return KaiTORDiplomacyUiText.Format(
                "kaitor_diplomacy_ui_family_format",
                "Spouse: {SPOUSE}\nLiving children and adopted heirs: {CHILDREN}\nMarried heirs: {MARRIED}",
                ("SPOUSE", spouse),
                ("CHILDREN", livingChildren),
                ("MARRIED", marriedChildren));
        }
    }

    [DataSourceProperty]
    public string PopulationSummaryText
    {
        get
        {
            var kingdom = Clan.PlayerClan?.Kingdom;
            if (kingdom == null)
                return KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_no_realm", "Your clan is not part of a realm.");

            var heroes = Hero.AllAliveHeroes
                .Where(h => h != null && h.Clan?.Kingdom == kingdom)
                .ToArray();

            var marriedPairs = heroes.Count(h =>
                h.Spouse != null &&
                h.Spouse.IsAlive &&
                h.Spouse.Clan?.Kingdom == kingdom &&
                string.CompareOrdinal(h.StringId, h.Spouse.StringId) < 0);

            var pregnant = heroes.Count(h => h.IsPregnant);
            var clans = kingdom.Clans.Count(c => c != null && !c.IsEliminated);
            var lords = heroes.Count(h => h.IsLord);
            var settlements = kingdom.Settlements.Count;

            return KaiTORDiplomacyUiText.Format(
                "kaitor_diplomacy_ui_population_format",
                "Noble houses: {CLANS}\nSettlements: {SETTLEMENTS}\nLiving lords: {LORDS}\nMarried couples: {MARRIED}\nPregnancies: {PREGNANT}",
                ("CLANS", clans),
                ("SETTLEMENTS", settlements),
                ("LORDS", lords),
                ("MARRIED", marriedPairs),
                ("PREGNANT", pregnant));
        }
    }

    [DataSourceProperty]
    public string CultureSummaryText
    {
        get
        {
            var settlement = TaleWorlds.CampaignSystem.Settlements.Settlement.CurrentSettlement;
            if (settlement == null)
                return KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_no_settlement", "No settlement is currently open.");

            var owner = settlement.OwnerClan?.Name?.ToString() ?? KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_none", "None");
            var culture = settlement.Culture?.Name?.ToString() ?? KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_none", "None");

            return KaiTORDiplomacyUiText.Format(
                "kaitor_diplomacy_ui_culture_format",
                "Settlement: {SETTLEMENT}\nOwner: {OWNER}\nCurrent nationality: {CULTURE}",
                ("SETTLEMENT", settlement.Name),
                ("OWNER", owner),
                ("CULTURE", culture));
        }
    }

    [DataSourceProperty]
    public string ActionResultText
    {
        get => _actionResultText;
        private set
        {
            if (_actionResultText == value) return;
            _actionResultText = value ?? string.Empty;
            OnPropertyChanged(nameof(ActionResultText));
            OnPropertyChanged(nameof(IsActionResultVisible));
        }
    }

    [DataSourceProperty]
    public bool IsActionResultVisible => !string.IsNullOrWhiteSpace(ActionResultText);

    [DataSourceProperty]
    public bool IsProposeDisabled => !CanPropose(out _);

    [DataSourceProperty]
    public bool IsBreakDisabled => !CanBreak(out _);

    public void ActionRefresh()
    {
        if (_disposed) return;
        Refresh();
        ActionResultText = KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_refreshed", "Information refreshed.");
    }

    public void ActionChooseRealm()
    {
        if (_disposed) return;

        var source = Clan.PlayerClan?.Kingdom;
        var diplomacy = Campaign.Current?.GetCampaignBehavior<KaiDiplomacyBehavior>();
        if (source == null || diplomacy == null || !diplomacy.RuntimeEnabled)
        {
            ActionResultText = KaiTORDiplomacyUiText.Get(
                "kaitor_diplomacy_ui_diplomacy_unavailable",
                "Diplomatic actions are currently unavailable.");
            return;
        }

        var elements = Kingdom.All
            .Where(k => k != null && !k.IsEliminated && k != source)
            .OrderBy(k => k.Name?.ToString() ?? string.Empty, StringComparer.Ordinal)
            .Select(k =>
            {
                var active = diplomacy.IsNonAggressionPactActive(source, k);
                var status = active
                    ? KaiTORDiplomacyUiText.Format(
                        "kaitor_diplomacy_ui_list_pact_active",
                        "Pact: {DAYS} days",
                        ("DAYS", diplomacy.GetRemainingDays(source, k)))
                    : KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_list_no_pact", "No pact");

                var trust = KaiTORDiplomacyUiText.Format(
                    "kaitor_diplomacy_ui_trust",
                    "Trust: {TRUST}",
                    ("TRUST", diplomacy.GetTrust(source, k)));

                return new InquiryElement(
                    k,
                    k.Name.ToString(),
                    null,
                    true,
                    status + " | " + trust);
            })
            .ToList();

        if (elements.Count == 0)
        {
            ActionResultText = KaiTORDiplomacyUiText.Get(
                "kaitor_diplomacy_ui_no_realms",
                "No other active realms are available.");
            return;
        }

        MBInformationManager.ShowMultiSelectionInquiry(
            new MultiSelectionInquiryData(
                KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_select_kingdom", "Select a realm"),
                KaiTORDiplomacyUiText.Get(
                    "kaitor_diplomacy_ui_select_kingdom_hint",
                    "Choose the realm you want to inspect or negotiate with."),
                elements,
                true,
                1,
                1,
                KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_choose_realm", "Choose realm"),
                KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_cancel", "Cancel"),
                selected =>
                {
                    if (selected.Count == 0 || selected[0].Identifier is not Kingdom kingdom)
                        return;

                    _selectedKingdom = kingdom;
                    KaiRuntimeLog.Write("GAUNTLET_REALM_SELECTED", $"kingdom={kingdom.StringId}");
                    OnPropertyChanged(nameof(SelectedKingdomText));
                    NotifyActionAvailability();
                },
                null),
            true,
            true);
    }

    public void ActionCycleDuration()
    {
        if (_disposed) return;

        var index = Array.IndexOf(NapDurations, _selectedDurationDays);
        index = (index + 1) % NapDurations.Length;
        _selectedDurationDays = NapDurations[index];

        OnPropertyChanged(nameof(DurationButtonText));
        NotifyActionAvailability();
    }

    public void ActionProposeNap()
    {
        if (_disposed) return;

        if (!CanPropose(out var reason))
        {
            ActionResultText = reason;
            return;
        }

        var source = Clan.PlayerClan.Kingdom;
        var target = _selectedKingdom;

        if (source.UnresolvedDecisions
            .OfType<KaiNonAggressionPactDecision>()
            .Any(d => d.TargetKingdom == target && !d.ShouldBeCancelled()))
        {
            ActionResultText = KaiTORDiplomacyUiText.Format(
                "kaitor_diplomacy_ui_already_considering",
                "The council is already considering a pact with {REALM}.",
                ("REALM", target.Name));
            return;
        }

        source.AddDecision(new KaiNonAggressionPactDecision(Clan.PlayerClan, target, _selectedDurationDays), false);
        KaiRuntimeLog.Write(
            "GAUNTLET_NAP_SUBMIT",
            $"source={source.StringId}; target={target.StringId}; days={_selectedDurationDays}");

        ActionResultText = KaiTORDiplomacyUiText.Format(
            "kaitor_diplomacy_ui_nap_submitted",
            "A {DAYS}-day non-aggression pact with {REALM} was submitted to the council.",
            ("DAYS", _selectedDurationDays),
            ("REALM", target.Name));
        Refresh();
    }

    public void ActionBreakNap()
    {
        if (_disposed) return;

        if (!CanBreak(out var reason))
        {
            ActionResultText = reason;
            return;
        }

        var source = Clan.PlayerClan.Kingdom;
        var target = _selectedKingdom;

        if (source.UnresolvedDecisions
            .OfType<KaiBreakNonAggressionPactDecision>()
            .Any(d => d.TargetKingdom == target && !d.ShouldBeCancelled()))
        {
            ActionResultText = KaiTORDiplomacyUiText.Format(
                "kaitor_diplomacy_ui_break_already_considering",
                "The council is already considering ending the pact with {REALM}.",
                ("REALM", target.Name));
            return;
        }

        source.AddDecision(new KaiBreakNonAggressionPactDecision(Clan.PlayerClan, target), false);
        KaiRuntimeLog.Write(
            "GAUNTLET_NAP_BREAK_SUBMIT",
            $"source={source.StringId}; target={target.StringId}");

        ActionResultText = KaiTORDiplomacyUiText.Format(
            "kaitor_diplomacy_ui_break_submitted",
            "Ending the pact with {REALM} was submitted to the council.",
            ("REALM", target.Name));
        Refresh();
    }

    public void ActionOpenCulture()
    {
        if (_disposed) return;
        CultureRequested?.Invoke();
    }

    public void ActionClose()
    {
        if (_disposed) return;
        CloseRequested?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }

    private void Refresh()
    {
        if (_selectedKingdom != null &&
            (_selectedKingdom.IsEliminated || _selectedKingdom == Clan.PlayerClan?.Kingdom))
            _selectedKingdom = null;

        NotifySummaryProperties();
        NotifyActionAvailability();
    }

    private bool CanPropose(out string reason)
    {
        reason = string.Empty;

        var diplomacy = Campaign.Current?.GetCampaignBehavior<KaiDiplomacyBehavior>();
        var source = Clan.PlayerClan?.Kingdom;
        var target = _selectedKingdom;

        if (diplomacy == null || !diplomacy.RuntimeEnabled || source == null || target == null)
        {
            reason = KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_diplomacy_unavailable", "Diplomatic actions are currently unavailable.");
            return false;
        }

        if (!diplomacy.CanCreateNonAggressionPact(source, target, _selectedDurationDays, out reason))
            return false;

        if (diplomacy.GetNapAcceptanceScore(source, target) < 0)
        {
            reason = KaiTORDiplomacyUiText.Format(
                "kaitor_diplomacy_ui_realm_not_ready",
                "{REALM} is not ready to accept this proposal.",
                ("REALM", target.Name));
            return false;
        }

        if (Clan.PlayerClan.Influence < KaiDiplomacyBehavior.NapProposalInfluenceCost)
        {
            reason = KaiTORDiplomacyUiText.Format(
                "kaitor_diplomacy_ui_influence_required",
                "{INFLUENCE} influence is required.",
                ("INFLUENCE", KaiDiplomacyBehavior.NapProposalInfluenceCost));
            return false;
        }

        return true;
    }

    private bool CanBreak(out string reason)
    {
        reason = string.Empty;

        var diplomacy = Campaign.Current?.GetCampaignBehavior<KaiDiplomacyBehavior>();
        var source = Clan.PlayerClan?.Kingdom;
        var target = _selectedKingdom;

        if (diplomacy == null || !diplomacy.RuntimeEnabled || source == null || target == null)
        {
            reason = KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_diplomacy_unavailable", "Diplomatic actions are currently unavailable.");
            return false;
        }

        if (!diplomacy.IsNonAggressionPactActive(source, target))
        {
            reason = KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_no_active_pact", "There is no active non-aggression pact with the selected realm.");
            return false;
        }

        if (Clan.PlayerClan.Influence < KaiDiplomacyBehavior.NapBreakInfluenceCost)
        {
            reason = KaiTORDiplomacyUiText.Format(
                "kaitor_diplomacy_ui_influence_required",
                "{INFLUENCE} influence is required.",
                ("INFLUENCE", KaiDiplomacyBehavior.NapBreakInfluenceCost));
            return false;
        }

        return true;
    }

    private void NotifySummaryProperties()
    {
        OnPropertyChanged(nameof(OverviewText));
        OnPropertyChanged(nameof(SelectedKingdomText));
        OnPropertyChanged(nameof(FamilySummaryText));
        OnPropertyChanged(nameof(PopulationSummaryText));
        OnPropertyChanged(nameof(CultureSummaryText));
    }

    private void NotifyActionAvailability()
    {
        OnPropertyChanged(nameof(IsProposeDisabled));
        OnPropertyChanged(nameof(IsBreakDisabled));
        OnPropertyChanged(nameof(DurationButtonText));
    }

}
