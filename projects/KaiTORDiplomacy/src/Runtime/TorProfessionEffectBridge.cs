using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Applies TOR character-creation profession effects to an arbitrary dynasty hero.
/// We deliberately do NOT call HeroExtensions.AddCareer for non-player heroes: several
/// TOR career InitialCareerSetup implementations reference Hero.MainHero internally.
/// Instead CareerID/root choice is written to that hero's own ExtendedInfo and the
/// personal effects from TORCharacterCreationContentHandler are replayed explicitly.
/// World/player-only effects (teleports, kingdom joins, companion grants, oak upgrades)
/// are intentionally excluded.
/// </summary>
internal static class TorProfessionEffectBridge
{
    private static Assembly _torAssembly;
    private static Type _heroExtensions;
    private static Type _torCareers;
    private static Type _torSkills;
    private static Type _torPerks;
    private static Type _spellCastingLevel;
    private static Type _religionObject;

    public static bool IsAvailable => EnsureTypes();

    private static readonly HashSet<string> SupportedProfessions = new(StringComparer.OrdinalIgnoreCase)
    {
        "option_3_empire_magister_apprentice",
        "option_3_empire_knight",
        "option_3_empire_priest_acolyte",
        "option_3_empire_witch_hunter",
        "option_3_empire_free_company",
        "option_3_bretonnia_damsel",
        "option_3_bretonnia_knight_errant",
        "option_3_we_spellsinger",
        "option_3_we_waywatcher",
        "option_3_we_warden",
        "option_3_eo_greylord_apprentice",
        "option_3_eo_ghost_strider",
        "option_3_vc_vampire",
        "option_3_vc_necromancer",
        "option_3_mousillon_vampire",
        "option_3_mousillon_necromancer",
        "option_3_mousillon_knight_errant",
        "option_3_dw_shield_breaker",
        "option_3_dw_slayer",
        "option_3_dw_rune_smith",
        "option_3_gs_path_of_boss",
        "option_3_gs_path_of_bully",
        "option_3_gs_path_of_boar_boys",
        "option_3_gs_path_of_savage_boys",
        "option_3_gs_path_of_shaman"
    };

    private static readonly HashSet<string> SupportedSpecializations = new(StringComparer.OrdinalIgnoreCase)
    {
        "priest_sigmar",
        "priest_ulric",
        "bloodline_von_carstein",
        "bloodline_blood_dragon",
        "bloodline_necrarch",
        "bloodline_von_carstein_mous",
        "bloodline_blood_dragon_mous",
        "bloodline_necrarch_mous",
        "knight_blazing_sun",
        "knight_panthers",
        "knight_white_wolf",
        "knight_griphon",
        "knight_gryphon",
        "knight_reiksguard",
        "lore_of_fire",
        "lore_of_light",
        "lore_of_metal",
        "lore_of_death",
        "lore_of_beasts",
        "lore_of_heavens",
        "lore_of_life"
    };

    public static bool IsProfessionSupported(string professionId)
        => !string.IsNullOrWhiteSpace(professionId) && SupportedProfessions.Contains(professionId);

    public static bool IsSpecializationSupported(string specializationId)
        => string.IsNullOrWhiteSpace(specializationId) || SupportedSpecializations.Contains(specializationId);

    public static bool ApplyStage2Effect(Hero hero, string optionId, out string error)
    {
        error = string.Empty;
        if (hero == null || string.IsNullOrWhiteSpace(optionId))
            return true;
        if (!EnsureTypes())
        {
            error = "TOR reflection types unavailable";
            return false;
        }

        try
        {
            string attribute = null;
            string religion = null;
            var influence = 0;

            switch (optionId)
            {
                case "option_2_dw_umgi": attribute = "HumanGrudge"; break;
                case "option_2_dw_elgi": attribute = "ElfGrudge"; break;
                case "option_2_dw_urks": attribute = "GreenskinGrudge"; break;
                case "option_2_dw_zanguzaz": attribute = "UndeadGrudge"; break;
                case "option_2_dw_thaggoraki": attribute = "SkavenGrudge"; break;
                case "option_2_we_kurnous": attribute = "WEKithbandSymbol"; religion = "cult_of_kurnous"; influence = 40; break;
                case "option_2_we_isha": attribute = "WETreekinSymbol"; religion = "cult_of_isha"; influence = 40; break;
                case "option_2_we_loec": attribute = "WEWardancerSymbol"; religion = "cult_of_loec"; influence = 40; break;
                case "option_2_we_vaul": attribute = "WEKithbandSymbol"; religion = "cult_of_vaul"; influence = 40; break;
                case "option_2_we_khaine": attribute = "WEKithbandSymbol"; religion = "cult_of_anath_raema"; influence = 40; break;
            }

            if (!string.IsNullOrWhiteSpace(attribute))
                AddAttribute(hero, attribute);
            if (!string.IsNullOrWhiteSpace(religion) && influence != 0)
                AddReligion(hero, religion, influence);

            return true;
        }
        catch (Exception ex)
        {
            error = ex.GetBaseException().Message;
            return false;
        }
    }

