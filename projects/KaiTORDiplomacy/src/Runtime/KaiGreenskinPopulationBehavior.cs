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
/// Independent Greenskin spore lifecycle. No marriage/pregnancy is used. New adult
/// Greenskin AI companions are created by HeroCreator with the host clan passed into
/// native initialization, so KaiTOR never writes Hero.Clan directly.
/// </summary>
public sealed class KaiGreenskinPopulationBehavior : CampaignBehaviorBase
{
    public const bool AutomaticPopulationEnabled = true;

    private const string GreenskinCultureId = "aserai";
    private const double SporePressureThreshold = 100d;
    private const double SporeBaseWeeklyGain = 2d;
    private const double SporeFortificationWeeklyGain = 1.25d;
    private const int SporeSpawnCooldownDays = 84;
    private const int MaximumGreenskinAiCompanions = 10;

    private const string PressureSaveKey = "kaitor_greenskin_spore_pressure_v1";
    private const string CooldownSaveKey = "kaitor_greenskin_spore_cooldown_v1";

    private static readonly string[] WandererTemplateIds =
    {
        "tor_wanderer_greenskins_0",
        "tor_wanderer_greenskins_1",
        "tor_wanderer_greenskins_2",
        "tor_wanderer_greenskins_3"
    };

    private Dictionary<string, double> _pressureByKingdom = new();
    private Dictionary<string, double> _cooldownUntilDays = new();

    public override void RegisterEvents()
    {
        CampaignEvents.WeeklyTickEvent.AddNonSerializedListener(this, OnWeeklyTick);
    }

    public override void SyncData(IDataStore dataStore)
    {
        dataStore.SyncData(PressureSaveKey, ref _pressureByKingdom);
        dataStore.SyncData(CooldownSaveKey, ref _cooldownUntilDays);
        _pressureByKingdom ??= new Dictionary<string, double>();
        _cooldownUntilDays ??= new Dictionary<string, double>();
    }

    private void OnWeeklyTick()
    {
        if (!AutomaticPopulationEnabled || Campaign.Current == null)
            return;

        var playerKingdom = Clan.PlayerClan?.Kingdom;
        foreach (var kingdom in Kingdom.All
                     .Where(k => k != null &&
                                 !k.IsEliminated &&
                                 k.Leader != null &&
                                 k != playerKingdom &&
                                 string.Equals(k.Culture?.StringId, GreenskinCultureId, StringComparison.Ordinal))
                     .OrderBy(k => k.StringId, StringComparer.Ordinal))
        {
            AdvanceSpores(kingdom);
        }
    }

    private void AdvanceSpores(Kingdom kingdom)
    {
        var forts = kingdom.Settlements
            .Where(s => s != null && s.IsFortification)
            .OrderBy(s => s.StringId, StringComparer.Ordinal)
            .ToArray();
        if (forts.Length == 0)
        {
            Log("SKIP", kingdom, "reason=no_fortification");
            return;
        }

        var pressure = GetValue(_pressureByKingdom, kingdom.StringId);
        var gain = SporeBaseWeeklyGain + forts.Length * SporeFortificationWeeklyGain;
        pressure = Math.Min(SporePressureThreshold * 2d, pressure + gain);
        _pressureByKingdom[kingdom.StringId] = pressure;
        Log("PRESSURE", kingdom, $"value={pressure:0.0}; gain={gain:0.0}; threshold={SporePressureThreshold:0}");

        if (pressure < SporePressureThreshold)
            return;

        var now = CampaignTime.Now.ToDays;
        var cooldown = GetValue(_cooldownUntilDays, kingdom.StringId);
        if (cooldown > now)
        {
            Log("SKIP", kingdom, $"reason=cooldown; remaining={Math.Ceiling(cooldown - now):0}d");
            return;
        }

        var target = Math.Min(MaximumGreenskinAiCompanions, Math.Max(2, kingdom.Clans.Count(IsNormalNobleClan) * 2));
        var current = CountAiCompanions(kingdom);
        if (current >= target)
        {
            Log("SKIP", kingdom, $"reason=quota; companions={current}; target={target}");
            return;
        }

        if (!TorFamilySafety.CanAddAttribute)
        {
            Log("SPAWN_FAILED", kingdom, "reason=tor_attribute_bridge_unavailable");
            return;
        }

        var hostClan = kingdom.Clans
            .Where(IsNormalNobleClan)
            .OrderBy(c => c.Heroes.Count(h => h != null && h.IsAlive && TorFamilySafety.IsAiCompanion(h)))
            .ThenBy(c => c.StringId, StringComparer.Ordinal)
            .FirstOrDefault();
        if (hostClan == null)
        {
            Log("SPAWN_FAILED", kingdom, "reason=no_host_clan");
            return;
        }

        var settlement = hostClan.Settlements
            .Where(s => s != null && s.IsFortification && !s.IsUnderSiege)
            .OrderBy(s => s.StringId, StringComparer.Ordinal)
            .FirstOrDefault()
            ?? forts.FirstOrDefault(s => !s.IsUnderSiege);
        if (settlement == null)
        {
            Log("SPAWN_FAILED", kingdom, "reason=no_safe_settlement");
            return;
        }

        if (!TrySpawn(hostClan, settlement, out var hero, out var error))
        {
            Log("SPAWN_FAILED", kingdom, $"clan={hostClan.StringId}; settlement={settlement.StringId}; error={error}");
            return;
        }

        _pressureByKingdom[kingdom.StringId] = Math.Max(0d, pressure - SporePressureThreshold);
        _cooldownUntilDays[kingdom.StringId] = now + SporeSpawnCooldownDays;

        Log("SPAWN_SUCCESS", kingdom,
            $"hero={hero.StringId}; clan={hostClan.StringId}; settlement={settlement.StringId}; cooldown={SporeSpawnCooldownDays}d; companionsBefore={current}; target={target}");
        KaiRuntimeLog.Write("GREEN_SKIN_SPAWN", $"kingdom={kingdom.StringId}; hero={hero.StringId}; clan={hostClan.StringId}");
    }

