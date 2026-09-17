using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Persistent kingdom-level dynastic bonds created by explicit political marriages.
/// The marriage itself remains a native Bannerlord MarriageAction; this behavior only
/// stores primitive kingdom/hero ids and applies additive diplomatic consequences.
/// </summary>
public sealed class KaiPoliticalMarriageBehavior : CampaignBehaviorBase
{
    public const int PoliticalMarriageCost = 500000;
    public const int DynasticTrustBonus = 30;
    public const int DynasticRulerRelationBonus = 20;
    public const int DynasticNapDays = 180;

    private const string FirstSpouseSaveKey = "kaitor_dynastic_bond_first_spouse_v1";
    private const string SecondSpouseSaveKey = "kaitor_dynastic_bond_second_spouse_v1";
    private const string CreatedDaySaveKey = "kaitor_dynastic_bond_created_day_v1";

    private Dictionary<string, string> _firstSpouseByKingdomPair = new();
    private Dictionary<string, string> _secondSpouseByKingdomPair = new();
    private Dictionary<string, double> _createdDayByKingdomPair = new();

    public override void RegisterEvents()
    {
        CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
    }

    public override void SyncData(IDataStore dataStore)
    {
        dataStore.SyncData(FirstSpouseSaveKey, ref _firstSpouseByKingdomPair);
        dataStore.SyncData(SecondSpouseSaveKey, ref _secondSpouseByKingdomPair);
        dataStore.SyncData(CreatedDaySaveKey, ref _createdDayByKingdomPair);

        _firstSpouseByKingdomPair ??= new Dictionary<string, string>();
        _secondSpouseByKingdomPair ??= new Dictionary<string, string>();
        _createdDayByKingdomPair ??= new Dictionary<string, double>();
    }

    private void OnDailyTick()
    {
        foreach (var key in _firstSpouseByKingdomPair.Keys.ToArray())
        {
            if (!TryResolveBond(key, out _, out _, out _, out _))
                RemoveBond(key);
        }
    }

    public bool CanStartPoliticalMarriage(Kingdom playerKingdom, Kingdom targetKingdom, out string reason)
    {
        reason = string.Empty;

        if (Campaign.Current == null || Hero.MainHero == null || Clan.PlayerClan == null)
        {
            reason = "Сейчас династические переговоры недоступны.";
            return false;
        }

        if (playerKingdom == null || targetKingdom == null || playerKingdom == targetKingdom)
        {
            reason = "Для политического брака нужны две разные державы.";
            return false;
        }

        if (playerKingdom.IsEliminated || targetKingdom.IsEliminated || playerKingdom.Leader == null || targetKingdom.Leader == null)
        {
            reason = "Одна из держав больше не может заключать династические соглашения.";
            return false;
        }

        if (playerKingdom.Leader != Hero.MainHero || playerKingdom.RulingClan != Clan.PlayerClan)
        {
            reason = "Политический брачный союз может заключить только правитель державы.";
            return false;
        }

        if (FactionManager.IsAtWarAgainstFaction(playerKingdom, targetKingdom))
        {
            reason = "Во время войны сначала необходимо заключить мир.";
            return false;
        }

        if (HasActiveBond(playerKingdom, targetKingdom))
        {
            reason = "Между этими державами уже действует династический союз.";
            return false;
        }

        var diplomacy = Campaign.Current.GetCampaignBehavior<KaiDiplomacyBehavior>();
        if (diplomacy == null || !diplomacy.RuntimeEnabled)
        {
            reason = "Дополнительные дипломатические соглашения сейчас недоступны.";
            return false;
        }

        var cooldown = diplomacy.GetNapCooldownRemainingDays(playerKingdom, targetKingdom);
        if (cooldown > 0)
        {
            reason = $"После недавнего нарушения договоров новые династические соглашения будут доступны через {cooldown} дн.";
            return false;
        }

        if (Hero.MainHero.Gold < PoliticalMarriageCost)
        {
            reason = $"Для политического брачного соглашения требуется {PoliticalMarriageCost:N0} динаров.";
            return false;
        }

        return true;
    }

    public bool TryFinalizePoliticalMarriage(
        Kingdom playerKingdom,
        Kingdom targetKingdom,
        Hero playerCandidate,
        Hero targetCandidate,
        out string result)
    {
        result = string.Empty;

        if (!CanStartPoliticalMarriage(playerKingdom, targetKingdom, out result))
            return false;

        if (!IsCandidateFromRulingHouse(playerCandidate, playerKingdom, allowMainHero: true) ||
            !IsCandidateFromRulingHouse(targetCandidate, targetKingdom, allowMainHero: false))
        {
            result = "Состав правящих домов изменился. Выберите кандидатов заново.";
            return false;
        }

        var marriageModel = Campaign.Current?.Models?.MarriageModel;
        if (marriageModel == null || !playerCandidate.CanMarry() || !targetCandidate.CanMarry() ||
            !marriageModel.IsCoupleSuitableForMarriage(playerCandidate, targetCandidate))
        {
            result = "Эта пара больше не может заключить брак по действующим законам мира.";
            return false;
        }

        var recipient = targetKingdom.Leader;
        if (recipient == null || Hero.MainHero.Gold < PoliticalMarriageCost)
        {
            result = $"Для соглашения требуется {PoliticalMarriageCost:N0} динаров.";
            return false;
        }

        // Pay first, but refund if native MarriageAction fails before the bond is registered.
        GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, recipient, PoliticalMarriageCost, false);

        try
        {
            MarriageAction.Apply(playerCandidate, targetCandidate, true);
        }
        catch
        {
            GiveGoldAction.ApplyBetweenCharacters(recipient, Hero.MainHero, PoliticalMarriageCost, false);
            result = "Брачное соглашение не удалось оформить. Плата возвращена.";
            return false;
        }

