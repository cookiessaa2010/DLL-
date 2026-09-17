using System.IO;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace KaiTOR.Diplomacy.Models;

/// <summary>
/// Runtime gate for the optional KaiTOR Dawi women asset pack.
/// The bridge only opens when a real female CharacterObject registered against the
/// existing TOR dwarf race is present AND the local asset staging tool completed.
/// Missing/invalid assets therefore fail closed before any hero is created.
/// </summary>
internal static class DawiWomenAssetBridge
{
    public const string FemaleDawiLordTemplateId = "kaitor_dawi_woman_lord";
    private const string AssetReadyMarker = "kaitor_dawi_assets_ready.flag";

    public const bool ForceSafeOffForLiveTest = false;

    public static bool IsAvailable
    {
        get
        {
            if (ForceSafeOffForLiveTest || !HasAssetReadyMarker())
                return false;

            var manager = MBObjectManager.Instance;
            if (manager == null)
                return false;

            var template = manager.GetObject<CharacterObject>(FemaleDawiLordTemplateId);
            if (template == null || !template.IsFemale)
                return false;

            var dwarfRace = FaceGen.GetRaceOrDefault("dwarf");
            var humanRace = FaceGen.GetRaceOrDefault("human");

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

    private static bool HasAssetReadyMarker()
    {
        try
        {
            var moduleRoot = TaleWorlds.ModuleManager.ModuleHelper.GetModuleFullPath("KaiTOR_DawiWomen");
            return !string.IsNullOrWhiteSpace(moduleRoot) &&
                   File.Exists(Path.Combine(moduleRoot, "ModuleData", AssetReadyMarker));
        }
        catch
        {
            return false;
        }
    }
}
