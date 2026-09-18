using System;
using KaiTOR.Diplomacy.Models;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Player-facing Blood Kiss conversation flow. This behavior is independent from the
/// autonomous vampire-population system so it remains available while population AI
/// is safety-off. It never transfers clans or rewires the campaign graph.
/// </summary>
public sealed class KaiBloodKissBehavior : CampaignBehaviorBase
{
    private const int BloodKissCooldownDays = 180;
    private const string CooldownSaveKey = "kaitor_player_blood_kiss_cooldown_v1";
    private const string TrollWarriorTemplateId = "tor_gs_trolls";

    private double _cooldownUntilDays;

    public override void RegisterEvents()
    {
        CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
    }

    public override void SyncData(IDataStore dataStore)
    {
        dataStore.SyncData(CooldownSaveKey, ref _cooldownUntilDays);
        if (double.IsNaN(_cooldownUntilDays) || double.IsInfinity(_cooldownUntilDays) || _cooldownUntilDays < 0d)
            _cooldownUntilDays = 0d;
    }

    private void OnSessionLaunched(CampaignGameStarter starter)
    {
        starter.AddPlayerLine(
            "kaitor_blood_kiss_start",
            "hero_main_options",
            "kaitor_blood_kiss_route",
            "Даровать Поцелуй крови.",
            CanShowBloodKissOption,
            null,
            125,
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
            "Тролль не принимает Поцелуй крови. Но его первобытную волю можно попытаться подчинить и обратить его силу на службу вашему дому.",
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
            "Прими мой дар. Дарую тебе Поцелуй крови.",
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

        KaiRuntimeLog.Write("BLOOD_KISS_ALLOWED", $"source={Hero.MainHero.StringId}; target={target.StringId}; outcome=mortal_conversion");

        if (!TorProfessionEffectBridge.ApplyBloodKissConversion(target, out var error))
        {
            KaiRuntimeLog.Write("BLOOD_KISS_BLOCKED", $"target={target.StringId}; reason=conversion_failed; error={error}");
            return;
        }

        _cooldownUntilDays = CampaignTime.Now.ToDays + BloodKissCooldownDays;
        KaiRuntimeLog.Write(
            "BLOOD_KISS_SUCCESS",
            $"source={Hero.MainHero.StringId}; target={target.StringId}; race={target.CharacterObject?.Race}; career={TorProfessionEffectBridge.GetCurrentCareerId(target) ?? "none"}; cooldown={BloodKissCooldownDays}d");
    }

    private void TryRecruitTrollWarrior()
    {
        var target = Hero.OneToOneConversationHero;
        if (!CanShowBloodKissOption() || target == null || !IsTargetTroll())
        {
            KaiRuntimeLog.Write("BLOOD_KISS_BLOCKED", $"target={target?.StringId ?? "null"}; reason=troll_stale_or_ineligible");
            return;
        }

        var mainParty = MobileParty.MainParty;
        var manager = MBObjectManager.Instance;
        var troll = manager?.GetObject<CharacterObject>(TrollWarriorTemplateId);
        if (mainParty == null || troll == null || troll.Race != FaceGen.GetRaceOrDefault("troll"))
        {
            KaiRuntimeLog.Write("BLOOD_KISS_BLOCKED", $"target={target.StringId}; reason=troll_template_or_party_missing");
            return;
        }

        // The target hero is deliberately NOT moved between clans. Granting one real
        // TOR troll warrior to the player's party fulfills the follower outcome without
        // touching Hero.Clan, Clan.Kingdom, lord parties or succession graphs.
        mainParty.MemberRoster.AddToCounts(troll, 1);
        _cooldownUntilDays = CampaignTime.Now.ToDays + BloodKissCooldownDays;

        KaiRuntimeLog.Write(
            "TROLL_RECRUIT",
            $"source={Hero.MainHero.StringId}; conversationTarget={target.StringId}; troop={TrollWarriorTemplateId}; cooldown={BloodKissCooldownDays}d");
    }

    private static void LogBlocked(string reason)
    {
        var target = Hero.OneToOneConversationHero;
        KaiRuntimeLog.Write(
            "BLOOD_KISS_BLOCKED",
            $"source={Hero.MainHero?.StringId ?? "null"}; target={target?.StringId ?? "null"}; reason={reason}");
    }
}
