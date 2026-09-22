using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KaiTOR.Diplomacy.Models;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Abstract player Dawi dynasty. Marriage is a house-to-house union: no female Hero,
/// female CharacterObject, skin, skeleton, FaceGen body or child Hero is ever created.
/// Sons live as save records until maturity, then materialize as adult male TOR Dawi.
/// </summary>
public sealed class KaiDawiDynastyBehavior : CampaignBehaviorBase
{
    private const string UnionClanKey = "kaitor_dawi_union_clan_v1";
    private const string UnionStartKey = "kaitor_dawi_union_start_v1";
    private const string NextHeirKey = "kaitor_dawi_union_next_heir_v1";
    private const string HeirsKey = "kaitor_dawi_virtual_heirs_v1";

    public const int FirstHeirDelayDays = 180;
    public const int HeirIntervalDays = 240;
    public const int HeirMaturityYears = 4;
    public const int AdultSpawnAge = 30;
    public const int MaximumHeirs = 4;
    public const int UnionRelationBonus = 20;
    public const int UnionTrustBonus = 20;
    public const int UnionNapDays = 180;

    private static readonly string[] MaleHeirNames =
    {
        "Торик", "Борин", "Дурган", "Казрик", "Гримнир", "Харек", "Оррик", "Барек"
    };

    private string _unionClanId;
    private double _unionStartDay = -1d;
    private double _nextHeirDay = -1d;
    private List<string> _heirs = new();

    public bool HasPlayerUnion => !string.IsNullOrWhiteSpace(_unionClanId);

    public override void RegisterEvents()
    {
        CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
    }

    public override void SyncData(IDataStore dataStore)
    {
        dataStore.SyncData(UnionClanKey, ref _unionClanId);
        dataStore.SyncData(UnionStartKey, ref _unionStartDay);
        dataStore.SyncData(NextHeirKey, ref _nextHeirDay);
        dataStore.SyncData(HeirsKey, ref _heirs);

        _heirs ??= new List<string>();
        _heirs = _heirs.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();

        if (double.IsNaN(_unionStartDay) || double.IsInfinity(_unionStartDay))
            _unionStartDay = -1d;
        if (double.IsNaN(_nextHeirDay) || double.IsInfinity(_nextHeirDay))
            _nextHeirDay = -1d;
    }

    private void OnSessionLaunched(CampaignGameStarter starter)
    {
        starter.AddPlayerLine(
            "kaitor_dawi_dynasty_offer",
            "hero_main_options",
            "kaitor_dawi_dynasty_reply",
            "Предлагаю заключить брачный союз между нашими домами.",
            CanOfferUnion,
            null,
            126,
            null,
            null);

        starter.AddDialogLine(
            "kaitor_dawi_dynasty_accept",
            "kaitor_dawi_dynasty_reply",
            "close_window",
            "Пусть наши дома будут связаны клятвой. Союз принят.",
            CanAcceptUnion,
            CreateUnion,
            120,
            null);

        starter.AddDialogLine(
            "kaitor_dawi_dynasty_reject",
            "kaitor_dawi_dynasty_reply",
            "close_window",
            "Между нашими домами пока недостаточно доверия для такого союза.",
            CanRejectUnion,
            () => KaiRuntimeLog.Write(
                "DAWI_DYNASTIC_UNION_REJECTED",
                $"playerClan={Clan.PlayerClan?.StringId ?? "null"}; targetClan={Hero.OneToOneConversationHero?.Clan?.StringId ?? "null"}"),
            110,
            null);
    }

