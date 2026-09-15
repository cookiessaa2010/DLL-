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
/// Race-specific population continuity that deliberately does not force every TOR race
/// through Bannerlord marriage/pregnancy. Humans and supported elves keep native family
/// growth; Dawi unlock native family growth only with the optional female asset pack;
/// Greenskins gain adult heroes from off-screen spores; vampire realms replenish rare
/// immortal scions through the same public race mutation TOR uses for Blood Kiss.
/// </summary>
public sealed class KaiRacialPopulationBehavior : CampaignBehaviorBase
{
    private const string GreenskinCultureId = "aserai";
    private const string SylvaniaCultureId = "khuzait";
    private const string MousillonCultureId = "mousillon";

    private const double SporePressureThreshold = 100d;
    private const double SporeBaseWeeklyGain = 2d;
    private const double SporeFortificationWeeklyGain = 1.25d;
    private const int SporeSpawnCooldownDays = 84;
    private const int MaximumGreenskinAiCompanions = 10;

    private const int BloodKissCooldownDays = 180;
    private const int MaximumVampireScions = 8;
    private const int BloodKissMinimumRelation = 0;

    private const string SporePressureSaveKey = "kaitor_racial_spore_pressure_v1";
    private const string SporeCooldownSaveKey = "kaitor_racial_spore_cooldown_v1";
    private const string BloodKissCooldownSaveKey = "kaitor_racial_bloodkiss_cooldown_v1";

    private static readonly string[] GreenskinWandererTemplateIds =
    {
        "tor_wanderer_greenskins_0", // Big Boss
        "tor_wanderer_greenskins_1", // Shaman
        "tor_wanderer_greenskins_2", // Bully
        "tor_wanderer_greenskins_3"  // Goblin Tinkerer
    };

    private Dictionary<string, double> _sporePressureByKingdom = new();
    private Dictionary<string, double> _sporeCooldownUntilDays = new();
    private Dictionary<string, double> _bloodKissCooldownUntilDays = new();

    public override void RegisterEvents()
    {
        CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
    }

    public override void SyncData(IDataStore dataStore)
    {
        dataStore.SyncData(SporePressureSaveKey, ref _sporePressureByKingdom);
        dataStore.SyncData(SporeCooldownSaveKey, ref _sporeCooldownUntilDays);
        dataStore.SyncData(BloodKissCooldownSaveKey, ref _bloodKissCooldownUntilDays);

        _sporePressureByKingdom ??= new Dictionary<string, double>();
        _sporeCooldownUntilDays ??= new Dictionary<string, double>();
        _bloodKissCooldownUntilDays ??= new Dictionary<string, double>();
    }

    private void OnWeeklyTick()
    {
        if (Campaign.Current == null)
            return;

        // Match KaiDynastyAiBehavior: autonomous world-growth systems never make
        // population/dynasty decisions for the player's current kingdom.
        var playerKingdom = Clan.PlayerClan?.Kingdom;

        foreach (var kingdom in Kingdom.All
                     .Where(k => k != null && !k.IsEliminated && k.Leader != null && k != playerKingdom)
                     .OrderBy(k => k.StringId, StringComparer.Ordinal))
        {
            var cultureId = kingdom.Culture?.StringId;
            if (string.Equals(cultureId, GreenskinCultureId, StringComparison.Ordinal))
            {
                AdvanceGreenskinSpores(kingdom);
            }
            else if (string.Equals(cultureId, SylvaniaCultureId, StringComparison.Ordinal) ||
                     string.Equals(cultureId, MousillonCultureId, StringComparison.Ordinal))
            {
                TryAdvanceVampireScions(kingdom);
            }
        }
    }

    private void AdvanceGreenskinSpores(Kingdom kingdom)
    {
        var fortifications = kingdom.Settlements
            .Where(settlement => settlement != null && settlement.IsFortification)
            .ToArray();
        if (fortifications.Length == 0)
            return;

        var currentPressure = GetValue(_sporePressureByKingdom, kingdom.StringId);
        var weeklyGain = SporeBaseWeeklyGain + fortifications.Length * SporeFortificationWeeklyGain;
        currentPressure = Math.Min(SporePressureThreshold * 2d, currentPressure + weeklyGain);
        _sporePressureByKingdom[kingdom.StringId] = currentPressure;

        if (currentPressure < SporePressureThreshold)
            return;

        var now = CampaignTime.Now.ToDays;
        if (GetValue(_sporeCooldownUntilDays, kingdom.StringId) > now)
            return;

        var target = GetGreenskinAiCompanionTarget(kingdom);
        if (CountGreenskinAiCompanions(kingdom) >= target)
            return;

        if (!TorFamilySafety.CanAddAttribute)
            return;

        var hostClan = GetGreenskinHostClan(kingdom);
        if (hostClan == null)
            return;

        var settlement = hostClan.Settlements
            .Where(x => x != null && x.IsFortification)
            .OrderBy(x => x.StringId, StringComparer.Ordinal)
            .FirstOrDefault()
            ?? fortifications.OrderBy(x => x.StringId, StringComparer.Ordinal).FirstOrDefault();
        if (settlement == null)
            return;

        if (!TrySpawnSporeBornHero(hostClan, settlement))
            return;

        _sporePressureByKingdom[kingdom.StringId] = Math.Max(0d, currentPressure - SporePressureThreshold);
        _sporeCooldownUntilDays[kingdom.StringId] = now + SporeSpawnCooldownDays;
    }

