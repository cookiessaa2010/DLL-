using System;
using System.Collections.Generic;
using System.Linq;
using KaiTOR.Diplomacy.Models;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace KaiTOR.Diplomacy.Runtime;

public static class KaiDiplomacyCommands
{
    [CommandLineFunctionality.CommandLineArgumentFunction("help", "kaitor_diplomacy")]
    public static string Help(List<string> arguments)
    {
        if (arguments.Count != 0) return "Usage: kaitor_diplomacy.help";
        return string.Join("\n", new[]
        {
            "KaiTOR Diplomacy commands:",
            "kaitor_diplomacy.status",
            "kaitor_diplomacy.world_status",
            "kaitor_diplomacy.marriages",
            "kaitor_diplomacy.family_rules",
            "kaitor_diplomacy.ui_status",
            "kaitor_diplomacy.ui_family",
            "kaitor_diplomacy.live_test_start",
            "kaitor_diplomacy.live_test_snapshot",
            "kaitor_diplomacy.live_test_path",
            "kaitor_diplomacy.live_test_mark <text>",
            "kaitor_diplomacy.save_status",
            "kaitor_diplomacy.culture_support",
            "kaitor_diplomacy.settlement",
            "kaitor_diplomacy.kingdoms",
            "kaitor_diplomacy.ledger",
            "kaitor_diplomacy.inspect <kingdomA> <kingdomB>"
        });
    }

    [CommandLineFunctionality.CommandLineArgumentFunction("status", "kaitor_diplomacy")]
    public static string Status(List<string> arguments)
    {
        if (arguments.Count != 0) return "Usage: kaitor_diplomacy.status";

        var behavior = GetBehavior();
        if (behavior == null) return "KaiTOR Diplomacy behavior is not loaded.";
        if (!behavior.RuntimeEnabled) return C(DisabledMessage + " " + behavior.DescribeSaveCompatibility());

        var pacts = behavior.DescribeActivePacts().ToArray();
        return C(pacts.Length == 0
            ? "KaiTOR Diplomacy: PASS; no active non-aggression pacts.\n" + behavior.DescribeSaveCompatibility()
            : "KaiTOR Diplomacy: PASS\n" + behavior.DescribeSaveCompatibility() + "\n" + string.Join("\n", pacts));
    }

    [CommandLineFunctionality.CommandLineArgumentFunction("world_status", "kaitor_diplomacy")]
    public static string WorldStatus(List<string> arguments)
    {
        if (arguments.Count != 0) return "Usage: kaitor_diplomacy.world_status";
        if (Campaign.Current == null) return "No campaign is active.";

        var marriedPairs = Hero.AllAliveHeroes.Count(hero =>
            hero?.Spouse != null &&
            hero.Spouse.IsAlive &&
            string.CompareOrdinal(hero.StringId, hero.Spouse.StringId) < 0);

        var activeKingdoms = Kingdom.All.Count(kingdom => kingdom != null && !kingdom.IsEliminated);
        var activeClans = Clan.All.Count(clan => clan != null && !clan.IsEliminated);

        return string.Join("\n", new[]
        {
            $"KaiTOR world lifecycle: {(CampaignOptions.IsLifeDeathCycleDisabled ? "DISABLED" : "ENABLED")}",
            $"MarriageModel: {Campaign.Current.Models.MarriageModel?.GetType().FullName ?? "<null>"}",
            $"PregnancyModel: {Campaign.Current.Models.PregnancyModel?.GetType().FullName ?? "<null>"}",
            $"HeroDeathModel: {Campaign.Current.Models.HeroDeathProbabilityCalculationModel?.GetType().FullName ?? "<null>"}",
            $"Active kingdoms: {activeKingdoms}",
            $"Active clans: {activeClans}",
            $"Living married couples: {marriedPairs}"
        });
    }