    private bool CanOfferUnion()
    {
        var main = Hero.MainHero;
        var playerClan = Clan.PlayerClan;
        var target = Hero.OneToOneConversationHero;
        var targetClan = target?.Clan;

        if (Campaign.Current == null || main == null || playerClan == null || target == null || targetClan == null)
            return false;
        if (!KaiRaceLifecycle.IsDawi(main) || main.Age < KaiRaceLifecycle.DawiMarriageAge)
            return false;
        if (HasPlayerUnion || targetClan == playerClan || targetClan.IsEliminated)
            return false;
        if (targetClan.Leader != target || !target.IsAlive || !target.IsActive || target.IsPrisoner)
            return false;
        if (!KaiRaceLifecycle.IsDawi(target))
            return false;
        if (FactionManager.IsAtWarAgainstFaction(playerClan.MapFaction, targetClan.MapFaction))
            return false;

        return true;
    }

    private bool CanAcceptUnion()
        => CanOfferUnion() &&
           Hero.OneToOneConversationHero.Clan.GetRelationWithClan(Clan.PlayerClan) >= 0;

    private bool CanRejectUnion()
        => CanOfferUnion() && !CanAcceptUnion();

    private void CreateUnion()
    {
        var target = Hero.OneToOneConversationHero;
        var targetClan = target?.Clan;
        var playerClan = Clan.PlayerClan;
        if (target == null || targetClan == null || playerClan == null || !CanAcceptUnion())
            return;

        _unionClanId = targetClan.StringId;
        _unionStartDay = CampaignTime.Now.ToDays;
        _nextHeirDay = _unionStartDay + FirstHeirDelayDays;

        if (playerClan.Leader != null && targetClan.Leader != null)
            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(
                playerClan.Leader,
                targetClan.Leader,
                UnionRelationBonus,
                true);

        var playerKingdom = playerClan.Kingdom;
        var targetKingdom = targetClan.Kingdom;
        var diplomacy = Campaign.Current?.GetCampaignBehavior<KaiDiplomacyBehavior>();
        var nap = "same_or_no_kingdom";

        if (diplomacy != null &&
            playerKingdom != null &&
            targetKingdom != null &&
            playerKingdom != targetKingdom)
        {
            diplomacy.AdjustTrust(playerKingdom, targetKingdom, UnionTrustBonus);
            nap = diplomacy.EnsureNonAggressionPactAtLeast(
                    playerKingdom,
                    targetKingdom,
                    UnionNapDays,
                    out var reason)
                ? "active"
                : "unavailable:" + reason;
        }

        InformationManager.DisplayMessage(new InformationMessage(
            $"Брачный союз Dawi заключён между домом {playerClan.Name} и домом {targetClan.Name}."));

        KaiRuntimeLog.Write(
            "DAWI_DYNASTIC_UNION",
            $"playerClan={playerClan.StringId}; targetClan={targetClan.StringId}; relation=+{UnionRelationBonus}; trust=+{UnionTrustBonus}; nap={nap}; firstHeirIn={FirstHeirDelayDays}d");
    }

    private void OnDailyTick()
    {
        if (Campaign.Current == null || Hero.MainHero == null || Clan.PlayerClan == null)
            return;
        if (!KaiRaceLifecycle.IsDawi(Hero.MainHero) || !HasPlayerUnion)
            return;

        TryCreateVirtualHeir();
        TryMaterializeHeirs();
    }

    private void TryCreateVirtualHeir()
    {
        if (_heirs.Count >= MaximumHeirs ||
            _nextHeirDay < 0d ||
            CampaignTime.Now.ToDays < _nextHeirDay)
            return;

        var ordinal = _heirs.Count;
        var name = MaleHeirNames[ordinal % MaleHeirNames.Length];
        var born = CampaignTime.Now.ToDays;
        var mature = born + CampaignTime.DaysInYear * HeirMaturityYears;

        _heirs.Add(Encode(name, born, mature, string.Empty));
        _nextHeirDay = born + HeirIntervalDays;

        InformationManager.DisplayMessage(new InformationMessage(
            $"В брачном союзе Dawi родился сын: {name}. До вступления в дом как взрослого героя пройдёт около {HeirMaturityYears} игровых лет."));

        KaiRuntimeLog.Write(
            "DAWI_HEIR_VIRTUAL_BORN",
            $"name={name}; ordinal={ordinal + 1}; birthDay={born:0.00}; maturityDay={mature:0.00}; unionClan={_unionClanId}");
    }

