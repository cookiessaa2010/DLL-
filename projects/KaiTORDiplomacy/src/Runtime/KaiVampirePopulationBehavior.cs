using System;
using System.Collections.Generic;
using System.Linq;
using KaiTOR.Diplomacy.Models;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Independent vampire realm continuity. It creates no heroes and never rewires clans:
/// an existing eligible mortal in an AI vampire realm may receive the same safe Blood
/// Kiss conversion used by the player dialogue. Player kingdoms and MainHero are excluded.
/// </summary>
public sealed class KaiVampirePopulationBehavior : CampaignBehaviorBase
{
    public const bool AutomaticPopulationEnabled = true;

    private const string SylvaniaCultureId = "khuzait";
    private const string MousillonCultureId = "mousillon";
    private const int BloodKissCooldownDays = 180;
    private const int MaximumVampireScions = 8;
    private const int BloodKissMinimumRelation = 0;
    private const string CooldownSaveKey = "kaitor_vampire_population_cooldown_v1";

    private Dictionary<string, double> _cooldownUntilDays = new();

    public override void RegisterEvents()
    {
        CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
    }

    public override void SyncData(IDataStore dataStore)
    {
        dataStore.SyncData(CooldownSaveKey, ref _cooldownUntilDays);
        _cooldownUntilDays ??= new Dictionary<string, double>();
    }

    private void OnWeeklyTick()
    {
        if (!AutomaticPopulationEnabled || Campaign.Current == null)
            return;

        var playerKingdom = Clan.PlayerClan?.Kingdom;
        foreach (var kingdom in Kingdom.All
                     .Where(IsVampireRealm)
                     .Where(k => k != playerKingdom)
                     .OrderBy(k => k.StringId, StringComparer.Ordinal))
        {
            TryAdvanceRealm(kingdom);
        }
    }

    private void TryAdvanceRealm(Kingdom kingdom)
    {
        var now = CampaignTime.Now.ToDays;
        if (GetCooldown(kingdom.StringId) > now)
        {
            Log("SKIP", kingdom, $"reason=cooldown; remaining={Math.Ceiling(GetCooldown(kingdom.StringId) - now):0}d");
            return;
        }

        var clans = kingdom.Clans.Where(IsNormalNobleClan).ToArray();
        if (clans.Length == 0)
        {
            Log("SKIP", kingdom, "reason=no_noble_clans");
            return;
        }

        var heroes = clans
            .SelectMany(c => c.Heroes)
            .Where(h => h != null && h.IsAlive && h.IsActive)
            .Distinct()
            .ToArray();

        var target = Math.Min(MaximumVampireScions, Math.Max(2, clans.Length));
        var current = heroes.Count(TorFamilySafety.IsVampire);
        if (current >= target)
        {
            Log("SKIP", kingdom, $"reason=quota; vampires={current}; target={target}");
            return;
        }

        var sponsor = heroes
            .Where(TorFamilySafety.IsVampire)
            .OrderByDescending(h => h == kingdom.Leader)
            .ThenByDescending(h => h.IsLord)
            .ThenBy(h => h.StringId, StringComparer.Ordinal)
            .FirstOrDefault();

        if (sponsor == null)
        {
            Log("SKIP", kingdom, "reason=no_vampire_sponsor");
            return;
        }

        var candidates = heroes
            .Where(h => IsEligibleCandidate(h, kingdom, sponsor))
            .Select(h => new { Hero = h, Score = ScoreCandidate(h, sponsor) })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Hero.StringId, StringComparer.Ordinal)
            .ToArray();

        if (candidates.Length == 0)
        {
            Log("SKIP", kingdom, $"reason=no_candidate; sponsor={sponsor.StringId}; vampires={current}; target={target}");
            return;
        }

        var candidate = candidates[0].Hero;
        Log("BLOOD_KISS_BEGIN", kingdom, $"sponsor={sponsor.StringId}; target={candidate.StringId}; candidates={candidates.Length}");

        if (!TorProfessionEffectBridge.ApplyBloodKissConversion(candidate, out var error))
        {
            Log("BLOOD_KISS_FAILED", kingdom, $"target={candidate.StringId}; error={error}");
            return;
        }

        _cooldownUntilDays[kingdom.StringId] = now + BloodKissCooldownDays;
        Log("BLOOD_KISS_SUCCESS", kingdom,
            $"sponsor={sponsor.StringId}; target={candidate.StringId}; cooldown={BloodKissCooldownDays}d; vampiresBefore={current}; targetQuota={target}");
        KaiRuntimeLog.Write("BLOOD_KISS_SUCCESS", $"mode=ai_population; kingdom={kingdom.StringId}; source={sponsor.StringId}; target={candidate.StringId}");
    }

    private static bool IsEligibleCandidate(Hero hero, Kingdom kingdom, Hero sponsor)
    {
        if (hero == null || hero == sponsor || hero == Hero.MainHero)
            return false;
        if (!hero.IsAlive || !hero.IsActive || hero.IsTemplate || hero.IsMinorFactionHero)
            return false;
        if (hero.Clan == null || hero.Clan.Kingdom != kingdom || hero.Clan == Clan.PlayerClan)
            return false;
        if (hero == kingdom.Leader || hero.Clan.Leader == hero)
            return false;
        if (hero.Age < Campaign.Current.Models.AgeModel.HeroComesOfAge)
            return false;
        if (hero.IsPrisoner || hero.PartyBelongedToAsPrisoner != null)
            return false;
        if (hero.PartyBelongedTo?.MapEvent != null || hero.PartyBelongedTo?.BesiegedSettlement != null)
            return false;
        if (hero.Spouse != null || hero.Children.Count > 0)
            return false;
        if (!hero.IsLord && !TorFamilySafety.IsAiCompanion(hero))
            return false;
        if (TorFamilySafety.IsVampire(hero) || TorFamilySafety.IsUndead(hero))
            return false;
        if (hero.CharacterObject == null || hero.CharacterObject.Race != FaceGen.GetRaceOrDefault("human"))
            return false;

        return hero.GetRelation(sponsor) >= BloodKissMinimumRelation;
    }

    private static int ScoreCandidate(Hero hero, Hero sponsor)
    {
        var score = 0;
        if (TorFamilySafety.IsAiCompanion(hero)) score += 80;
        if (hero.Clan == sponsor.Clan) score += 50;
        score += hero.GetRelation(sponsor);
        score += Math.Min(30, hero.Level / 2);
        return score;
    }

    private static bool IsVampireRealm(Kingdom kingdom)
    {
        if (kingdom == null || kingdom.IsEliminated || kingdom.Leader == null)
            return false;
        var id = kingdom.Culture?.StringId;
        return string.Equals(id, SylvaniaCultureId, StringComparison.Ordinal) ||
               string.Equals(id, MousillonCultureId, StringComparison.Ordinal);
    }

    private static bool IsNormalNobleClan(Clan clan)
        => clan != null &&
           !clan.IsEliminated &&
           !clan.IsBanditFaction &&
           !clan.IsRebelClan &&
           !clan.IsMinorFaction &&
           !clan.IsClanTypeMercenary;

    private double GetCooldown(string kingdomId)
        => _cooldownUntilDays != null &&
           kingdomId != null &&
           _cooldownUntilDays.TryGetValue(kingdomId, out var value) &&
           !double.IsNaN(value) &&
           !double.IsInfinity(value)
            ? value
            : 0d;

    private static void Log(string stage, Kingdom kingdom, string details)
        => KaiPopulationLog.Write("vampire", stage, $"kingdom={kingdom?.StringId ?? "null"}; {details}");
}
