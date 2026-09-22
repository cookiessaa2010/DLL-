using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Explicit diagnostic child spawner for TOR Character Creation coverage tests.
/// Nothing here runs automatically in normal campaigns.
/// </summary>
public static class KaiChildTestCommands
{
    [CommandLineFunctionality.CommandLineArgumentFunction("help", "kaitor_child")]
    public static string Help(List<string> arguments)
    {
        if (arguments.Count != 0) return "Usage: kaitor_child.help";
        return string.Join("\n", new[]
        {
            "KaiTOR child test commands:",
            "kaitor_child.spawn_empire [age] [m|f]",
            "kaitor_child.spawn_bretonnia [age] [m|f]",
            "kaitor_child.spawn_sylvania [age] [m|f]",
            "kaitor_child.spawn_mousillon [age] [m|f]",
            "kaitor_child.spawn_asrai [age] [m|f]",
            "kaitor_child.spawn_eonir [age] [m|f]",
            "kaitor_child.spawn_dawi [age] [m]",
            "kaitor_child.spawn_greenskin [age] [m]",
            "kaitor_child.age <heroId> <0-18>",
            "kaitor_child.auto_path <heroId>",
            "kaitor_child.list",
            "kaitor_child.coverage",
            "",
            "Recommended live test: spawn at 8 -> choose stage 1 -> age 14 -> choose stage 2 -> age 16 -> choose profession/specialization -> age 18 -> verify career package."
        });
    }

    [CommandLineFunctionality.CommandLineArgumentFunction("spawn_empire", "kaitor_child")]
    public static string SpawnEmpire(List<string> arguments)
        => Spawn(arguments, "empire", "human", "Империя", allowFemale: true);

    [CommandLineFunctionality.CommandLineArgumentFunction("spawn_bretonnia", "kaitor_child")]
    public static string SpawnBretonnia(List<string> arguments)
        => Spawn(arguments, "vlandia", "bretonnian", "Бретонния", allowFemale: true);

    [CommandLineFunctionality.CommandLineArgumentFunction("spawn_sylvania", "kaitor_child")]
    public static string SpawnSylvania(List<string> arguments)
        => Spawn(arguments, "khuzait", "human", "Сильвания", allowFemale: true);

    [CommandLineFunctionality.CommandLineArgumentFunction("spawn_mousillon", "kaitor_child")]
    public static string SpawnMousillon(List<string> arguments)
        => Spawn(arguments, "mousillon", "human", "Мусильон", allowFemale: true);

    [CommandLineFunctionality.CommandLineArgumentFunction("spawn_asrai", "kaitor_child")]
    public static string SpawnAsrai(List<string> arguments)
        => Spawn(arguments, "battania", "elf", "Азраи", allowFemale: true);

    [CommandLineFunctionality.CommandLineArgumentFunction("spawn_eonir", "kaitor_child")]
    public static string SpawnEonir(List<string> arguments)
        => Spawn(arguments, "eonir", "elf", "Эонир", allowFemale: true);

    [CommandLineFunctionality.CommandLineArgumentFunction("spawn_dawi", "kaitor_child")]
    public static string SpawnDawi(List<string> arguments)
        => Spawn(arguments, "sturgia", "dwarf", "Дави", allowFemale: false);

    [CommandLineFunctionality.CommandLineArgumentFunction("spawn_greenskin", "kaitor_child")]
    public static string SpawnGreenskin(List<string> arguments)
        => Spawn(arguments, "aserai", "orc", "Зеленокожие", allowFemale: false);

    [CommandLineFunctionality.CommandLineArgumentFunction("age", "kaitor_child")]
    public static string SetAge(List<string> arguments)
    {
        if (Campaign.Current == null) return "No campaign is active.";
        if (arguments.Count != 2) return "Usage: kaitor_child.age <heroId> <0-18>";
        if (!int.TryParse(arguments[1], out var age) || age < 0 || age > 18)
            return "Age must be an integer from 0 to 18.";

        var hero = FindHero(arguments[0]);
        if (hero == null) return "Unknown living player-clan hero: " + arguments[0];

        hero.SetBirthDay(CampaignTime.YearsFromNow(-age));
        KaiRuntimeLog.Write("CHILD_TEST_AGE", $"hero={hero.StringId}; age={age}");
        return $"Test age set: {hero.Name} ({hero.StringId}) -> {age}. Resume campaign time for one daily tick.";
    }

    [CommandLineFunctionality.CommandLineArgumentFunction("auto_path", "kaitor_child")]
    public static string AutoPath(List<string> arguments)
    {
        if (Campaign.Current == null) return "No campaign is active.";
        if (arguments.Count != 1) return "Usage: kaitor_child.auto_path <heroId>";

        var hero = FindHero(arguments[0]);
        if (hero == null) return "Unknown living player-clan hero: " + arguments[0];

        var education = Campaign.Current.GetCampaignBehavior<KaiLoreEducationBehavior>();
        if (education == null) return "KaiLoreEducationBehavior is not loaded.";

        var ok = education.ApplySyntheticFullTorPath(hero, out var report);
        return ok
            ? $"TOR full path applied: {hero.Name} ({hero.StringId}); {report}"
            : $"TOR full path failed: {hero.Name} ({hero.StringId}); {report}";
    }