    private static bool TrySpawn(Clan hostClan, Settlement settlement, out Hero hero, out string error)
    {
        hero = null;
        error = string.Empty;

        var manager = MBObjectManager.Instance;
        if (manager == null)
        {
            error = "object_manager_missing";
            return false;
        }

        var dayStamp = Math.Abs((int)CampaignTime.Now.ToDays);
        CharacterObject template = null;
        for (var offset = 0; offset < WandererTemplateIds.Length; offset++)
        {
            var index = (dayStamp + offset) % WandererTemplateIds.Length;
            template = manager.GetObject<CharacterObject>(WandererTemplateIds[index]);
            if (template != null) break;
        }

        if (template == null)
        {
            error = "template_missing";
            return false;
        }

        var adultAge = Campaign.Current.Models.AgeModel.HeroComesOfAge;
        var age = adultAge + 12 + dayStamp % 20;

        // Passing hostClan here is the native ownership transaction. HeroCreator uses
        // HeroInitializationArgs.SetClan(hostClan) internally; no direct Hero.Clan write.
        hero = HeroCreator.CreateSpecialHero(template, settlement, hostClan, null, age);
        if (hero == null)
        {
            error = "create_special_hero_null";
            return false;
        }

        try
        {
            hero.ChangeState(Hero.CharacterStates.Active);
            hero.SetNewOccupation(Occupation.Special);

            if (hero.Clan != hostClan)
            {
                error = "native_clan_postcondition_failed";
                KillCharacterAction.ApplyByRemove(hero);
                hero = null;
                return false;
            }

            if (!TorFamilySafety.TryAddAttribute(hero, "AICompanion") || !TorFamilySafety.IsAiCompanion(hero))
            {
                error = "ai_companion_attribute_failed";
                KillCharacterAction.ApplyByRemove(hero);
                hero = null;
                return false;
            }

            // Keep the companion in a safe fortification until TOR's own
            // TORAICompanionCampaignBehavior attaches it to a suitable lord party.
            EnterSettlementAction.ApplyForCharacterOnly(hero, settlement);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.GetBaseException().Message;
            if (hero != null)
            {
                try { KillCharacterAction.ApplyByRemove(hero); } catch { }
                hero = null;
            }
            return false;
        }
    }

    private static int CountAiCompanions(Kingdom kingdom)
        => kingdom.Clans
            .Where(IsNormalNobleClan)
            .SelectMany(c => c.Heroes)
            .Count(h => h != null &&
                        h.IsAlive &&
                        TorFamilySafety.IsCulture(h, GreenskinCultureId) &&
                        TorFamilySafety.IsAiCompanion(h));

    private static bool IsNormalNobleClan(Clan clan)
        => clan != null &&
           !clan.IsEliminated &&
           !clan.IsBanditFaction &&
           !clan.IsRebelClan &&
           !clan.IsMinorFaction &&
           !clan.IsClanTypeMercenary;

    private static double GetValue(Dictionary<string, double> dictionary, string key)
        => dictionary != null &&
           key != null &&
           dictionary.TryGetValue(key, out var value) &&
           !double.IsNaN(value) &&
           !double.IsInfinity(value)
            ? value
            : 0d;

    private static void Log(string stage, Kingdom kingdom, string details)
        => KaiPopulationLog.Write("greenskin", stage, $"kingdom={kingdom?.StringId ?? "null"}; {details}");
}
