using System;
using System.Collections.Generic;
using System.Linq;
using KaiTOR.Diplomacy.Models;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Player-facing Blood Kiss conversation flow. This behavior is independent from the
/// autonomous vampire-population system so it remains available while population AI
/// is safety-off. v0.6.5.7 completes successful mortal conversion by binding the
/// converted hero to the player's bloodline/clan and safely leaving the encounter.
/// </summary>
public sealed class KaiBloodKissBehavior : CampaignBehaviorBase
{
    private const int BloodKissCooldownDays = 180;
    private const string CooldownSaveKey = "kaitor_player_blood_kiss_cooldown_v1";
    private const string ProgenySaveKey = "kaitor_player_blood_kiss_progeny_v1";
    private const string TrollWarriorTemplateId = "tor_gs_trolls";

    private double _cooldownUntilDays;
    private List<string> _bloodProgenyIds = new();

    public override void RegisterEvents()
    {
        CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        CampaignEvents.OnGameLoadedEvent.AddNonSerializedListener(this, OnGameLoaded);
    }

    public override void SyncData(IDataStore dataStore)
    {
        dataStore.SyncData(CooldownSaveKey, ref _cooldownUntilDays);
        dataStore.SyncData(ProgenySaveKey, ref _bloodProgenyIds);

        if (double.IsNaN(_cooldownUntilDays) || double.IsInfinity(_cooldownUntilDays) || _cooldownUntilDays < 0d)
            _cooldownUntilDays = 0d;

        _bloodProgenyIds ??= new List<string>();
        _bloodProgenyIds = _bloodProgenyIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private void OnGameLoaded(CampaignGameStarter starter)
    {
        RepairTrackedBloodProgeny();
    }

    private void OnSessionLaunched(CampaignGameStarter starter)
    {
        // TOR's ordinary troll greeting goes start -> close_window, so a troll can
        // never reach hero_main_options. For a vampire only, intercept that greeting
        // with a higher-priority route that still requires the player to explicitly
        // choose Blood Kiss. This works for both troll heroes and troop conversations.
        starter.AddDialogLine(
            "kaitor_vampire_troll_greeting",
            "start",
            "kaitor_vampire_troll_options",
            "*Тролль втягивает воздух и настороженно рычит, чувствуя в вас нечто противоестественное.*",
            CanRouteDirectTrollConversation,
            null,
            250,
            null);

        starter.AddPlayerLine(
            "kaitor_vampire_troll_offer",
            "kaitor_vampire_troll_options",
            "kaitor_vampire_troll_response",
            "Даровать поцелуй крови.",
            null,
            null,
            125,
            null,
            null);

        starter.AddDialogLine(
            "kaitor_vampire_troll_response",
            "kaitor_vampire_troll_response",
            "kaitor_blood_kiss_troll_choice",
            "Тролль не принимает поцелуй крови. Но его первобытную волю можно попытаться подчинить и обратить его силу на службу вашему дому.",
            CanRouteDirectTrollConversation,
            null,
            250,
            null);

        starter.AddPlayerLine(
            "kaitor_vampire_troll_leave",
            "kaitor_vampire_troll_options",
            "close_window",
            "Уйти.",
            null,
            null,
            90,
            null,
            null);

        starter.AddPlayerLine(
            "kaitor_blood_kiss_start",
            "hero_main_options",
            "kaitor_blood_kiss_route",
            "Даровать поцелуй крови.",
            CanShowBloodKissOption,
            null,
            125,
            null,
            null);

        // Save-recovery path for heroes converted by older KaiTOR builds.
        // v0.6.5.6 did not persist progeny ids, so the player must explicitly confirm
        // which existing MinorVampire should be bound into the bloodline.
        starter.AddPlayerLine(
            "kaitor_blood_kiss_claim_existing",
            "hero_main_options",
            "close_window",
            "Признать дитя крови частью моего рода.",
            CanClaimExistingBloodProgeny,
            ClaimExistingBloodProgeny,
            124,
            null,
            null);

        // Foreign clan leaders are always protected before race-specific routing.
        starter.AddDialogLine(
            "kaitor_blood_kiss_clan_leader",
            "kaitor_blood_kiss_route",
            "close_window",
            "Вы пытаетесь приблизиться, но охрана правителя слишком бдительна. Ваши намерения раскрыты, и вас немедленно выдворяют.",
            IsTargetForeignClanLeader,
            () => LogBlocked("clan_leader"),
            400,
            null);

        starter.AddDialogLine(
            "kaitor_blood_kiss_troll",
            "kaitor_blood_kiss_route",
            "kaitor_blood_kiss_troll_choice",
            "Тролль не принимает поцелуй крови. Но его первобытную волю можно попытаться подчинить и обратить его силу на службу вашему дому.",
            IsTargetTroll,
            null,
            350,
            null);

        starter.AddPlayerLine(
            "kaitor_blood_kiss_troll_accept",
            "kaitor_blood_kiss_troll_choice",
            "close_window",
            "Подчинись. Отныне ты будешь сражаться за меня.",
            null,
            TryRecruitTrollWarrior,
            120,
            null,
            null);

        starter.AddPlayerLine(
            "kaitor_blood_kiss_troll_decline",
            "kaitor_blood_kiss_troll_choice",
            "hero_main_options",
            "Нет. Оставим это.",
            null,
            null,
            100,
            null,
            null);

        starter.AddDialogLine(
            "kaitor_blood_kiss_undead",
            "kaitor_blood_kiss_route",
            "close_window",
            "Поцелуй крови не оказывает никакого эффекта.",
            IsTargetUndead,
            () => LogBlocked("undead"),
            300,
            null);

        starter.AddDialogLine(
            "kaitor_blood_kiss_greenskin",
            "kaitor_blood_kiss_route",
            "close_window",
            "Поцелуй крови не оказывает никакого эффекта.",
            IsTargetGreenskin,
            () => LogBlocked("greenskin"),
            280,
            null);

        starter.AddDialogLine(
            "kaitor_blood_kiss_unsupported",
            "kaitor_blood_kiss_route",
            "close_window",
            "Поцелуй крови не оказывает никакого эффекта.",
            IsTargetUnsupportedRace,
            () => LogBlocked("unsupported_race"),
            250,
            null);

        starter.AddDialogLine(
            "kaitor_blood_kiss_mortal",
            "kaitor_blood_kiss_route",
            "kaitor_blood_kiss_mortal_confirm",
            "Вы чувствуете живую кровь. Если продолжить, обратного пути для смертного уже не будет.",
            IsTargetMortalHuman,
            null,
            100,
            null);

        starter.AddPlayerLine(
            "kaitor_blood_kiss_mortal_accept",
            "kaitor_blood_kiss_mortal_confirm",
            "close_window",
            "Прими мой дар. Дарую тебе поцелуй крови.",
            null,
            ApplyBloodKissToTarget,
            120,
            null,
            null);

        starter.AddPlayerLine(
            "kaitor_blood_kiss_mortal_decline",
            "kaitor_blood_kiss_mortal_confirm",
            "hero_main_options",
            "Не сейчас.",
            null,
            null,
            100,
            null,
            null);
    }

    private bool CanRouteDirectTrollConversation()
    {
        if (Campaign.Current == null || !TorFamilySafety.IsVampire(Hero.MainHero))
            return false;
        if (_cooldownUntilDays > CampaignTime.Now.ToDays)
            return false;

        var character = CharacterObject.OneToOneConversationCharacter;
        return character != null &&
               character.Race == FaceGen.GetRaceOrDefault("troll");
    }

    private bool CanShowBloodKissOption()
    {
        var target = Hero.OneToOneConversationHero;
        if (Campaign.Current == null || target == null || target == Hero.MainHero)
            return false;
        if (!target.IsAlive || !target.IsActive || target.IsTemplate)
            return false;
        if (target.Age < Campaign.Current.Models.AgeModel.HeroComesOfAge)
            return false;
        if (!TorFamilySafety.IsVampire(Hero.MainHero))
            return false;
        if (_cooldownUntilDays > CampaignTime.Now.ToDays)
            return false;

        return true;
    }

    private static bool IsTargetForeignClanLeader()
    {
        var target = Hero.OneToOneConversationHero;
        return target?.Clan != null &&
               target.Clan != Clan.PlayerClan &&
               target.Clan.Leader == target;
    }

    private static bool IsTargetTroll()
    {
        var target = Hero.OneToOneConversationHero;
        return target?.CharacterObject != null &&
               target.CharacterObject.Race == FaceGen.GetRaceOrDefault("troll") &&
               !IsTargetForeignClanLeader();
    }

    private static bool IsTargetUndead()
    {
        var target = Hero.OneToOneConversationHero;
        return target != null &&
               !IsTargetForeignClanLeader() &&
               !IsTargetTroll() &&
               (TorFamilySafety.IsVampire(target) || TorFamilySafety.IsUndead(target));
    }

    private static bool IsTargetGreenskin()
    {
        var target = Hero.OneToOneConversationHero;
        if (target?.CharacterObject == null || IsTargetForeignClanLeader() || IsTargetTroll())
            return false;

        var race = target.CharacterObject.Race;
        return TorFamilySafety.IsCulture(target, "aserai") ||
               race == FaceGen.GetRaceOrDefault("orc") ||
               race == FaceGen.GetRaceOrDefault("goblin");
    }

    private static bool IsTargetMortalHuman()
    {
        var target = Hero.OneToOneConversationHero;
        if (target?.CharacterObject == null || IsTargetForeignClanLeader())
            return false;
        if (IsTargetTroll() || IsTargetUndead() || IsTargetGreenskin())
            return false;

        return target.CharacterObject.Race == FaceGen.GetRaceOrDefault("human");
    }

    private static bool IsTargetUnsupportedRace()
    {
        var target = Hero.OneToOneConversationHero;
        return target != null &&
               !IsTargetForeignClanLeader() &&
               !IsTargetTroll() &&
               !IsTargetUndead() &&
               !IsTargetGreenskin() &&
               !IsTargetMortalHuman();
    }

    private void ApplyBloodKissToTarget()
    {
        var target = Hero.OneToOneConversationHero;
        if (!CanShowBloodKissOption() || target == null || !IsTargetMortalHuman())
        {
            KaiRuntimeLog.Write("BLOOD_KISS_BLOCKED", $"target={target?.StringId ?? "null"}; reason=stale_or_ineligible");
            return;
        }

        var oldClan = target.Clan;
        var oldParty = target.PartyBelongedTo;
        var wasPartyLeader = oldParty?.LeaderHero == target;

        KaiRuntimeLog.Write(
            "BLOOD_KISS_ALLOWED",
            $"source={Hero.MainHero.StringId}; target={target.StringId}; oldClan={oldClan?.StringId ?? "null"}; oldParty={oldParty?.StringId ?? "null"}; partyLeader={wasPartyLeader}; outcome=mortal_conversion");

        if (!TorProfessionEffectBridge.ApplyBloodKissConversion(target, out var error))
        {
            KaiRuntimeLog.Write("BLOOD_KISS_BLOCKED", $"target={target.StringId}; reason=conversion_failed; error={error}");
            return;
        }

        TrackBloodProgeny(target);

        if (!TryBindBloodProgeny(target, oldParty, wasPartyLeader, out var bindError))
        {
            _cooldownUntilDays = CampaignTime.Now.ToDays + BloodKissCooldownDays;
            KaiRuntimeLog.Write(
                "BLOOD_KISS_FAMILY_FAILED",
                $"target={target.StringId}; converted=true; error={bindError}; trackedForRepair=true");
            return;
        }

        _cooldownUntilDays = CampaignTime.Now.ToDays + BloodKissCooldownDays;

        if (PlayerEncounter.Current != null)
        {
            PlayerEncounter.LeaveEncounter = true;
            KaiRuntimeLog.Write("BLOOD_KISS_ENCOUNTER_EXIT", $"target={target.StringId}; leaveEncounter=true");
        }

        KaiRuntimeLog.Write(
            "BLOOD_KISS_SUCCESS",
            $"source={Hero.MainHero.StringId}; target={target.StringId}; race={target.CharacterObject?.Race}; career={TorProfessionEffectBridge.GetCurrentCareerId(target) ?? "none"}; clan={target.Clan?.StringId ?? "null"}; parent={(target.Father == Hero.MainHero || target.Mother == Hero.MainHero)}; party={target.PartyBelongedTo?.StringId ?? "null"}; cooldown={BloodKissCooldownDays}d");

        MBInformationManager.AddQuickInformation(
            new TaleWorlds.Localization.TextObject($"{target.Name} принят{(target.IsFemale ? "а" : string.Empty)} в ваш кровный род."),
            3000,
            target.CharacterObject,
            null,
            string.Empty);
    }

    private bool CanClaimExistingBloodProgeny()
    {
        var target = Hero.OneToOneConversationHero;
        if (Campaign.Current == null ||
            target == null ||
            target == Hero.MainHero ||
            Clan.PlayerClan == null ||
            !TorFamilySafety.IsVampire(Hero.MainHero))
        {
            return false;
        }

        if (!target.IsAlive ||
            !target.IsActive ||
            target.IsTemplate ||
            target.IsChild ||
            target.Clan == Clan.PlayerClan ||
            IsTargetForeignClanLeader())
        {
            return false;
        }

        return TorProfessionEffectBridge.IsMinorVampireIdentity(target);
    }

    private void ClaimExistingBloodProgeny()
    {
        var target = Hero.OneToOneConversationHero;
        if (!CanClaimExistingBloodProgeny() || target == null)
        {
            KaiRuntimeLog.Write(
                "BLOOD_KISS_RECOVERY_BLOCKED",
                $"target={target?.StringId ?? "null"}; reason=not_minor_vampire_or_ineligible");
            return;
        }

        var oldParty = target.PartyBelongedTo;
        var wasPartyLeader = oldParty?.LeaderHero == target;

        TrackBloodProgeny(target);

        if (!TryBindBloodProgeny(target, oldParty, wasPartyLeader, out var error))
        {
            KaiRuntimeLog.Write(
                "BLOOD_KISS_RECOVERY_FAILED",
                $"target={target.StringId}; error={error}");
            return;
        }

        if (PlayerEncounter.Current != null)
            PlayerEncounter.LeaveEncounter = true;

        KaiRuntimeLog.Write(
            "BLOOD_KISS_RECOVERY_SUCCESS",
            $"target={target.StringId}; clan={target.Clan?.StringId ?? "null"}; party={target.PartyBelongedTo?.StringId ?? "null"}");

        MBInformationManager.AddQuickInformation(
            new TaleWorlds.Localization.TextObject($"{target.Name} признан{(target.IsFemale ? "а" : string.Empty)} частью вашего кровного рода."),
            3000,
            target.CharacterObject,
            null,
            string.Empty);
    }

    private void TrackBloodProgeny(Hero hero)
    {
        if (hero == null)
            return;

        _bloodProgenyIds ??= new List<string>();
        if (!_bloodProgenyIds.Contains(hero.StringId, StringComparer.Ordinal))
            _bloodProgenyIds.Add(hero.StringId);
    }

    private void RepairTrackedBloodProgeny()
    {
        if (_bloodProgenyIds == null || _bloodProgenyIds.Count == 0 || Campaign.Current == null)
            return;

        foreach (var id in _bloodProgenyIds.Distinct(StringComparer.Ordinal).ToArray())
        {
            var hero = Hero.AllAliveHeroes.FirstOrDefault(h =>
                h != null && string.Equals(h.StringId, id, StringComparison.Ordinal));

            if (hero == null || !hero.IsAlive)
                continue;

            if (hero.Clan == Clan.PlayerClan &&
                (hero.Father == Hero.MainHero || hero.Mother == Hero.MainHero))
            {
                continue;
            }

            if (!TorProfessionEffectBridge.IsMinorVampireIdentity(hero))
            {
                KaiRuntimeLog.Write(
                    "BLOOD_KISS_REPAIR_SKIP",
                    $"target={id}; reason=identity_no_longer_minor_vampire");
                continue;
            }

            var oldParty = hero.PartyBelongedTo;
            var wasPartyLeader = oldParty?.LeaderHero == hero;
            if (TryBindBloodProgeny(hero, oldParty, wasPartyLeader, out var error))
            {
                KaiRuntimeLog.Write(
                    "BLOOD_KISS_REPAIR_SUCCESS",
                    $"target={hero.StringId}; clan={hero.Clan?.StringId ?? "null"}; party={hero.PartyBelongedTo?.StringId ?? "null"}");
            }
            else
            {
                KaiRuntimeLog.Write(
                    "BLOOD_KISS_REPAIR_FAILED",
                    $"target={hero.StringId}; error={error}");
            }
        }
    }

    private static bool TryBindBloodProgeny(
        Hero target,
        MobileParty oldParty,
        bool wasPartyLeader,
        out string error)
    {
        error = string.Empty;

        try
        {
            if (target == null ||
                Hero.MainHero == null ||
                Clan.PlayerClan == null ||
                MobileParty.MainParty == null)
            {
                error = "player_or_target_missing";
                return false;
            }

            if (target.IsPrisoner || target.PartyBelongedToAsPrisoner != null)
            {
                error = "target_is_prisoner";
                return false;
            }

            // AdoptHeroAction is Bannerlord's native family operation. Its parent
            // setter does not remove the child from the replaced parent's Children
            // collection, so clean exactly the side we are replacing first.
            if (Hero.MainHero.IsFemale)
            {
                var oldMother = target.Mother;
                if (oldMother != null && oldMother != Hero.MainHero)
                    oldMother.Children.Remove(target);
            }
            else
            {
                var oldFather = target.Father;
                if (oldFather != null && oldFather != Hero.MainHero)
                    oldFather.Children.Remove(target);
            }

            if (!target.IsLord)
                target.SetNewOccupation(Occupation.Lord);

            AdoptHeroAction.Apply(target);
            target.IsKnownToPlayer = true;

            if (target.PartyBelongedTo != MobileParty.MainParty)
                AddHeroToPartyAction.Apply(target, MobileParty.MainParty, false);

            if (wasPartyLeader &&
                oldParty != null &&
                oldParty != MobileParty.MainParty &&
                oldParty.IsActive &&
                !oldParty.IsDisbanding)
            {
                DisbandPartyAction.StartDisband(oldParty);
            }

            var parentLinked = target.Father == Hero.MainHero || target.Mother == Hero.MainHero;
            if (target.Clan != Clan.PlayerClan || !parentLinked || target.PartyBelongedTo != MobileParty.MainParty)
            {
                error =
                    $"postcondition_failed clan={target.Clan?.StringId ?? "null"}; parentLinked={parentLinked}; party={target.PartyBelongedTo?.StringId ?? "null"}";
                return false;
            }

            KaiRuntimeLog.Write(
                "BLOOD_KISS_FAMILY_SUCCESS",
                $"target={target.StringId}; clan={target.Clan.StringId}; parent={(target.Father == Hero.MainHero ? "father" : "mother")}; party={target.PartyBelongedTo.StringId}; oldParty={oldParty?.StringId ?? "null"}; oldPartyLeader={wasPartyLeader}; oldPartyDisbanding={oldParty?.IsDisbanding ?? false}");

            return true;
        }
        catch (Exception ex)
        {
            error = ex.GetBaseException().Message;
            KaiRuntimeLog.Exception(
                "BLOOD_KISS_FAMILY_FAILED",
                ex,
                $"target={target?.StringId ?? "null"}; oldParty={oldParty?.StringId ?? "null"}; oldPartyLeader={wasPartyLeader}");
            return false;
        }
    }

    private void TryRecruitTrollWarrior()
    {
        var targetHero = Hero.OneToOneConversationHero;
        var targetCharacter = CharacterObject.OneToOneConversationCharacter;
        var isHeroRoute = targetHero != null && CanShowBloodKissOption() && IsTargetTroll();
        var isDirectRoute = CanRouteDirectTrollConversation();
        if (!isHeroRoute && !isDirectRoute)
        {
            KaiRuntimeLog.Write(
                "BLOOD_KISS_BLOCKED",
                $"target={targetHero?.StringId ?? targetCharacter?.StringId ?? "null"}; reason=troll_stale_or_ineligible");
            return;
        }

        var mainParty = MobileParty.MainParty;
        var manager = MBObjectManager.Instance;
        var troll = manager?.GetObject<CharacterObject>(TrollWarriorTemplateId);
        if (mainParty == null || troll == null || troll.Race != FaceGen.GetRaceOrDefault("troll"))
        {
            KaiRuntimeLog.Write(
                "BLOOD_KISS_BLOCKED",
                $"target={targetHero?.StringId ?? targetCharacter?.StringId ?? "null"}; reason=troll_template_or_party_missing");
            return;
        }

        // Never transfer the conversation hero or troll-clan graph. The safe outcome
        // is one canonical TOR Troll soldier added through the party roster.
        mainParty.MemberRoster.AddToCounts(troll, 1);
        _cooldownUntilDays = CampaignTime.Now.ToDays + BloodKissCooldownDays;

        KaiRuntimeLog.Write(
            "TROLL_RECRUIT",
            $"source={Hero.MainHero.StringId}; conversationTarget={targetHero?.StringId ?? targetCharacter?.StringId ?? "troop"}; troop={TrollWarriorTemplateId}; cooldown={BloodKissCooldownDays}d");
    }

    private static void LogBlocked(string reason)
    {
        var target = Hero.OneToOneConversationHero;
        KaiRuntimeLog.Write(
            "BLOOD_KISS_BLOCKED",
            $"source={Hero.MainHero?.StringId ?? "null"}; target={target?.StringId ?? "null"}; reason={reason}");
    }
}