    public static bool ApplyProfessionPackage(Hero hero, string professionId, string specializationId, out string error)
    {
        error = string.Empty;
        if (hero == null || string.IsNullOrWhiteSpace(professionId))
        {
            error = "hero/profession missing";
            return false;
        }
        if (!EnsureTypes())
        {
            error = "TOR reflection types unavailable";
            return false;
        }

        try
        {
            // Character Creation grants this generic capability to every finished hero.
            // CanPlaceArtillery is intentionally omitted: it is a player/world utility,
            // not a profession prerequisite for dynasty children.
            AddAttribute(hero, "AbilityUser");

            switch (professionId)
            {
                case "option_3_empire_magister_apprentice":
                    AddAttribute(hero, "SpellCaster");
                    AddAbility(hero, "Dart");
                    AddLore(hero, "MinorMagic");
                    SetEntryCasting(hero);
                    SetSkillFloor(hero, "Spellcraft", 25);
                    AddTorPerk(hero, "Spellcraft", "EntrySpells");
                    SetCareerSafe(hero, "ImperialMagister");
                    break;

                case "option_3_empire_knight":
                    SetCareerSafe(hero, "KnightOldWorld");
                    break;

                case "option_3_bretonnia_damsel":
                    AddAttribute(hero, "SpellCaster");
                    AddAttribute(hero, "PriestLady");
                    AddAbility(hero, "Dart");
                    AddAbility(hero, "AuraOfTheLady");
                    AddLore(hero, "MinorMagic");
                    AddLore(hero, "LoreOfBeasts");
                    AddAbility(hero, "AmberSpear");
                    SetEntryCasting(hero);
                    SetSkillFloor(hero, "Spellcraft", 25);
                    SetSkillFloor(hero, "Faith", 25);
                    AddTorPerk(hero, "Spellcraft", "EntrySpells");
                    SetCareerSafe(hero, "GrailDamsel");
                    break;

                case "option_3_we_spellsinger":
                    AddAttribute(hero, "SpellCaster");
                    AddLore(hero, "LoreOfLife");
                    AddLore(hero, "LoreOfBeasts");
                    AddAbility(hero, "SummerHeat");
                    AddAbility(hero, "AmberSpear");
                    SetEntryCasting(hero);
                    SetSkillFloor(hero, "Spellcraft", 25);
                    AddTorPerk(hero, "Spellcraft", "EntrySpells");
                    SetCareerSafe(hero, "Spellsinger");
                    break;

                case "option_3_eo_greylord_apprentice":
                    AddAttribute(hero, "SpellCaster");
                    AddLore(hero, "LoreOfFire");
                    AddAbility(hero, "BoltOfAqshy");
                    SetEntryCasting(hero);
                    SetSkillFloor(hero, "Spellcraft", 25);
                    AddTorPerk(hero, "Spellcraft", "EntrySpells");
                    SetCareerSafe(hero, "GreyLord");
                    break;

                case "option_3_empire_priest_acolyte":
                    AddAttribute(hero, "Priest");
                    break;

                case "option_3_vc_vampire":
                case "option_3_mousillon_vampire":
                    AddAttribute(hero, "Vampire");
                    AddAttribute(hero, "Necromancer");
                    AddReligion(hero, "cult_of_nagash", 60);
                    break;

                case "option_3_vc_necromancer":
                case "option_3_mousillon_necromancer":
                    AddAttribute(hero, "SpellCaster");
                    AddAttribute(hero, "Necromancer");
                    AddAbility(hero, "SummonSkeleton");
                    AddLore(hero, "MinorMagic");
                    AddLore(hero, "Necromancy");
                    SetEntryCasting(hero);
                    SetSkillFloor(hero, "Spellcraft", 25);
                    AddTorPerk(hero, "Spellcraft", "EntrySpells");
                    SetCareerSafe(hero, "Necromancer");
                    AddReligion(hero, "cult_of_nagash", 25);
                    ApplyUndeadFaithTransition(hero, 25);
                    break;

                case "option_3_dw_shield_breaker":
                    SetCareerSafe(hero, "Ironbreaker");
                    break;
                case "option_3_dw_slayer":
                    SetCareerSafe(hero, "Slayer");
                    AddReligion(hero, "cult_of_grimnir", 30);
                    break;
                case "option_3_dw_rune_smith":
                    SetCareerSafe(hero, "Runelord");
                    AddAttribute(hero, "RuneCraft");
                    break;
                case "option_3_empire_witch_hunter":
                    SetCareerSafe(hero, "WitchHunter");
                    break;
                case "option_3_bretonnia_knight_errant":
                    SetCareerSafe(hero, "GrailKnight");
                    break;
                case "option_3_mousillon_knight_errant":
                    SetCareerSafe(hero, "BlackGrailKnight");
                    ApplyBlackGrailFaithTransition(hero);
                    break;
                case "option_3_we_waywatcher":
                case "option_3_eo_ghost_strider":
                    SetCareerSafe(hero, "Waywatcher");
                    break;
                case "option_3_we_warden":
                    SetCareerSafe(hero, "Warden");
                    break;
                case "option_3_empire_free_company":
                    SetCareerSafe(hero, "Mercenary");
                    break;
                case "option_3_gs_path_of_boss":
                case "option_3_gs_path_of_bully":
                case "option_3_gs_path_of_boar_boys":
                case "option_3_gs_path_of_savage_boys":
                    SetCareerSafe(hero, "OrcBoss");
                    break;
                case "option_3_gs_path_of_shaman":
                    AddAttribute(hero, "SpellCaster");
                    AddAbility(hero, "GazeUvMork");
                    AddLore(hero, "BigWaaagh");
                    SetEntryCasting(hero);
                    SetSkillFloor(hero, "Spellcraft", 25);
                    AddTorPerk(hero, "Spellcraft", "EntrySpells");
                    SetCareerSafe(hero, "OrcShaman");
                    break;
                default:
                    // Unknown future TOR option: keep narrative stat bonuses but fail
                    // loudly in diagnostics rather than silently assigning a wrong career.
                    error = "unmapped profession " + professionId;
                    return false;
            }

            if (!string.IsNullOrWhiteSpace(specializationId))
                ApplySpecializationPackage(hero, specializationId);

            return true;
        }
        catch (Exception ex)
        {
            error = ex.GetBaseException().Message;
            return false;
        }
    }

