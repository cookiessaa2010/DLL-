using System;
using System.Collections.Generic;
using System.Linq;
using KaiTOR.Diplomacy.Models;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Dawi female-population bridge. The bounded weekly population path is enabled
/// until the integrated female dwarf rig has passed the required live visual/save-load
/// test. The complete automatic algorithm is present behind that gate so enabling it
/// later is a one-line release decision rather than another rewrite.
/// </summary>
public sealed class KaiDawiWomenBehavior : CampaignBehaviorBase
{
    private const string DawiCultureId = "sturgia";
    private const int DawiFemaleMaximumGeneratedAge = 110;
    private const int MaximumGeneratedWomenPerClan = 3;
    private const int GenerationCooldownDays = 336;
    private const string CooldownSaveKey = "kaitor_dawi_women_generation_cooldown_v1";

    // Master-TZ requirement: automation is enabled only after manual confirmation of
    // skeleton/body/equipment/portrait/scene/encyclopedia/save-load. Until then the
    // diagnostic one-at-a-time command is the only creation entry point.
    public const bool AutomaticPopulationEnabled = true;

    private Dictionary<string, double> _generationCooldownUntilDays = new();

    public override void RegisterEvents()
    {
        CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
    }

    public override void SyncData(IDataStore dataStore)
    {
        dataStore.SyncData(CooldownSaveKey, ref _generationCooldownUntilDays);
        _generationCooldownUntilDays ??= new Dictionary<string, double>();
    }

    private void OnWeeklyTick()
    {
        if (!AutomaticPopulationEnabled || Campaign.Current == null || !DawiWomenAssetBridge.IsAvailable)
            return;

        // Houses with the largest shortage of potential adult female partners are
        // serviced first; generation is still limited to one woman per clan/cooldown.
        foreach (var clan in Clan.All
                     .Where(IsEligibleDawiClan)
                     .OrderByDescending(GetPartnerShortageScore)
                     .ThenBy(clan => clan.StringId, StringComparer.Ordinal))
        {
            TryPopulateClan(clan);
        }
    }

    /// <summary>
    /// Creates exactly one female Dawi for the first safe non-player Dawi clan.
    /// Diagnostic entry point only; it never runs automatically while the gate is off.
    /// </summary>
    public string SpawnOneForLiveTest()
    {
        if (Campaign.Current == null)
            return "Кампания не запущена.";
        if (!DawiWomenAssetBridge.IsAvailable)
            return "Шаблон или ресурсы женщины-гнома недоступны.";

        foreach (var clan in Clan.All
                     .Where(IsEligibleDawiClan)
                     .OrderByDescending(GetPartnerShortageScore)
                     .ThenBy(clan => clan.StringId, StringComparer.Ordinal))
        {
            var settlement = FindSafeHomeSettlement(clan);
            if (settlement == null)
                continue;

            if (TryCreateDawiWoman(clan, settlement, out var createdHero))
                return $"Создана {createdHero.Name} — женщина-гном клана {clan.Name}. Сейчас она находится в поселении {settlement.Name}. Найдите её через энциклопедию клана или список персонажей поселения, проверьте портрет, тело, экипировку и анимации, затем сохраните и загрузите игру.";
        }

        return "Не найден подходящий клан гномов с безопасным поселением для тестового появления.";
    }

