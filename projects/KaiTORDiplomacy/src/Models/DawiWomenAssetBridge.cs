using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace KaiTOR.Diplomacy.Models;

/// <summary>
/// Runtime gate for the optional KaiTOR Dawi women asset pack.
/// The bridge only opens when a real female CharacterObject registered against the
/// existing TOR dwarf race is present. Missing/invalid assets therefore fail closed.
/// </summary>
internal static class DawiWomenAssetBridge
{
    public const string FemaleDawiLordTemplateId = "kaitor_dawi_woman_lord";

    // v0.5.2 Dawi live test: discovery is enabled, but IsAvailable still performs
    // strict runtime validation before any Dawi family logic can run.
    public const bool ForceSafeOffForLiveTest = false;

    public static bool IsAvailable
    {
        get
        {
            if (ForceSafeOffForLiveTest)
                return false;

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
