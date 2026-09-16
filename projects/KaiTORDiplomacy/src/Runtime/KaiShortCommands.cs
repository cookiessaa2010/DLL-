using System.Collections.Generic;
using TaleWorlds.Library;

namespace KaiTOR.Diplomacy.Runtime;

public static class KaiShortCommands
{
    [CommandLineFunctionality.CommandLineArgumentFunction("status", "kaitor")]
    public static string Status(List<string> arguments)
        => KaiDiplomacyCommands.Status(arguments);

    [CommandLineFunctionality.CommandLineArgumentFunction("marriage", "kaitor")]
    public static string Marriage(List<string> arguments)
        => KaiMarriageCommands.MarriageStatus(arguments);

    [CommandLineFunctionality.CommandLineArgumentFunction("dynasty", "kaitor")]
    public static string Dynasty(List<string> arguments)
        => KaiDynastyCommands.DynastyStatus(arguments);
}
