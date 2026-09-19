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
    private int _selectedTab;
    private string _actionResultText = string.Empty;
    private bool _disposed;

    public KaiTORDiplomacyVM()
    {
        Refresh();
    }

    public event Action CloseRequested;
    public event Action FamilyRequested;
    public event Action CultureRequested;
    public event Action FallbackHubRequested;

    [DataSourceProperty] public string TitleText => KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_title", "KaiTOR Diplomacy");
    [DataSourceProperty] public string SubtitleText => KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_subtitle", "Houses, treaties and the fate of your realm.");
    [DataSourceProperty] public string OverviewTabText => KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_tab_overview", "Overview");
    [DataSourceProperty] public string DiplomacyTabText => KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_tab_diplomacy", "Diplomacy");
    [DataSourceProperty] public string FamilyTabText => KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_tab_family", "Family");
    [DataSourceProperty] public string PopulationTabText => KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_tab_population", "Realm & Population");
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
    [DataSourceProperty] public string OpenFamilyText => KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_open_family", "Open family affairs");
    [DataSourceProperty] public string OpenCultureText => KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_open_culture", "Change settlement nationality");
    [DataSourceProperty] public string DawiTestText => KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_dawi_test", "Spawn one Dawi woman for live test");
    [DataSourceProperty] public string FallbackHubText => KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_fallback", "Open classic KaiTOR menu");
    [DataSourceProperty] public string SelectedRealmHeaderText => KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_selected_realm", "Selected realm");
    [DataSourceProperty] public string FamilyHeaderText => KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_family_header", "House and dynasty");
    [DataSourceProperty] public string PopulationHeaderText => KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_population_header", "Realm systems");
    [DataSourceProperty] public string CultureHeaderText => KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_culture_header", "Settlement");
    [DataSourceProperty] public string ResultHeaderText => KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_result_header", "Latest action");

    [DataSourceProperty]
    public bool IsOverviewVisible => _selectedTab == 0;

    [DataSourceProperty]
    public bool IsDiplomacyVisible => _selectedTab == 1;

    [DataSourceProperty]
    public bool IsFamilyVisible => _selectedTab == 2;

    [DataSourceProperty]
    public bool IsPopulationVisible => _selectedTab == 3;

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

            var text = KaiTORDiplomacyUiText.Format(
                "kaitor_diplomacy_ui_selected_kingdom_format",
                "{REALM}\n{PACT}\nTrust: {TRUST}\nBreaches: {BREACHES}\nCooldown: {COOLDOWN} days",
                ("REALM", _selectedKingdom.Name),
                ("PACT", pactText),
                ("TRUST", diplomacy.GetTrust(source, _selectedKingdom)),
                ("BREACHES", diplomacy.GetBreachCount(source, _selectedKingdom)),
                ("COOLDOWN", diplomacy.GetNapCooldownRemainingDays(source, _selectedKingdom)));

            var war = Campaign.Current?.GetCampaignBehavior<KaiWarExhaustionBehavior>();
            if (war != null && source.IsAtWarWith(_selectedKingdom))
            {
                text += "\n" + KaiTORDiplomacyUiText.Format(
                    "kaitor_diplomacy_ui_war_exhaustion_line",
                    "War exhaustion: us {OURS}% / them {THEIRS}%",
                    ("OURS", war.GetExhaustion(source, _selectedKingdom).ToString("0")),
                    ("THEIRS", war.GetExhaustion(_selectedKingdom, source).ToString("0")));
            }

            var threat = Campaign.Current?.GetCampaignBehavior<KaiThreatBehavior>();
            if (threat != null)
            {
                text += "\n" + KaiTORDiplomacyUiText.Format(
                    "kaitor_diplomacy_ui_threat_line",
                    "Threat: {THREAT}/100",
                    ("THREAT", threat.GetThreat(_selectedKingdom).ToString("0")));
            }

            var dynasty = Campaign.Current?.GetCampaignBehavior<KaiDynasticMarriageBehavior>();
            var dynasticStrength = dynasty?.GetDynasticStrength(source, _selectedKingdom) ?? 0f;
            if (dynasticStrength > 0.1f)
            {
                text += "\n" + KaiTORDiplomacyUiText.Format(
                    "kaitor_diplomacy_ui_dynastic_line",
                    "Dynastic ties: {STRENGTH}/100",
                    ("STRENGTH", dynasticStrength.ToString("0")));
            }

            return text;
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
            var dawi = Campaign.Current?.GetCampaignBehavior<KaiDawiWomenBehavior>();
            var vampire = Campaign.Current?.GetCampaignBehavior<KaiVampirePopulationBehavior>();
            var greenskin = Campaign.Current?.GetCampaignBehavior<KaiGreenskinPopulationBehavior>();
            var realm = Campaign.Current?.GetCampaignBehavior<KaiRealmHouseGrowthBehavior>();

            return KaiTORDiplomacyUiText.Format(
                "kaitor_diplomacy_ui_population_format",
                "Dawi women module: {DAWI}\nDawi automatic population: {DAWI_AUTO}\nVampire population: {VAMPIRE}\nGreenskin population: {GREEN}\nAI realm-house growth: {HOUSES}",
                ("DAWI", YesNo(dawi != null)),
                ("DAWI_AUTO", YesNo(KaiDawiWomenBehavior.AutomaticPopulationEnabled)),
                ("VAMPIRE", YesNo(vampire != null)),
                ("GREEN", YesNo(greenskin != null)),
                ("HOUSES", YesNo(realm != null)));
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

    public void ActionSelectOverview() => SetTab(0);
    public void ActionSelectDiplomacy() => SetTab(1);
    public void ActionSelectFamily() => SetTab(2);
    public void ActionSelectPopulation() => SetTab(3);

    public void ActionRefresh()
    {
        if (_disposed) return;
        Refresh();
        ActionResultText = KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_refreshed", "KaiTOR status refreshed.");
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
                "KaiTOR diplomacy is currently unavailable.");
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

    public void ActionOpenFamily()
    {
        if (_disposed) return;
        FamilyRequested?.Invoke();
    }

    public void ActionOpenCulture()
    {
        if (_disposed) return;
        CultureRequested?.Invoke();
    }

    public void ActionOpenFallbackHub()
    {
        if (_disposed) return;
        FallbackHubRequested?.Invoke();
    }

    public void ActionDawiTest()
    {
        if (_disposed) return;

        var behavior = Campaign.Current?.GetCampaignBehavior<KaiDawiWomenBehavior>();
        ActionResultText = behavior == null
            ? KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_dawi_unavailable", "The Dawi women module is not loaded.")
            : behavior.SpawnOneForLiveTest();

        Refresh();
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
            reason = KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_diplomacy_unavailable", "KaiTOR diplomacy is currently unavailable.");
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
            reason = KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_diplomacy_unavailable", "KaiTOR diplomacy is currently unavailable.");
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

    private void SetTab(int tab)
    {
        if (_disposed || _selectedTab == tab) return;

        _selectedTab = tab;
        OnPropertyChanged(nameof(IsOverviewVisible));
        OnPropertyChanged(nameof(IsDiplomacyVisible));
        OnPropertyChanged(nameof(IsFamilyVisible));
        OnPropertyChanged(nameof(IsPopulationVisible));
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

    private static string YesNo(bool value)
        => value
            ? KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_yes", "Yes")
            : KaiTORDiplomacyUiText.Get("kaitor_diplomacy_ui_no", "No");
}