    private static Clan GetGreenskinHostClan(Kingdom kingdom)
        => kingdom.Clans
            .Where(IsNormalNobleClan)
            .OrderBy(clan => clan.Heroes.Count(hero => hero != null && hero.IsAlive && TorFamilySafety.IsAiCompanion(hero)))
            .ThenBy(clan => clan.StringId, StringComparer.Ordinal)
            .FirstOrDefault();

    private static bool TrySpawnSporeBornHero(Clan hostClan, Settlement settlement)
    {
        var manager = MBObjectManager.Instance;
        if (manager == null)
            return false;

        var dayStamp = Math.Abs((int)CampaignTime.Now.ToDays);
        CharacterObject template = null;
        for (var offset = 0; offset < GreenskinWandererTemplateIds.Length; offset++)
        {
            var index = (dayStamp + offset) % GreenskinWandererTemplateIds.Length;
            template = manager.GetObject<CharacterObject>(GreenskinWandererTemplateIds[index]);
            if (template != null)
                break;
        }

        if (template == null)
            return false;

        var adultAge = Campaign.Current.Models.AgeModel.HeroComesOfAge;
        var age = adultAge + 12 + dayStamp % 20;
        var hero = HeroCreator.CreateSpecialHero(template, settlement, hostClan, null, age);
        if (hero == null)
            return false;

        hero.SetNewOccupation(Occupation.Special);
        hero.Clan = hostClan;
        hero.SupporterOf = null;

        // TORAICompanionCampaignBehavior recognises the combination of occupation
        // Special + the exact AICompanion extended attribute. If TOR's mutation bridge
        // moved, remove the just-created hero instead of leaving an unmanaged special.
        if (!TorFamilySafety.TryAddAttribute(hero, "AICompanion") || !TorFamilySafety.IsAiCompanion(hero))
        {
            KillCharacterAction.ApplyByRemove(hero);
            return false;
        }

        return true;
    }

    private void TryAdvanceVampireScions(Kingdom kingdom)
    {
        var now = CampaignTime.Now.ToDays;
        if (GetValue(_bloodKissCooldownUntilDays, kingdom.StringId) > now)
            return;

        var nobleClans = kingdom.Clans.Where(IsNormalNobleClan).ToArray();
        if (nobleClans.Length == 0)
            return;

        var heroes = nobleClans
            .SelectMany(clan => clan.Heroes)
            .Where(hero => hero != null && hero.IsAlive && hero.IsActive)
            .Distinct()
            .ToArray();

        var target = Math.Min(MaximumVampireScions, Math.Max(2, nobleClans.Length));
        if (heroes.Count(TorFamilySafety.IsVampire) >= target)
            return;

        var sponsor = heroes
            .Where(TorFamilySafety.IsVampire)
            .OrderByDescending(hero => hero == kingdom.Leader)
            .ThenByDescending(hero => hero.IsLord)
            .ThenBy(hero => hero.StringId, StringComparer.Ordinal)
            .FirstOrDefault();
        if (sponsor == null)
            return;

        var candidate = heroes
            .Where(hero => IsEligibleForBloodKiss(hero, kingdom, sponsor))
            .Select(hero => new BloodKissCandidate(hero, ScoreBloodKissCandidate(hero, sponsor)))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Hero.StringId, StringComparer.Ordinal)
            .Select(x => x.Hero)
            .FirstOrDefault();
        if (candidate == null)
            return;

        if (!TorFamilySafety.TryApplyBloodKiss(candidate))
            return;