    public static bool ApplyBloodKissConversion(Hero hero, out string error)
    {
        error = string.Empty;
        if (hero?.CharacterObject == null)
        {
            error = "hero missing";
            return false;
        }
        if (!EnsureTypes())
        {
            error = "TOR reflection types unavailable";
            return false;
        }

        try
        {
            var vampireRace = FaceGen.GetRaceOrDefault("vampire");
            var humanRace = FaceGen.GetRaceOrDefault("human");
            if (vampireRace == humanRace)
            {
                error = "vampire race unavailable";
                return false;
            }

            // Match TOR's own Blood Kiss dialog semantics: biological race becomes
            // vampire and the hero receives the MinorVampire career. We write the
            // target hero's ExtendedInfo directly instead of calling AddCareer because
            // several TOR career setup implementations reference Hero.MainHero.
            hero.CharacterObject.Race = vampireRace;
            SetCareerSafe(hero, "MinorVampire");
            ApplyVampireCasterIdentity(hero, false);
            ApplyUndeadFaithTransition(hero, 25);
            return hero.CharacterObject.Race == vampireRace &&
                   string.Equals(GetCurrentCareerId(hero), GetCareerStringId("MinorVampire"), StringComparison.Ordinal);
        }
        catch (Exception ex)
        {
            error = ex.GetBaseException().Message;
            return false;
        }
    }