        RegisterBond(playerKingdom, targetKingdom, playerCandidate, targetCandidate);

        result = $"Политический брак заключён: {playerCandidate.Name} и {targetCandidate.Name}. " +
                 $"Династический союз укрепил отношения держав и закрепил пакт о ненападении на {DynasticNapDays} дней.";
        return true;
    }

    public bool HasActiveBond(Kingdom first, Kingdom second)
    {
        if (first == null || second == null)
            return false;

        var key = TreatyKey.For(first, second);
        if (!_firstSpouseByKingdomPair.ContainsKey(key))
            return false;

        if (TryResolveBond(key, out var storedFirst, out var storedSecond, out _, out _) &&
            ((storedFirst == first && storedSecond == second) || (storedFirst == second && storedSecond == first)))
            return true;

        RemoveBond(key);
        return false;
    }

    public void EndBondBecauseOfWar(Kingdom first, Kingdom second)
    {
        if (first == null || second == null)
            return;
        RemoveBond(TreatyKey.For(first, second));
    }

    public IEnumerable<string> DescribeActiveBonds()
    {
        foreach (var key in _firstSpouseByKingdomPair.Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray())
        {
            if (!TryResolveBond(key, out var firstKingdom, out var secondKingdom, out var firstSpouse, out var secondSpouse))
            {
                RemoveBond(key);
                continue;
            }

            _createdDayByKingdomPair.TryGetValue(key, out var createdDay);
            yield return $"{firstKingdom.Name} <-> {secondKingdom.Name}: {firstSpouse.Name} + {secondSpouse.Name}, created day {createdDay:0}.";
        }
    }

    public static IEnumerable<Hero> GetRulingHouseCandidates(Kingdom kingdom, bool allowMainHero)
    {
        if (kingdom?.RulingClan == null || Campaign.Current?.Models?.MarriageModel == null)
            return Enumerable.Empty<Hero>();

        return kingdom.RulingClan.AliveLords
            .Where(hero => IsCandidateFromRulingHouse(hero, kingdom, allowMainHero))
            .OrderByDescending(hero => hero == Hero.MainHero)
            .ThenBy(hero => hero.Name.ToString());
    }

    private void RegisterBond(Kingdom first, Kingdom second, Hero firstSpouse, Hero secondSpouse)
    {
        var key = TreatyKey.For(first, second);
        _firstSpouseByKingdomPair[key] = firstSpouse.StringId;
        _secondSpouseByKingdomPair[key] = secondSpouse.StringId;
        _createdDayByKingdomPair[key] = CampaignTime.Now.ToDays;

        var diplomacy = Campaign.Current?.GetCampaignBehavior<KaiDiplomacyBehavior>();
        diplomacy?.AdjustTrust(first, second, DynasticTrustBonus);
        diplomacy?.EnsureNonAggressionPact(first, second, DynasticNapDays);

        if (first.Leader != null && second.Leader != null && first.Leader != second.Leader)
            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(first.Leader, second.Leader, DynasticRulerRelationBonus, true);
    }

    private static bool IsCandidateFromRulingHouse(Hero hero, Kingdom kingdom, bool allowMainHero)
    {
        if (hero == null || kingdom?.RulingClan == null || hero.Clan != kingdom.RulingClan)
            return false;
        if (!hero.IsAlive || !hero.IsActive || hero.IsTemplate || hero.IsMinorFactionHero || hero.IsPrisoner)
            return false;
        if (!hero.CanMarry())
            return false;

        // Moving an AI kingdom leader through MarriageAction is too invasive for the
        // global test. The player's own ruler is explicitly safe to keep available.
        if (hero == kingdom.Leader && !(allowMainHero && hero == Hero.MainHero))
            return false;

        return true;
    }

    private bool TryResolveBond(
        string key,
        out Kingdom firstKingdom,
        out Kingdom secondKingdom,
        out Hero firstSpouse,
        out Hero secondSpouse)
    {
        firstKingdom = null;
        secondKingdom = null;
        firstSpouse = null;
        secondSpouse = null;

        if (!TreatyKey.TrySplit(key, out var firstKingdomId, out var secondKingdomId))
            return false;
        if (!_firstSpouseByKingdomPair.TryGetValue(key, out var firstHeroId) ||
            !_secondSpouseByKingdomPair.TryGetValue(key, out var secondHeroId))
            return false;

        firstKingdom = Kingdom.All.FirstOrDefault(k => k != null && !k.IsEliminated && string.Equals(k.StringId, firstKingdomId, StringComparison.Ordinal));
        secondKingdom = Kingdom.All.FirstOrDefault(k => k != null && !k.IsEliminated && string.Equals(k.StringId, secondKingdomId, StringComparison.Ordinal));
        firstSpouse = Hero.AllAliveHeroes.FirstOrDefault(h => h != null && string.Equals(h.StringId, firstHeroId, StringComparison.Ordinal));
        secondSpouse = Hero.AllAliveHeroes.FirstOrDefault(h => h != null && string.Equals(h.StringId, secondHeroId, StringComparison.Ordinal));

        return firstKingdom != null && secondKingdom != null &&
               firstSpouse != null && secondSpouse != null &&
               firstSpouse.IsAlive && secondSpouse.IsAlive &&
               firstSpouse.Spouse == secondSpouse && secondSpouse.Spouse == firstSpouse;
    }

    private void RemoveBond(string key)
    {
        _firstSpouseByKingdomPair.Remove(key);
        _secondSpouseByKingdomPair.Remove(key);
        _createdDayByKingdomPair.Remove(key);
    }
}