    [CommandLineFunctionality.CommandLineArgumentFunction("marriages", "kaitor_diplomacy")]
    public static string Marriages(List<string> arguments)
    {
        if (arguments.Count != 0) return "Usage: kaitor_diplomacy.marriages";
        if (Campaign.Current == null) return "No campaign is active.";

        var pairs = Hero.AllAliveHeroes
            .Where(hero =>
                hero?.Spouse != null &&
                hero.Spouse.IsAlive &&
                string.CompareOrdinal(hero.StringId, hero.Spouse.StringId) < 0)
            .OrderBy(hero => hero.StringId, StringComparer.Ordinal)
            .Select(hero =>
            {
                var spouse = hero.Spouse;
                var fertility = TorFamilySafety.CanUseVanillaPregnancy(hero, spouse)
                    ? "offspring-safe"
                    : "childless";
                return $"{hero.StringId} [{hero.Culture?.StringId ?? "none"}; {hero.Clan?.StringId ?? "none"}] <-> " +
                       $"{spouse.StringId} [{spouse.Culture?.StringId ?? "none"}; {spouse.Clan?.StringId ?? "none"}] ({fertility})";
            })
            .ToArray();

        return pairs.Length == 0
            ? "KaiTOR world marriages: none."
            : $"KaiTOR world marriages ({pairs.Length}):\n" + string.Join("\n", pairs);
    }

    [CommandLineFunctionality.CommandLineArgumentFunction("family_rules", "kaitor_diplomacy")]
    public static string FamilyRules(List<string> arguments)
    {
        if (arguments.Count != 0) return "Usage: kaitor_diplomacy.family_rules";

        var lines = new List<string>
        {
            "KaiTOR lore family rules:",
            "TOR native marriage model: globally disabled; KaiTOR wrapper: ACTIVE."
        };
        lines.AddRange(KaiRaceLifecycle.DescribeRules());
        lines.Add("Dawi: abstract house union; no female Hero/assets; only adult male heirs materialize.");
        lines.Add("NPC marriage: same culture + same FaceGen race; no upper marriage-age cap.");
        lines.Add("Pregnancy: opposite sex + same FaceGen race + lore biology gate.");
        return C(string.Join("\n", lines));
    }

    [CommandLineFunctionality.CommandLineArgumentFunction("ui_status", "kaitor_diplomacy")]
    public static string UiStatus(List<string> arguments)
    {
        if (arguments.Count != 0) return "Usage: kaitor_diplomacy.ui_status";
        if (Campaign.Current == null) return "No campaign is active.";

        var family = Campaign.Current.GetCampaignBehavior<KaiFamilyAffairsBehavior>();
        var diplomacy = GetBehavior();
        var culture = Campaign.Current.GetCampaignBehavior<KaiCultureAssimilationBehavior>();
        var incoming = Campaign.Current.GetCampaignBehavior<KaiIncomingMarriageProposalBehavior>();

        return C(string.Join("\n", new[]
        {
            KaiFamilyAffairsBehavior.DescribeUiStatus(),
            $"FamilyAffairsBehavior={(family != null ? "OK" : "MISSING")}",
            $"DiplomacyBehavior={(diplomacy != null ? (diplomacy.RuntimeEnabled ? "OK" : "BLOCKED") : "MISSING")}",
            $"CultureAssimilationBehavior={(culture != null ? "OK" : "MISSING")}",
            $"IncomingMarriageProposalBehavior={(incoming != null ? "OK" : "MISSING")}"
        }));
    }

    [CommandLineFunctionality.CommandLineArgumentFunction("ui_family", "kaitor_diplomacy")]
    public static string UiFamily(List<string> arguments)
    {
        if (arguments.Count != 0) return "Usage: kaitor_diplomacy.ui_family";
        return C(KaiFamilyAffairsBehavior.OpenFamilyMenuFromConsole());
    }

    [CommandLineFunctionality.CommandLineArgumentFunction("live_test_start", "kaitor_diplomacy")]
    public static string LiveTestStart(List<string> arguments)
    {
        if (arguments.Count != 0) return "Usage: kaitor_diplomacy.live_test_start";
        if (Campaign.Current == null) return "No campaign is active.";
        KaiLiveTestBehavior.ResetAndSnapshot();
        return "KaiTOR live-test log started. Path: %LOCALAPPDATA%\\KaiTORDiplomacy\\KaiTOR-LiveTest.log";
    }

