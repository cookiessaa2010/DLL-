using System;
using System.Collections.Generic;
using KaiTOR.Diplomacy.Runtime;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.Library;

namespace KaiTOR.Diplomacy.Models;

/// <summary>
/// Biological decorator over the active TOR/Bannerlord pregnancy model.
/// Ordinary mortal humans keep the active Bannerlord/TOR pregnancy model. Dawi and
/// long-lived elves use the Bannerlord 1.3.15 chance structure with a race-normalized
/// biological age and no vanilla upper-age cutoff. Vampires, ordinary undead and
/// Greenskins are routed out by the central lore biology gate.
/// </summary>
public sealed class KaiPregnancyModel : PregnancyModel
{
    private readonly PregnancyModel _baseModel;
    private readonly HashSet<string> _loggedThisDay = new(StringComparer.Ordinal);
    private int _lastLogDay = -1;

    public KaiPregnancyModel(PregnancyModel baseModel)
    {
        _baseModel = baseModel;
    }

    public string UnderlyingModelTypeName => _baseModel?.GetType().FullName ?? "<null>";

    public override float PregnancyDurationInDays => _baseModel?.PregnancyDurationInDays ?? 36f;
    public override float MaternalMortalityProbabilityInLabor => _baseModel?.MaternalMortalityProbabilityInLabor ?? 0f;
    public override float StillbirthProbability => _baseModel?.StillbirthProbability ?? 0f;
    public override float DeliveringFemaleOffspringProbability => _baseModel?.DeliveringFemaleOffspringProbability ?? 0.5f;
    public override float DeliveringTwinsProbability => _baseModel?.DeliveringTwinsProbability ?? 0f;

    public override float GetDailyChanceOfPregnancyForHero(Hero hero)
    {
        if (_baseModel == null || hero == null)
            return 0f;

        var spouse = hero.Spouse;
        if (spouse == null)
            return _baseModel.GetDailyChanceOfPregnancyForHero(hero);

        // Biology and social marriage are intentionally separate. A pair can be a valid
        // family while still being excluded from Bannerlord's offspring generator.
        var blockReason = TorFamilySafety.GetVanillaPregnancyBlockReason(hero, spouse);
        if (blockReason != null)
        {
            LogOncePerDay("PREGNANCY_BLOCKED", hero, spouse, $"reason={blockReason}");
            return 0f;
        }

        if (!KaiRaceLifecycle.UsesCustomFertility(hero))
        {
            // TOR's active pregnancy model can return an unconditional zero even for
            // ordinary living same-race couples. Live-test logs confirmed this for
            // multiple human couples. Reuse Bannerlord 1.3.15's native fertility
            // structure here instead of delegating a valid couple back into TOR's gate.
            var mortalChance = GetBannerlordStyleDailyChance(hero);
            LogOncePerDay(
                mortalChance > 0f ? "PREGNANCY_ALLOWED" : "PREGNANCY_BLOCKED",
                hero,
                spouse,
                $"bannerlord_curve; calendarAge={hero.Age:0.0}; chance={mortalChance:0.000000}; underlying={UnderlyingModelTypeName}");
            return mortalChance;
        }

        var chance = GetRaceAwareDailyChance(hero);
        LogOncePerDay(
            chance > 0f ? "PREGNANCY_ALLOWED" : "PREGNANCY_BLOCKED",
            hero,
            spouse,
            $"race_aware; calendarAge={hero.Age:0.0}; biologicalAge={KaiRaceLifecycle.GetBiologicalAge(hero):0.0}; chance={chance:0.000000}");
        return chance;
    }

    private static float GetBannerlordStyleDailyChance(Hero hero)
    {
        var spouse = hero?.Spouse;
        var clan = hero?.Clan;
        if (hero == null || spouse == null || clan == null)
            return 0f;

        if (!KaiRaceLifecycle.IsWithinLoreFertilityWindow(hero))
            return 0f;

        var childCountFactor = hero.Children.Count + 1;
        var desiredClanSize = 4f + 4f * clan.Tier;
        if (desiredClanSize <= 0f)
            desiredClanSize = 4f;

        var aliveLords = clan.AliveLords.Count;
        var clanPopulationFactor = hero != Hero.MainHero && spouse != Hero.MainHero
            ? Math.Min(1f, (2f * desiredClanSize - aliveLords) / desiredClanSize)
            : 1f;
        clanPopulationFactor = Math.Max(0f, clanPopulationFactor);

        var ageFactor = Math.Max(
            0f,
            1.2f - (hero.Age - KaiRaceLifecycle.HumanFertilityStart) * 0.04f);

        var chance = ageFactor /
                     (childCountFactor * childCountFactor) *
                     0.12f *
                     clanPopulationFactor;

        var explained = new ExplainedNumber(chance, false, null);
        if (hero.GetPerkValue(DefaultPerks.Charm.Virile) || spouse.GetPerkValue(DefaultPerks.Charm.Virile))
            explained.AddFactor(DefaultPerks.Charm.Virile.PrimaryBonus, DefaultPerks.Charm.Virile.Name);

        return Math.Max(0f, explained.ResultNumber);
    }

    private static float GetRaceAwareDailyChance(Hero hero)
    {
        var spouse = hero?.Spouse;
        var clan = hero?.Clan;
        if (hero == null || spouse == null || clan == null)
            return 0f;

        if (!KaiRaceLifecycle.IsWithinCustomFertilityWindow(hero))
            return 0f;

        var childCountFactor = hero.Children.Count + 1;
        var desiredClanSize = 4f + 4f * clan.Tier;
        if (desiredClanSize <= 0f)
            desiredClanSize = 4f;

        var aliveLords = clan.AliveLords.Count;
        var clanPopulationFactor = hero != Hero.MainHero && spouse != Hero.MainHero
            ? Math.Min(1f, (2f * desiredClanSize - aliveLords) / desiredClanSize)
            : 1f;
        clanPopulationFactor = Math.Max(0f, clanPopulationFactor);

        var ageFactor = KaiRaceLifecycle.GetCustomPregnancyAgeFactor(hero);
        var chance = ageFactor / (childCountFactor * childCountFactor) * 0.12f * clanPopulationFactor;

        var explained = new ExplainedNumber(chance, false, null);
        if (hero.GetPerkValue(DefaultPerks.Charm.Virile) || spouse.GetPerkValue(DefaultPerks.Charm.Virile))
            explained.AddFactor(DefaultPerks.Charm.Virile.PrimaryBonus, DefaultPerks.Charm.Virile.Name);

        return Math.Max(0f, explained.ResultNumber);
    }

    private void LogOncePerDay(string stage, Hero hero, Hero spouse, string details)
    {
        try
        {
            var day = (int)Math.Floor(CampaignTime.Now.ToDays);
            if (day != _lastLogDay)
            {
                _lastLogDay = day;
                _loggedThisDay.Clear();
            }

            var key = $"{stage}:{hero?.StringId}:{spouse?.StringId}";
            if (!_loggedThisDay.Add(key))
                return;

            var line = $"hero={hero?.StringId ?? "null"}; spouse={spouse?.StringId ?? "null"}; " +
                       $"heroClan={hero?.Clan?.StringId ?? "none"}; spouseClan={spouse?.Clan?.StringId ?? "none"}; " +
                       $"heroCulture={hero?.Culture?.StringId ?? "none"}; spouseCulture={spouse?.Culture?.StringId ?? "none"}; {details}";
            KaiRuntimeLog.Write(stage, line);
            KaiFamilyLog.Write(stage, line);
        }
        catch
        {
            // Model calculations must stay side-effect safe even if diagnostics fail.
        }
    }
}