    private static string GetCareerStringId(string careerProperty)
    {
        var career = GetStaticMember(_torCareers, careerProperty);
        return career?.GetType().GetProperty("StringId", BindingFlags.Public | BindingFlags.Instance)?.GetValue(career) as string;
    }

    public static string GetCareerPropertyForProfession(string professionId, string specializationId = null)
    {
        if (professionId == "option_3_empire_magister_apprentice") return "ImperialMagister";
        if (professionId == "option_3_empire_knight") return "KnightOldWorld";
        if (professionId == "option_3_bretonnia_damsel") return "GrailDamsel";
        if (professionId == "option_3_we_spellsinger") return "Spellsinger";
        if (professionId == "option_3_eo_greylord_apprentice") return "GreyLord";
        if (professionId == "option_3_vc_necromancer" || professionId == "option_3_mousillon_necromancer") return "Necromancer";
        if (professionId == "option_3_dw_shield_breaker") return "Ironbreaker";
        if (professionId == "option_3_dw_slayer") return "Slayer";
        if (professionId == "option_3_dw_rune_smith") return "Runelord";
        if (professionId == "option_3_empire_witch_hunter") return "WitchHunter";
        if (professionId == "option_3_bretonnia_knight_errant") return "GrailKnight";
        if (professionId == "option_3_mousillon_knight_errant") return "BlackGrailKnight";
        if (professionId == "option_3_we_waywatcher" || professionId == "option_3_eo_ghost_strider") return "Waywatcher";
        if (professionId == "option_3_we_warden") return "Warden";
        if (professionId == "option_3_empire_free_company") return "Mercenary";
        if (professionId == "option_3_gs_path_of_shaman") return "OrcShaman";
        if (!string.IsNullOrWhiteSpace(professionId) && professionId.StartsWith("option_3_gs_path_of_", StringComparison.Ordinal)) return "OrcBoss";

        if (professionId == "option_3_empire_priest_acolyte")
            return specializationId == "priest_ulric" ? "WarriorPriestUlric" : specializationId == "priest_sigmar" ? "WarriorPriest" : null;

        if (professionId == "option_3_vc_vampire" || professionId == "option_3_mousillon_vampire")
        {
            if (specializationId != null && specializationId.Contains("blood_dragon", StringComparison.Ordinal)) return "BloodKnight";
            if (specializationId != null && specializationId.Contains("necrarch", StringComparison.Ordinal)) return "Necrarch";
            if (specializationId != null && specializationId.Contains("von_carstein", StringComparison.Ordinal)) return "MinorVampire";
        }

        return null;
    }

    public static string GetCurrentCareerId(Hero hero)
    {
        if (hero == null || !EnsureTypes()) return null;
        try
        {
            var info = GetExtendedInfo(hero);
            return info?.GetType().GetField("CareerID", BindingFlags.Public | BindingFlags.Instance)?.GetValue(info) as string;
        }
        catch { return null; }
    }