    [CommandLineFunctionality.CommandLineArgumentFunction("live_test_snapshot", "kaitor_diplomacy")]
    public static string LiveTestSnapshot(List<string> arguments)
    {
        if (arguments.Count != 0) return "Usage: kaitor_diplomacy.live_test_snapshot";
        if (Campaign.Current == null) return "No campaign is active.";
        KaiLiveTestBehavior.AppendSnapshot();
        return "KaiTOR live-test snapshot appended.";
    }

    [CommandLineFunctionality.CommandLineArgumentFunction("live_test_path", "kaitor_diplomacy")]
    public static string LiveTestPath(List<string> arguments)
    {
        if (arguments.Count != 0) return "Usage: kaitor_diplomacy.live_test_path";
        return "%LOCALAPPDATA%\\KaiTORDiplomacy\\KaiTOR-LiveTest.log";
    }

    [CommandLineFunctionality.CommandLineArgumentFunction("live_test_mark", "kaitor_diplomacy")]
    public static string LiveTestMark(List<string> arguments)
    {
        if (arguments.Count == 0) return "Usage: kaitor_diplomacy.live_test_mark <text>";
        var marker = string.Join(" ", arguments);
        KaiLiveTestLog.Write("manual", "MARK", $"text={marker}");
        return "Marker added to KaiTOR-LiveTest.log.";
    }

    [CommandLineFunctionality.CommandLineArgumentFunction("save_status", "kaitor_diplomacy")]
    public static string SaveStatus(List<string> arguments)
    {
        if (arguments.Count != 0) return "Usage: kaitor_diplomacy.save_status";
        var behavior = GetBehavior();
        return C(behavior?.DescribeSaveCompatibility() ?? "KaiTOR Diplomacy behavior is not loaded.");
    }

    [CommandLineFunctionality.CommandLineArgumentFunction("culture_support", "kaitor_diplomacy")]
    public static string CultureSupport(List<string> arguments)
    {
        if (arguments.Count != 0) return "Usage: kaitor_diplomacy.culture_support";
        if (Campaign.Current == null) return "No campaign is active.";

        var behavior = Campaign.Current.GetCampaignBehavior<KaiCultureAssimilationBehavior>();
        if (behavior == null) return "KaiTOR culture conversion behavior is not loaded.";

        return C("KaiTOR TOR culture support matrix:\n" + string.Join("\n", behavior.DescribeCultureSupport()));
    }

    [CommandLineFunctionality.CommandLineArgumentFunction("settlement", "kaitor_diplomacy")]
    public static string SettlementStatus(List<string> arguments)
    {
        if (arguments.Count != 0) return "Usage: kaitor_diplomacy.settlement";
        if (Campaign.Current == null) return "No campaign is active.";

        var behavior = Campaign.Current.GetCampaignBehavior<KaiCultureAssimilationBehavior>();
        return C(behavior?.DescribeCurrentSettlement() ?? "KaiTOR culture conversion behavior is not loaded.");
    }

    [CommandLineFunctionality.CommandLineArgumentFunction("kingdoms", "kaitor_diplomacy")]
    public static string Kingdoms(List<string> arguments)
    {
        if (arguments.Count != 0) return "Usage: kaitor_diplomacy.kingdoms";
        if (Campaign.Current == null) return "No campaign is active.";

        return string.Join("\n", Kingdom.All
            .Where(k => k != null && !k.IsEliminated)
            .OrderBy(k => k.StringId, StringComparer.Ordinal)
            .Select(k => k.StringId));
    }

