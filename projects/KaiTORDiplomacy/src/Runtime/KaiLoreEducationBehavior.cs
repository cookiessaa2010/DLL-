using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ModuleManager;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Additive TOR dynasty education. Bannerlord owns childhood/coming-of-age; KaiTOR adds
/// TOR narrative choices at 8/14/16, TOR specialization, and a safe profession package
/// on adulthood. Player-clan children get interactive choices. AI noble children make
/// deterministic weighted choices with no popups.
/// </summary>
public sealed class KaiLoreEducationBehavior : CampaignBehaviorBase
{
    private const int OriginAge = 8;
    private const int GrowthAge = 14;
    private const int ProfessionAge = 16;

    private const string CompletedKey = "kaitor_lore_education_completed";
    private const string ProfessionKey = "kaitor_lore_education_profession";
    private const string StageChoicesKey = "kaitor_lore_education_stage_choices_v1";
    private const string SpecializationKey = "kaitor_lore_education_specialization_v1";
    private const string CareerAppliedKey = "kaitor_lore_education_career_applied_v1";

    private Dictionary<string, int> _completedMasks = new();
    private Dictionary<string, string> _professionChoices = new();
    private Dictionary<string, string> _stageChoices = new();
    private Dictionary<string, string> _specializationChoices = new();
    private Dictionary<string, bool> _careerApplied = new();

    private List<LoreOption> _options = new();
    private List<SpecializationOption> _specializations = new();
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
        dataStore.SyncData(CompletedKey, ref _completedMasks);
        dataStore.SyncData(ProfessionKey, ref _professionChoices);
        dataStore.SyncData(StageChoicesKey, ref _stageChoices);
        dataStore.SyncData(SpecializationKey, ref _specializationChoices);
        dataStore.SyncData(CareerAppliedKey, ref _careerApplied);

