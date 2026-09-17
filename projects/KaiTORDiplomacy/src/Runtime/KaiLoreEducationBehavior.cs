using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.ModuleManager;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Adds TOR's three narrative character-creation choices to player-dynasty children without
/// replacing Bannerlord's normal education stages. Exact skill/attribute bonuses are loaded
/// from TOR_Core's live tor_cc_options.xml at runtime.
/// </summary>
public sealed class KaiLoreEducationBehavior : CampaignBehaviorBase
{
    private const int OriginAge = 8;
    private const int GrowthAge = 14;
    private const int ProfessionAge = 16;

    private Dictionary<string, int> _completedMasks = new();
    private Dictionary<string, string> _professionChoices = new();
    private List<LoreOption> _options = new();
    private bool _loaded;
    private bool _inquiryOpen;

    public override void RegisterEvents()
    {
        CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        CampaignEvents.ChildEducationCompletedEvent.AddNonSerializedListener(this, OnChildEducationCompleted);
        CampaignEvents.HeroComesOfAgeEvent.AddNonSerializedListener(this, OnHeroComesOfAge);
        CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
    }

    public override void SyncData(IDataStore dataStore)
    {
        dataStore.SyncData("kaitor_lore_education_completed", ref _completedMasks);
        dataStore.SyncData("kaitor_lore_education_profession", ref _professionChoices);
        _completedMasks ??= new Dictionary<string, int>();
        _professionChoices ??= new Dictionary<string, string>();
    }

    private void OnSessionLaunched(CampaignGameStarter starter)
    {
        LoadTorOptions();
    }

    private void OnChildEducationCompleted(Hero child, int age)
    {
        if (!IsRelevantChild(child)) return;
        if (age == OriginAge) TryOfferStage(child, 1);
        else if (age == GrowthAge) TryOfferStage(child, 2);
        else if (age == ProfessionAge) TryOfferStage(child, 3);
    }

    private void OnHeroComesOfAge(Hero hero)
    {
        if (hero == null || hero.Clan != Clan.PlayerClan) return;
        TryAssignCareer(hero);
    }

    private void OnDailyTick()
    {
        if (_inquiryOpen || !_loaded || Clan.PlayerClan == null) return;

        // Old-save catch-up: offer only one missing lore step per day and never replace native education.
        foreach (var child in Clan.PlayerClan.Heroes.Where(IsRelevantChild).OrderBy(h => h.Age))
        {
            if (child.Age >= OriginAge && !IsStageDone(child, 1)) { TryOfferStage(child, 1); return; }
            if (child.Age >= GrowthAge && !IsStageDone(child, 2)) { TryOfferStage(child, 2); return; }
            if (child.Age >= ProfessionAge && !IsStageDone(child, 3)) { TryOfferStage(child, 3); return; }
        }

        foreach (var adult in Clan.PlayerClan.Heroes.Where(h => h != null && h.IsAlive && h.Age >= Campaign.Current.Models.AgeModel.HeroComesOfAge))
            TryAssignCareer(adult);
    }

    private bool IsRelevantChild(Hero hero)
    {
        if (hero == null || !hero.IsAlive || hero == Hero.MainHero || hero.Clan != Clan.PlayerClan) return false;
        return hero.Age < Campaign.Current.Models.AgeModel.HeroComesOfAge;
    }