        _bloodKissCooldownUntilDays[kingdom.StringId] = now + BloodKissCooldownDays;
    }

    private static bool IsEligibleForBloodKiss(Hero hero, Kingdom kingdom, Hero sponsor)
    {
        if (hero == null || hero == sponsor || hero == Hero.MainHero)
            return false;
        if (!hero.IsAlive || !hero.IsActive || hero.IsTemplate || hero.IsMinorFactionHero)
            return false;
        if (hero.Clan?.Kingdom != kingdom)
            return false;
        if (hero.Age < Campaign.Current.Models.AgeModel.HeroComesOfAge)
            return false;
        if (hero.PartyBelongedToAsPrisoner != null)
            return false;
        if (hero.PartyBelongedTo?.MapEvent != null || hero.PartyBelongedTo?.Army != null)
            return false;
        if (hero.Spouse != null || hero.Children.Count > 0)
            return false;
        if (!hero.IsLord && !TorFamilySafety.IsAiCompanion(hero))
            return false;
        if (TorFamilySafety.IsVampire(hero) || TorFamilySafety.IsUndead(hero))
            return false;

        // Blood Kiss is a mortal-human conversion. Dawi, elves, Greenskins and other
        // custom races are deliberately excluded until we discuss those lore cases.
        var humanRace = FaceGen.GetRaceOrDefault("human");
        if (hero.CharacterObject == null || hero.CharacterObject.Race != humanRace)
            return false;

        return hero.GetRelation(sponsor) >= BloodKissMinimumRelation;
    }

    private static int ScoreBloodKissCandidate(Hero hero, Hero sponsor)
    {
        var score = 0;
        if (TorFamilySafety.IsAiCompanion(hero))
            score += 80;
        if (hero.Clan == sponsor.Clan)
            score += 50;
        if (hero.Father == sponsor || hero.Mother == sponsor)
            score += 40;

        score += hero.GetRelation(sponsor);
        score += Math.Min(30, hero.Level / 2);
        return score;
    }

    private static bool IsNormalNobleClan(Clan clan)
        => clan != null &&
           !clan.IsEliminated &&
           !clan.IsBanditFaction &&
           !clan.IsRebelClan &&
           !clan.IsMinorFaction &&
           !clan.IsClanTypeMercenary;

    private static int GetGreenskinAiCompanionTarget(Kingdom kingdom)
        => Math.Min(MaximumGreenskinAiCompanions, Math.Max(2, kingdom.Clans.Count(IsNormalNobleClan) * 2));

    private static int CountGreenskinAiCompanions(Kingdom kingdom)
        => kingdom.Clans
            .Where(IsNormalNobleClan)
            .SelectMany(clan => clan.Heroes)
            .Count(hero => hero != null &&
                           hero.IsAlive &&
                           TorFamilySafety.IsCulture(hero, GreenskinCultureId) &&
                           TorFamilySafety.IsAiCompanion(hero));

    public IEnumerable<string> DescribeStatus()
    {
        yield return $"Dawi female asset gate: {(DawiWomenAssetBridge.IsAvailable ? "READY" : "MISSING/SAFE-OFF")}";

        if (Campaign.Current == null)
            yield break;

        var now = CampaignTime.Now.ToDays;
        foreach (var kingdom in Kingdom.All
                     .Where(k => k != null && !k.IsEliminated)
                     .OrderBy(k => k.StringId, StringComparer.Ordinal))
        {
            var cultureId = kingdom.Culture?.StringId;
            if (string.Equals(cultureId, GreenskinCultureId, StringComparison.Ordinal))
            {
                var pressure = GetValue(_sporePressureByKingdom, kingdom.StringId);
                var cooldown = Math.Max(0d, GetValue(_sporeCooldownUntilDays, kingdom.StringId) - now);
                yield return $"{kingdom.Name}: spores={pressure:0.0}/{SporePressureThreshold:0}, " +
                             $"AI-companions={CountGreenskinAiCompanions(kingdom)}/{GetGreenskinAiCompanionTarget(kingdom)}, " +
                             $"cooldown={Math.Ceiling(cooldown):0}d";
            }
            else if (string.Equals(cultureId, SylvaniaCultureId, StringComparison.Ordinal) ||
                     string.Equals(cultureId, MousillonCultureId, StringComparison.Ordinal))
            {
                var clans = kingdom.Clans.Where(IsNormalNobleClan).ToArray();
                var vampires = clans.SelectMany(clan => clan.Heroes)
                    .Where(hero => hero != null && hero.IsAlive)
                    .Distinct()
                    .Count(TorFamilySafety.IsVampire);
                var target = Math.Min(MaximumVampireScions, Math.Max(2, clans.Length));
                var cooldown = Math.Max(0d, GetValue(_bloodKissCooldownUntilDays, kingdom.StringId) - now);
                yield return $"{kingdom.Name}: vampires={vampires}/{target}, BloodKiss cooldown={Math.Ceiling(cooldown):0}d";
            }
        }
    }

    private static double GetValue(Dictionary<string, double> dictionary, string key)
        => dictionary != null && key != null && dictionary.TryGetValue(key, out var value) &&
           !double.IsNaN(value) && !double.IsInfinity(value)
            ? value
            : 0d;

    private sealed class BloodKissCandidate
    {
        public BloodKissCandidate(Hero hero, int score)
        {
            Hero = hero;
            Score = score;
        }

        public Hero Hero { get; }
        public int Score { get; }
    }
}