    public static bool IsMinorVampireIdentity(Hero hero)
    {
        if (hero?.CharacterObject == null || !EnsureTypes())
            return false;

        try
        {
            var vampireRace = FaceGen.GetRaceOrDefault("vampire");
            var minorVampireId = GetCareerStringId("MinorVampire");
            var currentCareer = GetCurrentCareerId(hero);
            return hero.CharacterObject.Race == vampireRace &&
                   !string.IsNullOrWhiteSpace(minorVampireId) &&
                   string.Equals(currentCareer, minorVampireId, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static void ApplySpecializationPackage(Hero hero, string id)
    {
        switch (id)
        {
            case "priest_sigmar":
                SetCareerSafe(hero, "WarriorPriest");
                AddReligion(hero, "cult_of_sigmar", 60);
                AddAttribute(hero, "PriestSigmar");
                SetSkillFloor(hero, "Faith", 25);
                AddTorPerk(hero, "Faith", "NovicePrayers");
                break;
            case "priest_ulric":
                SetCareerSafe(hero, "WarriorPriestUlric");
                AddReligion(hero, "cult_of_ulric", 60);
                AddAttribute(hero, "PriestUlric");
                SetSkillFloor(hero, "Faith", 25);
                AddTorPerk(hero, "Faith", "NovicePrayers");
                break;

            case "bloodline_von_carstein":
            case "bloodline_von_carstein_mous":
                SetRace(hero, "vampire");
                AddAttribute(hero, "SpellCaster");
                AddAbility(hero, "NagashGaze");
                AddLore(hero, "MinorMagic");
                AddLore(hero, "Necromancy");
                SetSkillFloor(hero, "Spellcraft", 25);
                AddTorPerk(hero, "Spellcraft", "EntrySpells");
                SetCareerSafe(hero, "MinorVampire");
                ApplyVampireCasterIdentity(hero, false);
                ApplyUndeadFaithTransition(hero, 25);
                break;
            case "bloodline_blood_dragon":
            case "bloodline_blood_dragon_mous":
                SetRace(hero, "vampire");
                SetCareerSafe(hero, "BloodKnight");
                ApplyBloodKnightIdentity(hero);
                ApplyUndeadFaithTransition(hero, 25);
                break;
            case "bloodline_necrarch":
            case "bloodline_necrarch_mous":
                SetRace(hero, "necrarch");
                AddAttribute(hero, "SpellCaster");
                AddAbility(hero, "NagashGaze");
                AddLore(hero, "MinorMagic");
                AddLore(hero, "Necromancy");
                SetSkillFloor(hero, "Spellcraft", 25);
                AddTorPerk(hero, "Spellcraft", "EntrySpells");
                SetCareerSafe(hero, "Necrarch");
                ApplyVampireCasterIdentity(hero, true);
                ApplyUndeadFaithTransition(hero, 25);
                break;

            case "knight_blazing_sun": AddReligion(hero, "cult_of_myrmidia", 30); break;
            case "knight_white_wolf": AddReligion(hero, "cult_of_ulric", 30); break;
            case "knight_gryphon":
            case "knight_griphon": AddReligion(hero, "cult_of_sigmar", 30); break;

            case "lore_of_fire": AddLore(hero, "LoreOfFire"); AddAbility(hero, "CinderBlast"); break;
            case "lore_of_light": AddLore(hero, "LoreOfLight"); AddAbility(hero, "ShemsGaze"); break;
            case "lore_of_metal": AddLore(hero, "LoreOfMetal"); AddAbility(hero, "GleamingArrow"); break;
            case "lore_of_death": AddLore(hero, "LoreOfDeath"); AddAbility(hero, "AshesAndDust"); break;
            case "lore_of_beasts": AddLore(hero, "LoreOfBeasts"); AddAbility(hero, "AmberSpear"); break;
            case "lore_of_heavens": AddLore(hero, "LoreOfHeavens"); AddAbility(hero, "LightningBolt"); break;
            case "lore_of_life": AddLore(hero, "LoreOfLife"); AddAbility(hero, "DrainLife"); break;
            // knight_panthers / knight_reiksguard have only spawn effects in TOR CC.
        }
    }

    private static void ApplyVampireCasterIdentity(Hero hero, bool necrarch)
    {
        SetRace(hero, necrarch ? "necrarch" : "vampire");
        AddAttribute(hero, "Necromancer");
        AddAttribute(hero, "SpellCaster");
        SetSkillFloor(hero, "Spellcraft", 25);
        AddLore(hero, "Necromancy");
        AddAbility(hero, "SummonSkeleton");
        AddLore(hero, "MinorMagic");
        AddAbility(hero, "Dart");
    }

    private static void ApplyBloodKnightIdentity(Hero hero)
    {
        SetRace(hero, "vampire");
        AddAttribute(hero, "Necromancer");
        RemoveAttribute(hero, "SpellCaster");
        SetSkillExact(hero, "Spellcraft", 0);
        RemoveAllKnownLores(hero);
        RemoveAllSpells(hero);
    }

    private static void ApplyUndeadFaithTransition(Hero hero, int nagashBonus)
    {
        foreach (var religion in EnumerateReligions())
        {
            var pantheon = religion?.GetType().GetProperty("Pantheon", BindingFlags.Public | BindingFlags.Instance)?.GetValue(religion);
            if (pantheon != null && string.Equals(pantheon.ToString(), "Human", StringComparison.OrdinalIgnoreCase))
                InvokeReligiousInfluence(hero, religion, -100);
        }

        if (nagashBonus != 0)
            AddReligion(hero, "cult_of_nagash", nagashBonus);
    }

    private static void ApplyBlackGrailFaithTransition(Hero hero)
    {
        AddReligion(hero, "cult_of_lady", -100);
        AddReligion(hero, "cult_of_nagash", 25);
    }

    private static IEnumerable EnumerateReligions()
    {
        var all = _religionObject.GetProperty("All", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as IEnumerable;
        return all ?? Array.Empty<object>();
    }

    private static void InvokeReligiousInfluence(Hero hero, object religion, int amount)
    {
        if (hero == null || religion == null || amount == 0) return;
        var method = _heroExtensions.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m => m.Name == "AddReligiousInfluence" && m.GetParameters().Length == 4 && m.GetParameters()[0].ParameterType == typeof(Hero));
        if (method == null) throw new MissingMethodException("HeroExtensions.AddReligiousInfluence");
        method.Invoke(null, new[] { (object)hero, religion, amount, false });
    }

    private static void RemoveAllKnownLores(Hero hero)
    {
        var info = GetExtendedInfo(hero);
        if (info == null) return;

        var loreType = _torAssembly.GetType("TOR_Core.AbilitySystem.Spells.LoreObject");
        var getAll = loreType?.GetMethod("GetAll", BindingFlags.Public | BindingFlags.Static);
        var all = getAll?.Invoke(null, null) as IEnumerable;
        var remove = info.GetType().GetMethod("RemoveKnownLore", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string) }, null);
        if (all == null || remove == null) return;

        foreach (var lore in all)
        {
            var id = lore?.GetType().GetProperty("StringId", BindingFlags.Public | BindingFlags.Instance)?.GetValue(lore) as string;
            if (!string.IsNullOrWhiteSpace(id))
                remove.Invoke(info, new object[] { id });
        }
    }

    private static void RemoveAllSpells(Hero hero)
    {
        var info = GetExtendedInfo(hero);
        info?.GetType().GetMethod("RemoveAllSpells", BindingFlags.Public | BindingFlags.Instance)?.Invoke(info, null);
    }

    private static void SetSkillExact(Hero hero, string property, int value)
    {
        var skill = GetStaticMember(_torSkills, property) as SkillObject;
        if (skill == null) throw new InvalidOperationException("TOR skill missing: " + property);
        hero.HeroDeveloper.SetInitialSkillLevel(skill, Math.Max(0, value));
    }

    private static void SetCareerSafe(Hero hero, string careerProperty)
    {
        var career = GetStaticMember(_torCareers, careerProperty);
        if (career == null) throw new InvalidOperationException("TOR career missing: " + careerProperty);

        var info = GetExtendedInfo(hero) ?? throw new InvalidOperationException("TOR ExtendedInfo missing for " + hero.StringId);
        var careerId = career.GetType().GetProperty("StringId", BindingFlags.Public | BindingFlags.Instance)?.GetValue(career) as string;
        if (string.IsNullOrWhiteSpace(careerId)) throw new InvalidOperationException("Career StringId missing: " + careerProperty);

        var infoType = info.GetType();
        var careerField = infoType.GetField("CareerID", BindingFlags.Public | BindingFlags.Instance);
        var choicesField = infoType.GetField("CareerChoices", BindingFlags.Public | BindingFlags.Instance);
        careerField?.SetValue(info, careerId);

        if (choicesField?.GetValue(info) is IList choices)
        {
            choices.Clear();
            var root = career.GetType().GetProperty("RootNode", BindingFlags.Public | BindingFlags.Instance)?.GetValue(career);
            var rootId = root?.GetType().GetProperty("StringId", BindingFlags.Public | BindingFlags.Instance)?.GetValue(root) as string;
            if (!string.IsNullOrWhiteSpace(rootId)) choices.Add(rootId);
        }
    }

    private static void AddAttribute(Hero hero, string value) => InvokeHeroExtension("AddAttribute", hero, value);
    private static void RemoveAttribute(Hero hero, string value) => InvokeHeroExtension("RemoveAttribute", hero, value);
    private static void AddAbility(Hero hero, string value) => InvokeHeroExtension("AddAbility", hero, value);
    private static void AddLore(Hero hero, string value) => InvokeHeroExtension("AddKnownLore", hero, value);

    private static void SetEntryCasting(Hero hero)
    {
        var entry = Enum.Parse(_spellCastingLevel, "Entry");
        InvokeHeroExtension("SetSpellCastingLevel", hero, entry);
    }

    private static void SetRace(Hero hero, string raceId)
    {
        if (hero?.CharacterObject == null || string.IsNullOrWhiteSpace(raceId)) return;
        hero.CharacterObject.Race = FaceGen.GetRaceOrDefault(raceId);
    }

    private static void SetSkillFloor(Hero hero, string property, int floor)
    {
        var skill = GetStaticMember(_torSkills, property) as SkillObject;
        if (skill == null) throw new InvalidOperationException("TOR skill missing: " + property);
        var current = hero.GetSkillValue(skill);
        hero.HeroDeveloper.SetInitialSkillLevel(skill, Math.Max(current, floor));
    }

    private static void AddTorPerk(Hero hero, string nestedTypeName, string perkName)
    {
        var nested = _torAssembly.GetType("TOR_Core.CharacterDevelopment.TORPerks+" + nestedTypeName);
        var perk = GetStaticMember(nested, perkName) as PerkObject;
        if (perk == null) throw new InvalidOperationException($"TOR perk missing: {nestedTypeName}.{perkName}");
        if (!hero.GetPerkValue(perk)) hero.HeroDeveloper.AddPerk(perk);
    }

    private static void AddReligion(Hero hero, string religionId, int amount)
    {
        object religion = null;
        foreach (var item in EnumerateReligions())
        {
            var id = item?.GetType().GetProperty("StringId", BindingFlags.Public | BindingFlags.Instance)?.GetValue(item) as string;
            if (string.Equals(id, religionId, StringComparison.Ordinal)) { religion = item; break; }
        }
        if (religion == null) throw new InvalidOperationException("Religion missing: " + religionId);
        InvokeReligiousInfluence(hero, religion, amount);
    }

    private static object GetExtendedInfo(Hero hero)
    {
        var method = _heroExtensions.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m => m.Name == "GetExtendedInfo" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(Hero));
        return method?.Invoke(null, new object[] { hero });
    }