    private void TryMaterializeHeirs()
    {
        for (var i = 0; i < _heirs.Count; i++)
        {
            if (!TryDecode(_heirs[i], out var name, out var born, out var mature, out var heroId))
                continue;
            if (!string.IsNullOrWhiteSpace(heroId) || CampaignTime.Now.ToDays < mature)
                continue;

            var created = MaterializeMaleHeir(name);
            if (created != null)
                _heirs[i] = Encode(name, born, mature, created.StringId);
        }
    }

    private Hero MaterializeMaleHeir(string name)
    {
        Hero created = null;
        try
        {
            var template = ResolveMaleDawiTemplate();
            if (template == null || template.IsFemale)
            {
                KaiRuntimeLog.Write(
                    "DAWI_HEIR_MATERIALIZE_FAIL",
                    $"name={name}; reason=no_male_dawi_template");
                return null;
            }

            var partnerClan = ResolveUnionClan();
            var settlement =
                MobileParty.MainParty?.CurrentSettlement
                ?? Clan.PlayerClan?.HomeSettlement
                ?? partnerClan?.HomeSettlement;

            created = HeroCreator.CreateSpecialHero(
                template,
                settlement,
                Clan.PlayerClan,
                null,
                AdultSpawnAge);

            if (created == null || created.IsFemale || !KaiRaceLifecycle.IsDawi(created))
                throw new InvalidOperationException("HeroCreator returned a non-male or non-Dawi heir.");

            var displayName = new TextObject(name);
            created.SetName(displayName, displayName);

            if (!created.IsLord)
                created.SetNewOccupation(Occupation.Lord);

            created.ChangeState(Hero.CharacterStates.Active);
            created.IsKnownToPlayer = true;

            AdoptHeroAction.Apply(created);

            if (MobileParty.MainParty != null &&
                MobileParty.MainParty.MapEvent == null &&
                created.PartyBelongedTo != MobileParty.MainParty)
            {
                AddHeroToPartyAction.Apply(created, MobileParty.MainParty, false);
            }

            var parentLinked =
                created.Father == Hero.MainHero ||
                created.Mother == Hero.MainHero;

            var success =
                created.IsAlive &&
                created.IsActive &&
                created.Clan == Clan.PlayerClan &&
                parentLinked &&
                !created.IsFemale &&
                KaiRaceLifecycle.IsDawi(created);

            var educationApplied = false;
            var educationReport = "not_attempted";
            if (success)
            {
                var education = Campaign.Current?.GetCampaignBehavior<KaiLoreEducationBehavior>();
                educationApplied = education != null && education.ApplySyntheticFullTorPath(created, out educationReport);
                if (!educationApplied)
                    KaiRuntimeLog.Write("DAWI_HEIR_EDUCATION_FAIL",
                        $"hero={created.StringId}; report={educationReport}");
            }

            KaiRuntimeLog.Write(
                success ? "DAWI_HEIR_MATERIALIZED" : "DAWI_HEIR_MATERIALIZE_FAIL",
                $"hero={created.StringId}; name={created.Name}; age={created.Age:0.0}; clan={created.Clan?.StringId ?? "null"}; parentLinked={parentLinked}; party={created.PartyBelongedTo?.StringId ?? "null"}; educationApplied={educationApplied}; education={educationReport}; success={success}");

            if (!success)
                return null;

            InformationManager.DisplayMessage(new InformationMessage(
                $"{created.Name} достиг возраста службы и вступил в ваш дом как взрослый наследник Dawi."));

            return created;
        }
        catch (Exception ex)
        {
            KaiRuntimeLog.Exception(
                "DAWI_HEIR_MATERIALIZE_FAIL",
                ex,
                $"name={name}; hero={created?.StringId ?? "null"}");
            return null;
        }
    }

