using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace KaiTOR.Diplomacy.Models;

/// <summary>
/// Runtime gate for the optional KaiTOR Dawi women asset pack.
/// KaiTOR never enables dwarf pregnancy merely because a female flag exists: the
/// expected template is registered only by the full asset pack after the female dwarf
/// skin, skeleton/action sets and meshes are present. This keeps a missing/partial asset
/// install fail-closed instead of allowing Bannerlord to create invalid offspring.
/// </summary>
internal static class DawiWomenAssetBridge
{
    public const string FemaleDawiLordTemplateId = "kaitor_dawi_woman_lord";

    public static bool IsAvailable
    {
        get
        {
            var manager = MBObjectManager.Instance;
            if (manager == null)
                return false;

            var template = manager.GetObject<CharacterObject>(FemaleDawiLordTemplateId);
            if (template == null || !template.IsFemale)
                return false;

            var dwarfRace = FaceGen.GetRaceOrDefault("dwarf");
            var humanRace = FaceGen.GetRaceOrDefault("human");

            // GetRaceOrDefault falls back when a custom race is absent. Never treat the
            // fallback human race as proof that the dwarf asset chain is installed.
            return dwarfRace != humanRace && template.Race == dwarfRace;
        }
    }

    public static bool IsSupportedDawiPair(Hero firstHero, Hero secondHero)
    {
        if (!IsAvailable || firstHero?.CharacterObject == null || secondHero?.CharacterObject == null)
            return false;

        var dwarfRace = FaceGen.GetRaceOrDefault("dwarf");
        return firstHero.CharacterObject.Race == dwarfRace &&
               secondHero.CharacterObject.Race == dwarfRace;
    }
}