    private static void InvokeHeroExtension(string name, Hero hero, object value)
    {
        var method = _heroExtensions.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m => m.Name == name && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == typeof(Hero));
        if (method == null) throw new MissingMethodException("HeroExtensions." + name);
        method.Invoke(null, new[] { (object)hero, value });
    }

    private static object GetStaticMember(Type type, string name)
    {
        if (type == null || string.IsNullOrWhiteSpace(name)) return null;
        return type.GetProperty(name, BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
            ?? type.GetField(name, BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
    }

    private static bool EnsureTypes()
    {
        if (_torAssembly != null && _heroExtensions != null && _torCareers != null) return true;
        _torAssembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "TOR_Core");
        if (_torAssembly == null) return false;
        _heroExtensions = _torAssembly.GetType("TOR_Core.Extensions.HeroExtensions");
        _torCareers = _torAssembly.GetType("TOR_Core.CharacterDevelopment.TORCareers");
        _torSkills = _torAssembly.GetType("TOR_Core.CharacterDevelopment.TORSkills");
        _torPerks = _torAssembly.GetType("TOR_Core.CharacterDevelopment.TORPerks");
        _spellCastingLevel = _torAssembly.GetType("TOR_Core.AbilitySystem.Spells.SpellCastingLevel");
        _religionObject = _torAssembly.GetType("TOR_Core.CampaignMechanics.Religion.ReligionObject");
        return _heroExtensions != null && _torCareers != null && _torSkills != null && _torPerks != null && _spellCastingLevel != null && _religionObject != null;
    }
}