    private void TryOfferStage(Hero child, int stage)
    {
        if (_inquiryOpen || child == null || IsStageDone(child, stage)) return;
        if (!_loaded) LoadTorOptions();
        if (!_loaded) return;

        var choices = _options
            .Where(x => x.Stage == stage && string.Equals(x.Culture, child.Culture?.StringId, StringComparison.OrdinalIgnoreCase))
            .Where(x => IsOptionAllowedForHero(x.Id, child))
            .OrderBy(x => Localize(x.OptionText), StringComparer.CurrentCulture)
            .ToList();

        // TOR does not define narrative choices for every non-playable culture. Do not block aging in that case.
        if (choices.Count == 0)
        {
            MarkStageDone(child, stage);
            return;
        }

        var inquiryOptions = choices.Select(x =>
        {
            var name = Localize(x.OptionText);
            var effect = Localize(x.PositiveEffectText);
            var title = string.IsNullOrWhiteSpace(effect) ? name : $"{name} — {effect}";
            return new InquiryElement(x.Id, title, null, true, Localize(x.FlavourText));
        }).ToList();

        var heading = stage switch
        {
            1 => "Лорное становление: происхождение",
            2 => "Лорное становление: путь взросления",
            _ => "Лорное становление: будущая профессия"
        };

        var intro = stage switch
        {
            1 => $"{child.Name} достиг возраста, когда происхождение и традиции рода начинают определять дальнейший путь. Этот выбор дополняет обычное воспитание и не заменяет его.",
            2 => $"Пришло время определить, чему посвятит юность {child.Name}. Выбор даст те же навыки и склонности, что соответствующий этап создания героя в The Old Realms.",
            _ => $"{child.Name} должен выбрать будущую профессию. В совершеннолетие этот путь станет основой настоящей карьеры The Old Realms, если такая карьера предусмотрена модом."
        };

        _inquiryOpen = true;
        MBInformationManager.ShowMultiSelectionInquiry(
            new MultiSelectionInquiryData(
                heading,
                intro,
                inquiryOptions,
                true,
                1,
                1,
                "Выбрать",
                "Позже",
                selected =>
                {
                    _inquiryOpen = false;
                    if (selected.Count == 0 || selected[0].Identifier is not string id) return;
                    var option = choices.FirstOrDefault(x => x.Id == id);
                    if (option == null) return;
                    ApplyNarrativeBonus(child, option);
                    MarkStageDone(child, stage);
                    if (stage == 3) _professionChoices[child.StringId] = option.Id;
                    InformationManager.DisplayMessage(new InformationMessage($"Путь {child.Name}: {Localize(option.OptionText)}."));
                },
                _ => _inquiryOpen = false),
            true,
            true);
    }

    private static bool IsOptionAllowedForHero(string optionId, Hero hero)
    {
        if (string.Equals(optionId, "option_3_bretonnia_damsel", StringComparison.OrdinalIgnoreCase))
            return hero.IsFemale;
        if (string.Equals(optionId, "option_3_bretonnia_knight_errant", StringComparison.OrdinalIgnoreCase))
            return !hero.IsFemale;
        return true;
    }

    private static void ApplyNarrativeBonus(Hero hero, LoreOption option)
    {
        foreach (var skillId in option.Skills)
        {
            var skill = Skills.All.FirstOrDefault(x => string.Equals(x.StringId, skillId, StringComparison.OrdinalIgnoreCase));
            if (skill == null) continue;
            hero.HeroDeveloper.AddFocus(skill, 1, false);
            hero.HeroDeveloper.ChangeSkillLevel(skill, 10, false);
        }

        if (!string.IsNullOrWhiteSpace(option.Attribute))
        {
            var attribute = Attributes.All.FirstOrDefault(x => string.Equals(x.StringId, option.Attribute, StringComparison.OrdinalIgnoreCase));
            if (attribute != null) hero.HeroDeveloper.AddAttribute(attribute, 1, false);
        }
    }

    private void TryAssignCareer(Hero hero)
    {
        if (hero == null || !_professionChoices.TryGetValue(hero.StringId, out var professionId)) return;
        if (!TryGetCareerPropertyName(professionId, out var careerProperty)) return;

        try
        {
            var torAssembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "TOR_Core");
            if (torAssembly == null) return;

            var extensions = torAssembly.GetType("TOR_Core.Extensions.HeroExtensions");
            var careers = torAssembly.GetType("TOR_Core.CharacterDevelopment.TORCareers");
            if (extensions == null || careers == null) return;

            var hasAnyCareer = extensions.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "HasAnyCareer" && m.GetParameters().Length == 1);
            if (hasAnyCareer != null && hasAnyCareer.Invoke(null, new object[] { hero }) is bool hasCareer && hasCareer)
                return;

            var career = careers.GetProperty(careerProperty, BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (career == null) return;

            var addCareer = extensions.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "AddCareer" && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == typeof(Hero));
            if (addCareer == null) return;