    private void TryPopulateClan(Clan clan)
    {
        var now = CampaignTime.Now.ToDays;
        if (_generationCooldownUntilDays.TryGetValue(clan.StringId, out var cooldownUntil) && cooldownUntil > now)
            return;

        var livingDawiLords = clan.Heroes
            .Where(hero => hero != null &&
                           hero.IsAlive &&
                           hero.IsActive &&
                           !hero.IsTemplate &&
                           hero.IsLord &&
                           KaiRaceLifecycle.IsDawi(hero))
            .ToArray();

        var adultMen = livingDawiLords.Count(hero => !hero.IsFemale && hero.Age >= KaiRaceLifecycle.DawiMarriageAge);
        var adultWomen = livingDawiLords.Count(hero => hero.IsFemale && hero.Age >= KaiRaceLifecycle.DawiMarriageAge);

        var targetWomen = Math.Min(MaximumGeneratedWomenPerClan, Math.Max(1, (adultMen + 3) / 4));
        if (adultWomen >= targetWomen)
            return;

        var settlement = FindSafeHomeSettlement(clan);
        if (settlement == null)
        {
            KaiRuntimeLog.Write("DAWI_WOMAN_FAIL", $"clan={clan.StringId}; reason=no_safe_home");
            return;
        }

        if (!TryCreateDawiWoman(clan, settlement, out _))
            return;

        _generationCooldownUntilDays[clan.StringId] = now + GenerationCooldownDays;
    }

    private static bool TryCreateDawiWoman(Clan clan, Settlement settlement, out Hero createdHero)
    {
        createdHero = null;
        try
        {
            var manager = MBObjectManager.Instance;
            if (manager == null)
            {
                KaiRuntimeLog.Write("DAWI_WOMAN_FAIL", $"clan={clan?.StringId ?? "null"}; reason=no_object_manager");
                return false;
            }

            var template = manager.GetObject<CharacterObject>(DawiWomenAssetBridge.FemaleDawiLordTemplateId);
            if (template == null || !template.IsFemale)
            {
                KaiRuntimeLog.Write("DAWI_WOMAN_FAIL", $"clan={clan?.StringId ?? "null"}; reason=template_missing_or_not_female");
                return false;
            }

            var dwarfRace = FaceGen.GetRaceOrDefault("dwarf");
            var humanRace = FaceGen.GetRaceOrDefault("human");
            if (dwarfRace == humanRace || template.Race != dwarfRace)
            {
                KaiRuntimeLog.Write("DAWI_WOMAN_FAIL", $"clan={clan?.StringId ?? "null"}; reason=invalid_dwarf_race");
                return false;
            }

            var minimumAge = (int)Math.Ceiling(KaiRaceLifecycle.DawiFertilityStart);
            var maximumAge = Math.Min(DawiFemaleMaximumGeneratedAge, (int)Math.Floor(KaiRaceLifecycle.DawiFertilityEnd));
            var dayStamp = Math.Abs((int)CampaignTime.Now.ToDays);
            var ageSpan = maximumAge - minimumAge + 1;
            var age = minimumAge + dayStamp % Math.Max(1, ageSpan);

            // HeroCreator owns the initial clan assignment. Do not follow this call with
            // hero.Clan = ...: direct campaign-graph mutation is explicitly forbidden.
            var hero = HeroCreator.CreateSpecialHero(template, settlement, clan, null, age);
            if (hero == null || !hero.IsFemale || hero.CharacterObject?.Race != dwarfRace || hero.Clan != clan)
            {
                KaiRuntimeLog.Write(
                    "DAWI_WOMAN_FAIL",
                    $"clan={clan?.StringId ?? "null"}; reason=post_create_validation; hero={hero?.StringId ?? "null"}; heroClan={hero?.Clan?.StringId ?? "null"}");
                if (hero != null)
                    KillCharacterAction.ApplyByRemove(hero);
                return false;
            }

            if (!hero.IsLord)
                hero.SetNewOccupation(Occupation.Lord);

            if (settlement != null && !settlement.HeroesWithoutParty.Contains(hero))
                EnterSettlementAction.ApplyForCharacterOnly(hero, settlement);

            var placedInSettlement = settlement != null && settlement.HeroesWithoutParty.Contains(hero);
            var success = hero.IsAlive &&
                          hero.IsLord &&
                          hero.Clan == clan &&
                          KaiRaceLifecycle.IsDawi(hero) &&
                          placedInSettlement;

            if (success)
                createdHero = hero;
            else if (hero.IsAlive)
                KillCharacterAction.ApplyByRemove(hero);

            KaiRuntimeLog.Write(
                success ? "DAWI_WOMAN_CREATE" : "DAWI_WOMAN_FAIL",
                $"clan={clan.StringId}; hero={hero.StringId}; name={hero.Name}; age={hero.Age:0.0}; settlement={settlement?.StringId ?? "null"}; placed={placedInSettlement}; success={success}");
            return success;
        }
        catch (Exception ex)
        {
            KaiRuntimeLog.Exception("DAWI_WOMAN_FAIL", ex, $"clan={clan?.StringId ?? "null"}; settlement={settlement?.StringId ?? "null"}");
            return false;
        }
    }

