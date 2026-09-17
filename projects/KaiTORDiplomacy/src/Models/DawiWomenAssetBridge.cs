using System.IO;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace KaiTOR.Diplomacy.Models;

/// <summary>
/// Runtime gate for the integrated Dawi-women assets shipped inside KaiTOR_Diplomacy.
/// The bridge opens only when the female Dawi CharacterObject is registered against
/// TOR's real dwarf race and the verified standalone TPAC is present in this module.
/// Missing or incomplete assets therefore fail closed before any hero is created.
/// </summary>
internal static class DawiWomenAssetBridge
{
    public const string FemaleDawiLordTemplateId = "kaitor_dawi_woman_lord";
    private const string ModuleId = "KaiTOR_Diplomacy";
    private const string AssetReadyMarker = "kaitor_dawi_assets_ready.flag";
    private const string AssetPackName = "kaitor_dawi_female.tpac";
    private const long ExpectedAssetPackBytes = 29213977L;

    public const bool ForceSafeOffForLiveTest = false;

    public static bool IsAvailable
    {
        get
        {
            if (ForceSafeOffForLiveTest || !HasIntegratedAssetPack())
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

    private static bool HasIntegratedAssetPack()
    {
        try
        {
            var moduleRoot = TaleWorlds.ModuleManager.ModuleHelper.GetModuleFullPath(ModuleId);
            if (string.IsNullOrWhiteSpace(moduleRoot))
                return false;

            var marker = Path.Combine(moduleRoot, "ModuleData", AssetReadyMarker);
            var pack = Path.Combine(moduleRoot, "AssetPackages", AssetPackName);
            if (!File.Exists(marker) || !File.Exists(pack))
                return false;

            return new FileInfo(pack).Length == ExpectedAssetPackBytes;
        }
        catch
        {
            return false;
        }
    }
}