    [CommandLineFunctionality.CommandLineArgumentFunction("nap", "kaitor_diplomacy")]
    public static string CreateNap(List<string> arguments)
    {
        if (arguments.Count != 3)
            return "Usage: kaitor_diplomacy.nap <kingdomA> <kingdomB> <days>";

        var behavior = GetBehavior();
        if (behavior == null) return "KaiTOR Diplomacy behavior is not loaded.";
        if (!behavior.RuntimeEnabled) return DisabledMessage;

        var first = FindKingdom(arguments[0]);
        var second = FindKingdom(arguments[1]);
        if (first == null) return $"Unknown kingdom: {arguments[0]}";
        if (second == null) return $"Unknown kingdom: {arguments[1]}";
        if (!int.TryParse(arguments[2], out var days)) return "Days must be an integer.";

        var created = behavior.TryCreateNonAggressionPact(first, second, days, out var reason);
        return created
            ? C($"Created NAP: {first.StringId} <-> {second.StringId}. {reason}")
            : C("NAP refused: " + reason);
    }

    [CommandLineFunctionality.CommandLineArgumentFunction("break_nap", "kaitor_diplomacy")]
    public static string BreakNap(List<string> arguments)
    {
        if (arguments.Count != 2)
            return "Usage: kaitor_diplomacy.break_nap <kingdomA> <kingdomB>";

        var behavior = GetBehavior();
        if (behavior == null) return "KaiTOR Diplomacy behavior is not loaded.";
        if (!behavior.RuntimeEnabled) return DisabledMessage;

        var first = FindKingdom(arguments[0]);
        var second = FindKingdom(arguments[1]);
        if (first == null || second == null) return "One or both kingdom ids are unknown.";

        return behavior.BreakNonAggressionPact(first, second)
            ? $"Removed NAP: {first.StringId} <-> {second.StringId}. Trust -10; new NAP blocked for 10 days."
            : "No active NAP existed for that pair.";
    }

    [CommandLineFunctionality.CommandLineArgumentFunction("ledger", "kaitor_diplomacy")]
    public static string Ledger(List<string> arguments)
    {
        if (arguments.Count != 0) return "Usage: kaitor_diplomacy.ledger";

        var behavior = GetBehavior();
        if (behavior == null) return "KaiTOR Diplomacy behavior is not loaded.";
        if (!behavior.RuntimeEnabled) return DisabledMessage;

        var entries = behavior.DescribeDiplomaticHistory().ToArray();
        return entries.Length == 0
            ? "KaiTOR Diplomacy ledger is empty."
            : C(string.Join("\n", entries));
    }

    [CommandLineFunctionality.CommandLineArgumentFunction("inspect", "kaitor_diplomacy")]
    public static string Inspect(List<string> arguments)
    {
        if (arguments.Count != 2)
            return "Usage: kaitor_diplomacy.inspect <kingdomA> <kingdomB>";

        var behavior = GetBehavior();
        if (behavior == null) return "KaiTOR Diplomacy behavior is not loaded.";
        if (!behavior.RuntimeEnabled) return DisabledMessage;

        var first = FindKingdom(arguments[0]);
        var second = FindKingdom(arguments[1]);
        if (first == null || second == null) return "One or both kingdom ids are unknown.";

        return $"{first.StringId} <-> {second.StringId}: " +
               $"NAP={(behavior.IsNonAggressionPactActive(first, second) ? "active" : "none")}, " +
               $"remaining={behavior.GetRemainingDays(first, second)} day(s), " +
               $"trust={behavior.GetTrust(first, second)}, " +
               $"breaches={behavior.GetBreachCount(first, second)}, " +
               $"cooldown={behavior.GetNapCooldownRemainingDays(first, second)} day(s).";
    }

    private static string C(string value) => KaiConsoleText.Safe(value);

    private const string DisabledMessage =
        "KaiTOR Diplomacy runtime is disabled by the TOR compatibility gate.";

    private static KaiDiplomacyBehavior GetBehavior()
        => Campaign.Current?.GetCampaignBehavior<KaiDiplomacyBehavior>();

    private static Kingdom FindKingdom(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        return Kingdom.All.FirstOrDefault(k =>
            string.Equals(k.StringId, id, StringComparison.OrdinalIgnoreCase));
    }
}