            addCareer.Invoke(null, new[] { (object)hero, career });
            ApplyEssentialCareerFlags(extensions, hero, professionId);
            InformationManager.DisplayMessage(new InformationMessage($"{hero.Name} вступает на выбранный профессиональный путь."));
        }
        catch
        {
            // TOR career APIs are optional integration points. Never block adulthood or save loading.
        }
    }

    private static void ApplyEssentialCareerFlags(Type extensions, Hero hero, string professionId)
    {
        // TOR character creation gives these professions core extended-info flags in addition to the CareerObject.
        // We mirror only the essential role flag here; lore/deity/bloodline specialization remains TOR-owned.
        string attribute = null;
        if (professionId.Contains("magister") || professionId.Contains("spellsinger") || professionId.Contains("greylord") || professionId.Contains("shaman"))
            attribute = "SpellCaster";
        else if (professionId.Contains("necromancer"))
            attribute = "Necromancer";
        else if (professionId.Contains("priest"))
            attribute = "Priest";
        else if (professionId.Contains("vampire"))
            attribute = "Vampire";
        else if (professionId.Contains("rune_smith"))
            attribute = "RuneCraft";

        if (attribute == null) return;
        var addAttribute = extensions.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m => m.Name == "AddAttribute" && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == typeof(Hero));
        addAttribute?.Invoke(null, new object[] { hero, attribute });
    }

    private static bool TryGetCareerPropertyName(string id, out string career)
    {
        career = null;
        if (string.IsNullOrWhiteSpace(id)) return false;

        if (id == "option_3_empire_free_company") career = "Mercenary";
        else if (id == "option_3_empire_magister_apprentice") career = "ImperialMagister";
        else if (id == "option_3_empire_priest_acolyte") career = "WarriorPriest";
        else if (id == "option_3_empire_witch_hunter") career = "WitchHunter";
        else if (id == "option_3_empire_knight") career = "KnightOldWorld";
        else if (id == "option_3_bretonnia_knight_errant") career = "GrailKnight";
        else if (id == "option_3_bretonnia_damsel") career = "GrailDamsel";
        else if (id.Contains("mousillon") && id.Contains("knight")) career = "BlackGrailKnight";
        else if ((id.Contains("vc_") || id.Contains("mousillon")) && id.Contains("vampire")) career = "MinorVampire";
        else if ((id.Contains("vc_") || id.Contains("mousillon")) && id.Contains("necromancer")) career = "Necromancer";
        else if (id.Contains("we_spellsinger")) career = "Spellsinger";
        else if (id.Contains("we_waywatcher")) career = "Waywatcher";
        else if (id.Contains("we_warden")) career = "Warden";
        else if (id.Contains("eo_greylord")) career = "GreyLord";
        else if (id.Contains("dw_shield_breaker")) career = "Ironbreaker";
        else if (id.Contains("dw_slayer")) career = "Slayer";
        else if (id.Contains("dw_rune_smith")) career = "Runelord";
        else if (id.Contains("gs_path_of_shaman")) career = "OrcShaman";
        else if (id.Contains("gs_path_of_")) career = "OrcBoss";

        return career != null;
    }

    private void LoadTorOptions()
    {
        if (_loaded) return;
        try
        {
            var root = ModuleHelper.GetModuleFullPath("TOR_Core");
            var path = Path.Combine(root, "ModuleData", "tor_custom_xmls", "tor_cc_options.xml");
            var document = XDocument.Load(path);
            _options = document.Descendants("CharacterCreationOption")
                .Select(x => new LoreOption
                {
                    Id = (string)x.Attribute("Id") ?? string.Empty,
                    Culture = (string)x.Attribute("Culture") ?? string.Empty,
                    Stage = (int?)x.Attribute("StageNumber") ?? 0,
                    Skills = x.Element("SkillsToIncrease")?.Elements("string").Select(e => e.Value.Trim()).Where(v => v.Length > 0).ToList() ?? new List<string>(),
                    Attribute = x.Element("AttributeToIncrease")?.Value?.Trim(),
                    OptionText = x.Element("OptionText")?.Value ?? string.Empty,
                    FlavourText = x.Element("OptionFlavourText")?.Value ?? string.Empty,
                    PositiveEffectText = x.Element("PositiveEffectText")?.Value ?? string.Empty
                })
                .Where(x => x.Stage >= 1 && x.Stage <= 3 && x.Id.Length > 0 && x.Culture.Length > 0)
                .ToList();
            _loaded = _options.Count > 0;
        }
        catch
        {
            _loaded = false;
            _options = new List<LoreOption>();
        }
    }

    private bool IsStageDone(Hero hero, int stage)
    {
        _completedMasks.TryGetValue(hero.StringId, out var mask);
        return (mask & (1 << (stage - 1))) != 0;
    }

    private void MarkStageDone(Hero hero, int stage)
    {
        _completedMasks.TryGetValue(hero.StringId, out var mask);
        _completedMasks[hero.StringId] = mask | (1 << (stage - 1));
    }

    private static string Localize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        try { return new TextObject(raw).ToString(); }
        catch { return raw; }
    }

    private sealed class LoreOption
    {
        public string Id { get; set; }
        public string Culture { get; set; }
        public int Stage { get; set; }
        public List<string> Skills { get; set; }
        public string Attribute { get; set; }
        public string OptionText { get; set; }
        public string FlavourText { get; set; }
        public string PositiveEffectText { get; set; }
    }
}
