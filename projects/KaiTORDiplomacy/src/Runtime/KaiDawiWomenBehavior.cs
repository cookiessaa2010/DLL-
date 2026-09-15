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
/// Optional Dawi female-population bridge. It stays completely dormant until the
/// external KaiTOR_DawiWomen asset module has registered a real female dwarf lord
/// template against TOR's existing dwarf race. No surrogate human females are ever
/// created and the player clan is never populated automatically.
/// </summary>
public sealed class KaiDawiWomenBehavior : CampaignBehaviorBase
{
    private const string DawiCultureId = "sturgia";
    private const int DawiFemaleMinimumAge = 30;
    private const int DawiFemaleMaximumGeneratedAge = 110;
    private const int MaximumGeneratedWomenPerClan = 3;
    private const int GenerationCooldownDays = 336;
    private const string CooldownSaveKey = "kaitor_dawi_women_generation_cooldown_v1";

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
        if (Campaign.Current == null || !DawiWomenAssetBridge.IsAvailable)
            return;

        foreach (var clan in Clan.All
                     .Where(IsEligibleDawiClan)
                     .OrderBy(clan => clan.StringId, StringComparer.Ordinal))
        {
            TryPopulateClan(clan);
        }
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
                           TorFamilySafety.IsCulture(hero, DawiCultureId))
            .ToArray();

        var adultMen = livingDawiLords.Count(hero => !hero.IsFemale && hero.Age >= DawiFemaleMinimumAge);
        var adultWomen = livingDawiLords.Count(hero => hero.IsFemale && hero.Age >= DawiFemaleMinimumAge);

        // Conservative bootstrap only. Once real Dawi marriages and daughters exist,
        // native Bannerlord/TOR family growth should carry the clan instead of KaiTOR
        // continuously spawning replacement nobles.
        var targetWomen = Math.Min(MaximumGeneratedWomenPerClan, Math.Max(1, (adultMen + 3) / 4));
        if (adultWomen >= targetWomen)
            return;

        var settlement = FindSafeHomeSettlement(clan);
        if (settlement == null)
            return;

        if (!TryCreateDawiWoman(clan, settlement))
            return;

        _generationCooldownUntilDays[clan.StringId] = now + GenerationCooldownDays;
    }

    private static bool TryCreateDawiWoman(Clan clan, Settlement settlement)
    {
        var manager = MBObjectManager.Instance;
        if (manager == null)
            return false;

        var template = manager.GetObject<CharacterObject>(DawiWomenAssetBridge.FemaleDawiLordTemplateId);
        if (template == null || !template.IsFemale)
            return false;

        var dwarfRace = FaceGen.GetRaceOrDefault("dwarf");
        if (template.Race != dwarfRace)
            return false;

        var dayStamp = Math.Abs((int)CampaignTime.Now.ToDays);
        var ageSpan = DawiFemaleMaximumGeneratedAge - DawiFemaleMinimumAge + 1;
        var age = DawiFemaleMinimumAge + dayStamp % ageSpan;

        var hero = HeroCreator.CreateSpecialHero(template, settlement, clan, null, age);
        if (hero == null || !hero.IsFemale || hero.CharacterObject?.Race != dwarfRace)
        {
            if (hero != null)
                KillCharacterAction.ApplyByRemove(hero);
            return false;
        }

        if (!hero.IsLord)
            hero.SetNewOccupation(Occupation.Lord);
        hero.Clan = clan;

        return hero.IsAlive && hero.Clan == clan;
    }

    private static Settlement FindSafeHomeSettlement(Clan clan)
        => clan.Settlements
               .Where(settlement => settlement != null && settlement.IsFortification)
               .OrderBy(settlement => settlement.StringId, StringComparer.Ordinal)
               .FirstOrDefault()
           ?? clan.HomeSettlement;

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
        yield return $"Dawi women assets: {(DawiWomenAssetBridge.IsAvailable ? "READY" : "MISSING/SAFE-OFF")}";
        if (Campaign.Current == null)
            yield break;

        foreach (var clan in Clan.All
                     .Where(clan => clan != null && string.Equals(clan.Culture?.StringId, DawiCultureId, StringComparison.Ordinal))
                     .OrderBy(clan => clan.StringId, StringComparer.Ordinal))
        {
            var women = clan.Heroes.Count(hero => hero != null && hero.IsAlive && hero.IsFemale && hero.IsLord);
            var men = clan.Heroes.Count(hero => hero != null && hero.IsAlive && !hero.IsFemale && hero.IsLord);
            yield return $"{clan.Name}: Dawi lords M={men}, F={women}";
        }
    }
}