    private CharacterObject ResolveMaleDawiTemplate()
    {
        var partner = ResolveUnionClan();

        var templateHero = partner?.AliveLords
            .FirstOrDefault(h =>
                h != null && h.IsAlive && h.IsActive && !h.IsFemale && KaiRaceLifecycle.IsDawi(h));

        templateHero ??= Clan.PlayerClan?.Heroes
            .FirstOrDefault(h =>
                h != null && h.IsAlive && h.IsActive && !h.IsFemale && KaiRaceLifecycle.IsDawi(h));

        templateHero ??= Hero.AllAliveHeroes
            .FirstOrDefault(h =>
                h != null && h.IsActive && !h.IsFemale && h.IsLord && KaiRaceLifecycle.IsDawi(h));

        if (templateHero?.CharacterObject == null)
            return null;

        var original = templateHero.CharacterObject.OriginalCharacter;
        return original != null && !original.IsFemale
            ? original
            : templateHero.CharacterObject;
    }

    private Clan ResolveUnionClan()
        => string.IsNullOrWhiteSpace(_unionClanId)
            ? null
            : Clan.All.FirstOrDefault(c =>
                c != null && string.Equals(c.StringId, _unionClanId, StringComparison.Ordinal));

    public IEnumerable<string> DescribePlayerHouse()
    {
        if (Hero.MainHero == null || !KaiRaceLifecycle.IsDawi(Hero.MainHero))
            yield break;

        var unionClan = ResolveUnionClan();
        yield return HasPlayerUnion
            ? $"Брачный союз Dawi: дом {unionClan?.Name?.ToString() ?? _unionClanId}. Супруга-Hero намеренно не создаётся."
            : "Брачный союз Dawi: не заключён. Поговорите с главой другого дома Dawi.";

        foreach (var raw in _heirs)
        {
            if (!TryDecode(raw, out var name, out var born, out var mature, out var heroId))
                continue;

            if (!string.IsNullOrWhiteSpace(heroId))
            {
                var hero = Hero.AllAliveHeroes.FirstOrDefault(h =>
                    h != null && string.Equals(h.StringId, heroId, StringComparison.Ordinal));
                yield return $"• {hero?.Name?.ToString() ?? name} — взрослый наследник дома.";
                continue;
            }

            var years = Math.Max(
                0,
                (int)Math.Floor((CampaignTime.Now.ToDays - born) / CampaignTime.DaysInYear));
            var daysLeft = Math.Max(
                0,
                (int)Math.Ceiling(mature - CampaignTime.Now.ToDays));

            yield return $"• {name} — сын, {years} игровых лет; до вступления в дом около {daysLeft} дн.";
        }
    }

    public IEnumerable<string> DescribeStatus()
    {
        yield return $"Dawi abstract dynasty: union={(HasPlayerUnion ? _unionClanId : "none")}; heirs={_heirs.Count}/{MaximumHeirs}; nextHeirDay={_nextHeirDay:0.00}.";
        yield return "Female Dawi Hero/assets: disabled; adult heirs only use existing male TOR Dawi templates.";
    }

    private static string Encode(string name, double born, double mature, string heroId)
        => string.Join(
            "\t",
            name ?? string.Empty,
            born.ToString("R", CultureInfo.InvariantCulture),
            mature.ToString("R", CultureInfo.InvariantCulture),
            heroId ?? string.Empty);

    private static bool TryDecode(
        string raw,
        out string name,
        out double born,
        out double mature,
        out string heroId)
    {
        name = string.Empty;
        born = 0d;
        mature = 0d;
        heroId = string.Empty;

        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var parts = raw.Split('\t');
        if (parts.Length < 4)
            return false;
        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out born) ||
            !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out mature))
            return false;

        name = parts[0];
        heroId = parts[3];
        return true;
    }
}