    [CommandLineFunctionality.CommandLineArgumentFunction("coverage", "kaitor_child")]
    public static string Coverage(List<string> arguments)
    {
        if (arguments.Count != 0) return "Usage: kaitor_child.coverage";
        if (Campaign.Current == null) return "No campaign is active.";

        var education = Campaign.Current.GetCampaignBehavior<KaiLoreEducationBehavior>();
        return education == null
            ? "KaiLoreEducationBehavior is not loaded."
            : string.Join("\n", education.DescribeTorCoverage());
    }

    [CommandLineFunctionality.CommandLineArgumentFunction("list", "kaitor_child")]
    public static string ListChildren(List<string> arguments)
    {
        if (arguments.Count != 0) return "Usage: kaitor_child.list";
        if (Campaign.Current == null || Clan.PlayerClan == null) return "No campaign/player clan is active.";

        var heroes = Clan.PlayerClan.Heroes
            .Where(h => h != null && h.IsAlive && h != Hero.MainHero)
            .OrderBy(h => h.Age)
            .ThenBy(h => h.StringId, StringComparer.Ordinal)
            .Select(h =>
                $"{h.StringId} | {h.Name} | age={h.Age:0.0} | culture={h.Culture?.StringId ?? "none"} | race={h.CharacterObject?.Race.ToString() ?? "none"} | career={TorProfessionEffectBridge.GetCurrentCareerId(h) ?? "none"}")
            .ToArray();

        return heroes.Length == 0 ? "Player clan has no other living heroes." : string.Join("\n", heroes);
    }

    private static string Spawn(
        List<string> arguments,
        string cultureId,
        string raceId,
        string label,
        bool allowFemale)
    {
        if (Campaign.Current == null || Clan.PlayerClan == null || Hero.MainHero == null)
            return "No campaign/player clan is active.";
        if (arguments.Count > 2)
            return "Usage: kaitor_child.spawn_* [age] [m|f]";

        var age = 8;
        if (arguments.Count >= 1 && (!int.TryParse(arguments[0], out age) || age < 0 || age > 17))
            return "Spawn age must be an integer from 0 to 17.";

        var female = false;
        if (arguments.Count >= 2)
        {
            var sex = arguments[1]?.Trim().ToLowerInvariant();
            if (sex == "f" || sex == "female" || sex == "ж")
                female = true;
            else if (sex != "m" && sex != "male" && sex != "м")
                return "Sex must be m or f.";
        }

        if (female && !allowFemale)
            return $"{label}: female diagnostic child is disabled for this race.";

        try
        {
            var templateHero = ResolveTemplate(cultureId, female);
            if (templateHero?.CharacterObject == null)
                return $"No {(female ? "female" : "male")} TOR hero template found for culture={cultureId}.";

            var template = templateHero.CharacterObject.OriginalCharacter ?? templateHero.CharacterObject;
            var settlement = MobileParty.MainParty?.CurrentSettlement
                             ?? Clan.PlayerClan.HomeSettlement
                             ?? templateHero.HomeSettlement;

            var child = HeroCreator.CreateSpecialHero(
                template,
                settlement,
                Clan.PlayerClan,
                null,
                age);

            if (child == null)
                return "HeroCreator.CreateSpecialHero returned null.";

            child.SetBirthDay(CampaignTime.YearsFromNow(-age));
            child.CharacterObject.Race = FaceGen.GetRaceOrDefault(raceId);
            child.ChangeState(Hero.CharacterStates.Active);
            child.SetNewOccupation(Occupation.Lord);
            child.IsKnownToPlayer = true;

            if (Hero.MainHero.IsFemale)
                child.Mother = Hero.MainHero;
            else
                child.Father = Hero.MainHero;

            var serial = Clan.PlayerClan.Heroes.Count(h =>
                h != null &&
                h.IsAlive &&
                h.Name != null &&
                h.Name.ToString().StartsWith("TEST ", StringComparison.OrdinalIgnoreCase)) + 1;
            var name = new TextObject($"TEST {label} Child {serial}");
            child.SetName(name, name);

            KaiRuntimeLog.Write("CHILD_TEST_SPAWN",
                $"hero={child.StringId}; culture={cultureId}; race={raceId}; age={age}; female={female}; template={templateHero.StringId}; parent={Hero.MainHero.StringId}");

            return $"Spawned: {child.Name} | id={child.StringId} | age={age} | culture={cultureId} | race={raceId}. " +
                   "Resume campaign time for one daily tick to open the TOR education stage.";
        }
        catch (Exception ex)
        {
            KaiRuntimeLog.Exception("CHILD_TEST_SPAWN_FAIL", ex,
                $"culture={cultureId}; race={raceId}; age={age}; female={female}");
            return "Spawn failed: " + ex.GetBaseException().Message;
        }
    }

    private static Hero ResolveTemplate(string cultureId, bool female)
    {
        return Hero.AllAliveHeroes
            .Where(h =>
                h != null &&
                h.IsAlive &&
                h.IsActive &&
                h.CharacterObject != null &&
                h.Culture != null &&
                string.Equals(h.Culture.StringId, cultureId, StringComparison.OrdinalIgnoreCase) &&
                h.IsFemale == female)
            .OrderByDescending(h => h.IsLord)
            .ThenBy(h => h.StringId, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static Hero FindHero(string id)
    {
        if (Clan.PlayerClan == null || string.IsNullOrWhiteSpace(id)) return null;
        return Clan.PlayerClan.Heroes.FirstOrDefault(h =>
            h != null &&
            h.IsAlive &&
            string.Equals(h.StringId, id, StringComparison.OrdinalIgnoreCase));
    }
}