    private static int GetPartnerShortageScore(Clan clan)
    {
        if (clan == null)
            return int.MinValue;

        var eligible = clan.Heroes
            .Where(hero => hero != null && hero.IsAlive && hero.IsActive && hero.IsLord && KaiRaceLifecycle.IsDawi(hero))
            .ToArray();

        var unmarriedMen = eligible.Count(hero => !hero.IsFemale && hero.Spouse == null && hero.Age >= KaiRaceLifecycle.DawiMarriageAge);
        var unmarriedWomen = eligible.Count(hero => hero.IsFemale && hero.Spouse == null && hero.Age >= KaiRaceLifecycle.DawiMarriageAge);
        return unmarriedMen - unmarriedWomen;
    }

    private static Settlement FindSafeHomeSettlement(Clan clan)
        => clan.Settlements
               .Where(settlement => settlement != null && settlement.IsFortification && !settlement.IsUnderSiege)
               .OrderBy(settlement => settlement.StringId, StringComparer.Ordinal)
               .FirstOrDefault()
           ?? (clan.HomeSettlement != null && !clan.HomeSettlement.IsUnderSiege ? clan.HomeSettlement : null);

    private static bool IsEligibleDawiClan(Clan clan)
    {
        if (clan == null || clan == Clan.PlayerClan)
            return false;
        if (clan.IsEliminated || clan.IsBanditFaction || clan.IsRebelClan || clan.IsMinorFaction || clan.IsClanTypeMercenary)
            return false;
        if (!string.Equals(clan.Culture?.StringId, DawiCultureId, StringComparison.Ordinal))
            return false;

        return clan.Kingdom != null && !clan.Kingdom.IsEliminated;
    }

    public IEnumerable<string> DescribeStatus()
    {
        yield return $"Женщины-гномы: ресурсы {(DawiWomenAssetBridge.IsAvailable ? "готовы" : "недоступны")}.";
        yield return $"Automatic population: {(AutomaticPopulationEnabled ? "ON (bounded weekly shortage fill)" : "OFF")}";
        yield return $"Возраст для семьи: {KaiRaceLifecycle.DawiFertilityStart:0}-{KaiRaceLifecycle.DawiFertilityEnd:0} лет; возраст создаваемых женщин — не старше {DawiFemaleMaximumGeneratedAge} лет.";
        if (Campaign.Current == null)
            yield break;

        foreach (var clan in Clan.All
                     .Where(clan => clan != null && string.Equals(clan.Culture?.StringId, DawiCultureId, StringComparison.Ordinal))
                     .OrderBy(clan => clan.StringId, StringComparer.Ordinal))
        {
            var women = clan.Heroes.Count(hero => hero != null && hero.IsAlive && hero.IsFemale && hero.IsLord && KaiRaceLifecycle.IsDawi(hero));
            var men = clan.Heroes.Count(hero => hero != null && hero.IsAlive && !hero.IsFemale && hero.IsLord && KaiRaceLifecycle.IsDawi(hero));
            yield return $"{clan.Name}: Dawi lords M={men}, F={women}, shortage={GetPartnerShortageScore(clan)}";
        }
    }
}
