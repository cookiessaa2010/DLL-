using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Lightweight in-game front end for KaiTOR diplomacy. It deliberately uses native
/// inquiry UI instead of patching TOR/Bannerlord kingdom screens, keeping the module
/// isolated from Gauntlet layouts and TOR UI updates.
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
        starter.AddGameMenuOption(
            "town",
            "kaitor_diplomacy_office_town",
            "KaiTOR: Diplomacy office",
            OfficeCondition,
            _ => ShowOffice(),
            false,
            8);

        starter.AddGameMenuOption(
            "castle",
            "kaitor_diplomacy_office_castle",
            "KaiTOR: Diplomacy office",
            OfficeCondition,
            _ => ShowOffice(),
            false,
            8);
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
            new("ledger", "Treaties and diplomatic trust", null),
            new("offer", "Propose a non-aggression pact", null, canRule, canRule ? string.Empty : "Only the ruling clan may sign kingdom treaties."),
            new("break", "Break a non-aggression pact", null, canRule && HasActivePlayerPact(diplomacy, playerKingdom), canRule ? "No active pact to break." : "Only the ruling clan may break kingdom treaties."),
            new("marriage", "Marriage compatibility rules", null),
            new("culture", "TOR culture conversion support", null),
        };

        MBInformationManager.ShowMultiSelectionInquiry(
            new MultiSelectionInquiryData(
                "KaiTOR Diplomacy",
                $"Kingdom: {playerKingdom.Name}.  Your role: {(canRule ? "ruler" : "vassal (read-only diplomacy)")}.",
                options,
                true,
                1,
                1,
                "Open",
                "Close",
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

            lines.Add($"{other.Name}: NAP={(nap ? remaining + "d" : "none")}, trust={trust}, breaches={breaches}, cooldown={cooldown}d");
        }

        if (lines.Count == 0) lines.Add("No KaiTOR treaty history for your kingdom yet.");
        ShowText("KaiTOR diplomatic ledger", string.Join("\n", lines));
    }

    private static void ShowNapTargets(KaiDiplomacyBehavior diplomacy, Kingdom playerKingdom)
    {
        var targets = new List<InquiryElement>();
        foreach (var other in Kingdom.All.Where(k => k != null && k != playerKingdom && !k.IsEliminated).OrderBy(k => k.Name.ToString()))
        {
            var allowed = diplomacy.CanCreateNonAggressionPact(playerKingdom, other, DefaultNapDays, out var reason);
            var score = diplomacy.GetNapAcceptanceScore(playerKingdom, other);
            var title = $"{other.Name}  [acceptance {score:+#;-#;0}]";
            targets.Add(new InquiryElement(other, title, null, allowed, allowed ? string.Empty : reason));
        }

        MBInformationManager.ShowMultiSelectionInquiry(
            new MultiSelectionInquiryData(
                "Propose non-aggression pact",
                "Choose a kingdom. Acceptance uses KaiTOR trust plus ruling-clan relations; TOR lore restrictions are checked first.",
                targets,
                true,
                1,
                1,
                "Choose",
                "Cancel",
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
            .Select(days => new InquiryElement(days, $"{days} campaign days", null))
            .ToList();

        MBInformationManager.ShowMultiSelectionInquiry(
            new MultiSelectionInquiryData(
                $"NAP with {target.Name}",
                $"Current acceptance score: {diplomacy.GetNapAcceptanceScore(playerKingdom, target)}. A score below 0 is normally refused.",
                durations,
                true,
                1,
                1,
                "Propose",
                "Cancel",
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
            ShowText("Proposal blocked", ruleReason);
            return;
        }

        var score = diplomacy.GetNapAcceptanceScore(playerKingdom, target);
        if (score < 0)
        {
            ShowText("Proposal refused", $"{target.Name} refused the pact. Acceptance score: {score}. Improve relations or rebuild diplomatic trust.");
            return;
        }

        if (diplomacy.TryCreateNonAggressionPact(playerKingdom, target, days, out var result))
            ShowText("Treaty signed", $"{playerKingdom.Name} and {target.Name} signed a {days}-day non-aggression pact.\n\n{result}");
        else
            ShowText("Proposal failed", result);
    }

    private static void ShowBreakTargets(KaiDiplomacyBehavior diplomacy, Kingdom playerKingdom)
    {
        var targets = Kingdom.All
            .Where(k => k != null && k != playerKingdom && !k.IsEliminated && diplomacy.IsNonAggressionPactActive(playerKingdom, k))
            .OrderBy(k => k.Name.ToString())
            .Select(k => new InquiryElement(k, $"{k.Name} — {diplomacy.GetRemainingDays(playerKingdom, k)} day(s) remaining", null))
            .ToList();

        if (targets.Count == 0)
        {
            ShowText("Break treaty", "Your kingdom has no active KaiTOR non-aggression pact.");
            return;
        }

        MBInformationManager.ShowMultiSelectionInquiry(
            new MultiSelectionInquiryData(
                "Break non-aggression pact",
                "Breaking a pact voluntarily costs 10 trust and blocks a new pact with that kingdom for 10 days.",
                targets,
                true,
                1,
                1,
                "Break",
                "Cancel",
                selected =>
                {
                    if (selected.Count == 0 || selected[0].Identifier is not Kingdom target) return;
                    InformationManager.ShowInquiry(new InquiryData(
                        "Confirm treaty break",
                        $"Break the pact with {target.Name}? Trust -10 and 10-day NAP cooldown.",
                        true,
                        true,
                        "Break pact",
                        "Cancel",
                        () =>
                        {
                            var broken = diplomacy.BreakNonAggressionPact(playerKingdom, target);
                            ShowText("Treaty", broken ? $"The pact with {target.Name} has been broken." : "The pact was no longer active.");
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
            "KaiTOR marriages",
            "TOR normally disables vanilla marriage. KaiTOR enables player-clan marriages only and keeps NPC-to-NPC automatic dynastic marriage disabled. " +
            "The pair must pass Bannerlord marriage safety checks and KaiTOR race/culture compatibility; incompatible races are never paired because Bannerlord offspring creation requires the parents' race to match.");
    }

    private static void ShowCultureSupport()
    {
        var behavior = Campaign.Current?.GetCampaignBehavior<KaiCultureAssimilationBehavior>();
        var lines = behavior?.DescribeCultureSupport().ToArray() ?? Array.Empty<string>();
        ShowText(
            "TOR full culture conversion",
            "100,000 denars, clan tier 3+, own town/castle. FULL means the culture exposes the required recruit, militia, tavern mercenary, caravan and wanderer templates.\n\n" +
            (lines.Length == 0 ? "No culture diagnostics available." : string.Join("\n", lines)));
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
            "OK",
            string.Empty,
            null,
            null), false, false);
    }
}
