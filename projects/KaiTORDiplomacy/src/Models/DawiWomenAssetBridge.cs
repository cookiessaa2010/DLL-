using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace KaiTOR.Diplomacy.Models;

/// <summary>
/// Runtime gate for the optional KaiTOR Dawi women asset pack.
/// The current live-test build intentionally keeps this gate hard-disabled so the
/// campaign can be tested without female-Dawi assets affecting stability, saves or
/// pregnancy. Remove ForceSafeOffForLiveTest only after the asset chain passes its
/// separate in-game rig/FaceGen/child-growth tests.
/// </summary>
internal static class DawiWomenAssetBridge
{
    public const string FemaleDawiLordTemplateId = "kaitor_dawi_woman_lord";

    // TEST PHASE: hard safety latch. This deliberately overrides asset discovery.
    // Dawi women stay absent and Dawi pregnancy stays blocked for the first live test.
    public const bool ForceSafeOffForLiveTest = true;

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