        _completedMasks ??= new Dictionary<string, int>();
        _professionChoices ??= new Dictionary<string, string>();
        _stageChoices ??= new Dictionary<string, string>();
        _specializationChoices ??= new Dictionary<string, string>();
        _careerApplied ??= new Dictionary<string, bool>();
    }

    private void OnSessionLaunched(CampaignGameStarter starter)
    {
        LoadTorOptions();
    }

    private void OnChildEducationCompleted(Hero child, int age)
    {
        if (!IsPlayerEducationChild(child)) return;
        if (age == OriginAge) TryOfferPlayerStage(child, 1);
        else if (age == GrowthAge) TryOfferPlayerStage(child, 2);
        else if (age == ProfessionAge) TryOfferPlayerStage(child, 3);
    }

    private void OnHeroComesOfAge(Hero hero)
    {
        if (!IsEligibleDynastyHero(hero)) return;
        TryAssignCareer(hero);
    }

    private void OnDailyTick()
    {
        if (!_loaded) LoadTorOptions();
        if (!_loaded || Campaign.Current == null) return;

        // Player UI is serialized one popup at a time. Specialization is deliberately
        // offered on a later tick after profession selection, never Inquiry->Inquiry.
        if (!_inquiryOpen && Clan.PlayerClan != null)
        {
            foreach (var child in Clan.PlayerClan.Heroes.Where(IsPlayerEducationChild).OrderBy(h => h.Age).ThenBy(h => h.StringId))
            {
                if (TryOfferPendingSpecialization(child)) return;
                if (child.Age >= OriginAge && !IsStageDone(child, 1)) { TryOfferPlayerStage(child, 1); return; }
                if (child.Age >= GrowthAge && !IsStageDone(child, 2)) { TryOfferPlayerStage(child, 2); return; }
                if (child.Age >= ProfessionAge && !IsStageDone(child, 3)) { TryOfferPlayerStage(child, 3); return; }
            }
        }

        // AI children never open UI and may process all overdue stages in one daily tick.
        foreach (var child in Hero.AllAliveHeroes
                     .Where(IsAiEducationChild)
                     .OrderBy(h => h.Clan?.StringId, StringComparer.Ordinal)
                     .ThenBy(h => h.StringId, StringComparer.Ordinal))
        {
            if (child.Age >= OriginAge && !IsStageDone(child, 1)) ProcessAiStage(child, 1);
            if (child.Age >= GrowthAge && !IsStageDone(child, 2)) ProcessAiStage(child, 2);
            if (child.Age >= ProfessionAge && !IsStageDone(child, 3)) ProcessAiStage(child, 3);
        }

        // Repair/finish profession packages for adults, including v0.6.3.3 children
        // that already have a career but never received the complete TOR package.
        foreach (var adult in Hero.AllAliveHeroes
                     .Where(IsEligibleDynastyHero)
                     .Where(h => h.Age >= Campaign.Current.Models.AgeModel.HeroComesOfAge)
                     .Where(h => _professionChoices.ContainsKey(h.StringId))
                     .OrderBy(h => h.StringId, StringComparer.Ordinal))
            TryAssignCareer(adult);
    }

    private bool IsPlayerEducationChild(Hero hero)
    {
        if (hero == null || !hero.IsAlive || !hero.IsActive || hero == Hero.MainHero || hero.Clan != Clan.PlayerClan) return false;
        if (hero.IsTemplate || hero.IsMinorFactionHero) return false;
        return hero.Age < Campaign.Current.Models.AgeModel.HeroComesOfAge;
    }

    private bool IsAiEducationChild(Hero hero)
    {
        if (hero == null || !hero.IsAlive || !hero.IsActive || hero == Hero.MainHero || hero.Clan == null || hero.Clan == Clan.PlayerClan) return false;
        if (hero.IsTemplate || hero.IsMinorFactionHero || hero.Clan.IsEliminated || hero.Clan.IsMinorFaction || hero.Clan.IsBanditFaction || hero.Clan.IsClanTypeMercenary) return false;
        if (hero.Age >= Campaign.Current.Models.AgeModel.HeroComesOfAge) return false;
        return _options.Any(x => string.Equals(x.Culture, hero.Culture?.StringId, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsEligibleDynastyHero(Hero hero)
    {
        if (hero == null || !hero.IsAlive || !hero.IsActive || hero == Hero.MainHero || hero.Clan == null) return false;
        if (hero.IsTemplate || hero.IsMinorFactionHero || hero.Clan.IsEliminated || hero.Clan.IsMinorFaction || hero.Clan.IsBanditFaction) return false;
        return true;
    }

    private void TryOfferPlayerStage(Hero child, int stage)
    {
        if (_inquiryOpen || child == null || IsStageDone(child, stage)) return;
        var choices = GetChoices(child, stage).ToList();
        if (choices.Count == 0)
        {
            MarkStageDone(child, stage);
            KaiRuntimeLog.Write("CHILD_STAGE", $"hero={child.StringId}; stage={stage}; result=no_options");
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
            1 => $"{child.Name} достиг возраста, когда происхождение и традиции рода начинают определять дальнейший путь. Этот выбор дополняет обычное воспитание Bannerlord.",
            2 => $"Пришло время определить, чему посвятит юность {child.Name}. Выбор использует данные The Old Realms.",
            _ => $"{child.Name} должен выбрать будущую профессию The Old Realms. Если у профессии есть специализация, она будет предложена следующим отдельным окном."
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
                    CommitStageChoice(child, stage, option, false);
                    InformationManager.DisplayMessage(new InformationMessage($"Путь {child.Name}: {Localize(option.OptionText)}."));
                },
                _ => _inquiryOpen = false),
            true,
            true);
    }

    private bool TryOfferPendingSpecialization(Hero child)
    {
        if (_inquiryOpen || child == null || !IsStageDone(child, 3)) return false;
        if (!_professionChoices.TryGetValue(child.StringId, out var professionId)) return false;
        var options = GetSpecializations(professionId).ToList();
        if (options.Count == 0 || _specializationChoices.ContainsKey(child.StringId)) return false;

        var elements = options.Select(x => new InquiryElement(
            x.Id,
            Localize(x.Name),
            null,
            true,
            Localize(x.Description))).ToList();

        _inquiryOpen = true;
        MBInformationManager.ShowMultiSelectionInquiry(
            new MultiSelectionInquiryData(
                "Профессиональная специализация",
                $"Выберите специализацию для {child.Name}. Применяются личные эффекты TOR; телепорты, смена королевства и другие стартовые эффекты создания персонажа не используются.",
                elements,
                true,
                1,
                1,
                "Выбрать",
                "Позже",
                selected =>
                {
                    _inquiryOpen = false;
                    if (selected.Count == 0 || selected[0].Identifier is not string id) return;
                    var spec = options.FirstOrDefault(x => x.Id == id);
                    if (spec == null) return;
                    ApplySpecializationStats(child, professionId, spec);
                    _specializationChoices[child.StringId] = spec.Id;
                    KaiRuntimeLog.Write("CHILD_SPECIALIZATION", $"hero={child.StringId}; profession={professionId}; specialization={spec.Id}; ai=false");
                },
                _ => _inquiryOpen = false),
            true,
            true);
        return true;
    }

    private void ProcessAiStage(Hero child, int stage)
    {
        var choices = GetChoices(child, stage).ToList();
        if (choices.Count == 0)
        {
            MarkStageDone(child, stage);
            return;
        }

        var option = choices
            .OrderByDescending(x => GetAiChoiceScore(child, stage, x))
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .First();
        CommitStageChoice(child, stage, option, true);

        if (stage == 3)
        {
            var specs = GetSpecializations(option.Id).ToList();
            if (specs.Count > 0)
            {
                var spec = specs
                    .OrderByDescending(x => StableScore(child.StringId + "|spec|" + x.Id))
                    .ThenBy(x => x.Id, StringComparer.Ordinal)
                    .First();
                ApplySpecializationStats(child, option.Id, spec);
                _specializationChoices[child.StringId] = spec.Id;
                KaiRuntimeLog.Write("CHILD_SPECIALIZATION", $"hero={child.StringId}; profession={option.Id}; specialization={spec.Id}; ai=true");
            }
        }
    }

    private void CommitStageChoice(Hero child, int stage, LoreOption option, bool ai)
    {
        try
        {
            ApplyNarrativeBonus(child, option);
            _stageChoices[StageKey(child, stage)] = option.Id;

            if (stage == 2 && !TorProfessionEffectBridge.ApplyStage2Effect(child, option.Id, out var stageError))
                KaiRuntimeLog.Write("CAREER_EFFECT_FAIL", $"hero={child.StringId}; stage=2; option={option.Id}; error={stageError}");

            if (stage == 3)
                _professionChoices[child.StringId] = option.Id;

            MarkStageDone(child, stage);
            KaiRuntimeLog.Write("CHILD_STAGE", $"hero={child.StringId}; stage={stage}; option={option.Id}; ai={ai}");
        }
        catch (Exception ex)
        {
            KaiRuntimeLog.Exception("CAREER_EFFECT_FAIL", ex, $"hero={child?.StringId ?? "null"}; stage={stage}; option={option?.Id ?? "null"}");
        }
    }

    private IEnumerable<LoreOption> GetChoices(Hero hero, int stage)
        => _options
            .Where(x => x.Stage == stage && string.Equals(x.Culture, hero.Culture?.StringId, StringComparison.OrdinalIgnoreCase))
            .Where(x => IsOptionAllowedForHero(x.Id, hero));

    private IEnumerable<SpecializationOption> GetSpecializations(string professionId)
        => _specializations.Where(x => string.Equals(x.ProfessionRequirement, professionId, StringComparison.OrdinalIgnoreCase));

    private static bool IsOptionAllowedForHero(string optionId, Hero hero)
    {
        if (string.Equals(optionId, "option_3_bretonnia_damsel", StringComparison.OrdinalIgnoreCase)) return hero.IsFemale;
        if (string.Equals(optionId, "option_3_bretonnia_knight_errant", StringComparison.OrdinalIgnoreCase)) return !hero.IsFemale;
        return true;
    }

    private static void ApplyNarrativeBonus(Hero hero, LoreOption option)
    {
        foreach (var skillId in option.Skills.Where(x => !string.IsNullOrWhiteSpace(x) && !x.StartsWith("-", StringComparison.Ordinal)))
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

    private static void ApplySpecializationStats(Hero hero, string professionId, SpecializationOption spec)
    {
        if (hero == null || spec == null) return;

        var skillChanges = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in spec.Skills.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            var decrease = raw.StartsWith("-", StringComparison.Ordinal);
            var id = decrease ? raw.Substring(1) : raw;
            skillChanges.TryGetValue(id, out var current);
            skillChanges[id] = current + (decrease ? -1 : 1);
        }

        foreach (var pair in skillChanges)
        {
            if (pair.Value == 0) continue;
            var skill = Skills.All.FirstOrDefault(x => string.Equals(x.StringId, pair.Key, StringComparison.OrdinalIgnoreCase));
            if (skill == null) continue;

            var focus = hero.HeroDeveloper.GetFocus(skill);
            var focusDelta = pair.Value;
            if (focusDelta < 0) focusDelta = -Math.Min(focus, -focusDelta);
            if (focusDelta != 0) hero.HeroDeveloper.AddFocus(skill, focusDelta, false);

            var currentSkill = hero.GetSkillValue(skill);
            if (pair.Value > 0)
                hero.HeroDeveloper.ChangeSkillLevel(skill, pair.Value * 10, false);
            else
                hero.HeroDeveloper.SetInitialSkillLevel(skill, Math.Max(0, currentSkill + pair.Value * 10));
        }

        var professionAttribute = string.Empty;
        // This mirrors TOR's duplicate-positive-attribute protection: if the profession
        // stage already raised the same attribute, specialization doesn't add it twice.
        foreach (var raw in spec.Attributes.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            var decrease = raw.StartsWith("-", StringComparison.Ordinal);
            var id = decrease ? raw.Substring(1) : raw;
            if (!decrease && string.Equals(id, professionAttribute, StringComparison.OrdinalIgnoreCase)) continue;
            var attribute = Attributes.All.FirstOrDefault(x => string.Equals(x.StringId, id, StringComparison.OrdinalIgnoreCase));
            if (attribute != null) hero.HeroDeveloper.AddAttribute(attribute, decrease ? -1 : 1, false);
        }
    }

    private int GetAiChoiceScore(Hero child, int stage, LoreOption option)
    {
        var score = StableScore(child.StringId + "|" + stage + "|" + option.Id);
        if (stage != 3) return score;

        var candidateCareer = TorProfessionEffectBridge.GetCareerPropertyForProfession(option.Id);
        if (string.IsNullOrWhiteSpace(candidateCareer)) return score;

        foreach (var parent in new[] { child.Father, child.Mother })
        {
            var parentCareer = TorProfessionEffectBridge.GetCurrentCareerId(parent);
            if (string.Equals(parentCareer, candidateCareer, StringComparison.OrdinalIgnoreCase)) score += 10000;
        }
        return score;
    }

    private void TryAssignCareer(Hero hero)
    {
        if (hero == null || !_professionChoices.TryGetValue(hero.StringId, out var professionId)) return;
        if (_careerApplied.TryGetValue(hero.StringId, out var done) && done) return;

        var specs = GetSpecializations(professionId).ToList();
        _specializationChoices.TryGetValue(hero.StringId, out var specializationId);
        if (specs.Count > 0 && string.IsNullOrWhiteSpace(specializationId)) return;

        if (TorProfessionEffectBridge.ApplyProfessionPackage(hero, professionId, specializationId, out var error))
        {
            _careerApplied[hero.StringId] = true;
            KaiRuntimeLog.Write("CAREER_APPLY", $"hero={hero.StringId}; profession={professionId}; specialization={specializationId ?? "none"}; career={TorProfessionEffectBridge.GetCurrentCareerId(hero) ?? "none"}");
            if (hero.Clan == Clan.PlayerClan)
                InformationManager.DisplayMessage(new InformationMessage($"{hero.Name} вступает на выбранный профессиональный путь The Old Realms."));
        }
        else
        {
            KaiRuntimeLog.Write("CAREER_EFFECT_FAIL", $"hero={hero.StringId}; profession={professionId}; specialization={specializationId ?? "none"}; error={error}");
        }
    }

    private void LoadTorOptions()
    {
        if (_loaded) return;
        try
        {
            var root = ModuleHelper.GetModuleFullPath("TOR_Core");
            var optionPath = Path.Combine(root, "ModuleData", "tor_custom_xmls", "tor_cc_options.xml");
            var document = XDocument.Load(optionPath);
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

            var specPath = Path.Combine(root, "ModuleData", "tor_custom_xmls", "tor_specialization_options.xml");
            var specDocument = XDocument.Load(specPath);
            _specializations = specDocument.Descendants("SpecializationOption")
                .Select(x => new SpecializationOption
                {
                    Id = (string)x.Attribute("Id") ?? string.Empty,
                    ProfessionRequirement = (string)x.Attribute("ProfessionRequirement") ?? string.Empty,
                    RaceId = (string)x.Attribute("RaceId") ?? string.Empty,
                    Name = x.Element("Name")?.Value ?? string.Empty,
                    Description = x.Element("Description")?.Value ?? string.Empty,
                    Skills = x.Element("SkillsToIncrease")?.Elements("string").Select(e => e.Value.Trim()).Where(v => v.Length > 0).ToList() ?? new List<string>(),
                    Attributes = x.Elements("AttributeToIncrease").Select(e => e.Value.Trim()).Where(v => v.Length > 0).ToList()
                })
                .Where(x => x.Id.Length > 0 && x.ProfessionRequirement.Length > 0)
                .ToList();

            _loaded = _options.Count > 0;
            KaiRuntimeLog.Write("CHILD_EDUCATION_LOAD", $"options={_options.Count}; specializations={_specializations.Count}; bridge={TorProfessionEffectBridge.IsAvailable}");
        }
        catch (Exception ex)
        {
            _loaded = false;
            _options = new List<LoreOption>();
            _specializations = new List<SpecializationOption>();
            KaiRuntimeLog.Exception("CAREER_EFFECT_FAIL", ex, "stage=load_tor_options");
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

    private static string StageKey(Hero hero, int stage) => hero.StringId + ":" + stage;

    private static int StableScore(string value)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (var c in value ?? string.Empty)
            {
                hash ^= c;
                hash *= 16777619;
            }
            return (int)(hash & 0x7fffffff);
        }
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

    private sealed class SpecializationOption
    {
        public string Id { get; set; }
        public string ProfessionRequirement { get; set; }
        public string RaceId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public List<string> Skills { get; set; }
        public List<string> Attributes { get; set; }
    }
}
