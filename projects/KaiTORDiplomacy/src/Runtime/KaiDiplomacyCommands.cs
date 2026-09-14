using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace KaiTOR.Diplomacy.Runtime;

public static class KaiDiplomacyCommands
{
    [CommandLineFunctionality.CommandLineArgumentFunction("status", "kaitor_diplomacy")]
    public static string Status(List<string> arguments)
    {
        if (arguments.Count != 0) return "Usage: kaitor_diplomacy.status";

        var behavior = GetBehavior();
        if (behavior == null) return "KaiTOR Diplomacy behavior is not loaded.";
        if (!behavior.RuntimeEnabled) return DisabledMessage;

        var pacts = behavior.DescribeActivePacts().ToArray();
        return pacts.Length == 0
            ? "KaiTOR Diplomacy: PASS; no active non-aggression pacts."
            : "KaiTOR Diplomacy: PASS\n" + string.Join("\n", pacts);
    }

    [CommandLineFunctionality.CommandLineArgumentFunction("kingdoms", "kaitor_diplomacy")]
    public static string Kingdoms(List<string> arguments)
    {
        if (arguments.Count != 0) return "Usage: kaitor_diplomacy.kingdoms";
        if (Campaign.Current == null) return "No campaign is active.";

        return string.Join("\n", Kingdom.All
            .Where(k => k != null && !k.IsEliminated)
            .OrderBy(k => k.StringId, StringComparer.Ordinal)
            .Select(k => $"{k.StringId} = {k.Name}"));
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
            ? $"Created NAP: {first.Name} <-> {second.Name}. {reason}"
            : "NAP refused: " + reason;
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
            ? $"Removed NAP: {first.Name} <-> {second.Name}. Trust -10; new NAP blocked for 10 days."
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
            : string.Join("\n", entries);
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

        return $"{first.Name} <-> {second.Name}: " +
               $"NAP={(behavior.IsNonAggressionPactActive(first, second) ? "active" : "none")}, " +
               $"remaining={behavior.GetRemainingDays(first, second)} day(s), " +
               $"trust={behavior.GetTrust(first, second)}, " +
               $"breaches={behavior.GetBreachCount(first, second)}, " +
               $"cooldown={behavior.GetNapCooldownRemainingDays(first, second)} day(s).";
    }

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
