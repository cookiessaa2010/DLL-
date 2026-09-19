using System;
using System.Collections.Generic;
using System.Linq;
using KaiTOR.Diplomacy.Models;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace KaiTOR.Diplomacy.Runtime;

public sealed class KaiLiveTestBehavior : CampaignBehaviorBase
{
    public override void RegisterEvents()
    {
        CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        CampaignEvents.OnGameLoadedEvent.AddNonSerializedListener(this, OnGameLoaded);
        CampaignEvents.OnSaveStartedEvent.AddNonSerializedListener(this, OnSaveStarted);
        CampaignEvents.OnSaveOverEvent.AddNonSerializedListener(this, OnSaveOver);
    }

    public override void SyncData(IDataStore dataStore)
    {
    }

    private static void OnSessionLaunched(CampaignGameStarter starter)
    {
        KaiLiveTestLog.Write("lifecycle", "SESSION_LAUNCHED");
        WriteSnapshot("session_launched");
    }

    private static void OnGameLoaded(CampaignGameStarter starter)
    {
        KaiLiveTestLog.Write("lifecycle", "GAME_LOADED");
        WriteSnapshot("game_loaded");
    }

    private static void OnSaveStarted()
    {
        KaiLiveTestLog.Write("lifecycle", "SAVE_STARTED");
    }

    private static void OnSaveOver(bool success, string saveName)
    {
        KaiLiveTestLog.Write(
            "lifecycle",
            "SAVE_OVER",
            $"success={success}; save={saveName ?? "unknown"}");

        if (success)
            WriteSnapshot("save_over:" + (saveName ?? "unknown"));
    }

    public static string ResetAndSnapshot()
    {
        KaiLiveTestLog.Reset("manual_live_test_start");
        KaiRuntimeLog.Write("LIVE_TEST_RESET", "KaiTOR-LiveTest.log reset by user");
        WriteSnapshot("manual_start");
        return $"KaiTOR live-test log started: {KaiLiveTestLog.FilePath}";
    }

    public static string AppendSnapshot(string reason = "manual")
    {
        WriteSnapshot(reason);
        return $"KaiTOR live-test snapshot appended: {KaiLiveTestLog.FilePath}";
    }

    public static void WriteSnapshot(string reason)
    {
        KaiLiveTestLog.WriteSnapshot(reason, BuildSnapshot());
    }

    public static IEnumerable<string> BuildSnapshot()
    {
        if (Campaign.Current == null)
        {
            yield return "section=meta; campaign=none";
            yield break;
        }

        var hero = Hero.MainHero;
        var playerClan = Clan.PlayerClan;
        var playerKingdom = playerClan?.Kingdom;
        var settlement = Settlement.CurrentSettlement;
        var currentMenu = Campaign.Current.CurrentMenuContext?.GameMenu?.StringId ?? "none";

        yield return
            $"section=meta; campaignDay={CampaignTime.Now.ToDays:0.00}; " +
            $"hero={hero?.StringId ?? "none"}; heroName={hero?.Name}; " +
            $"clan={playerClan?.StringId ?? "none"}; clanName={playerClan?.Name}; " +
            $"kingdom={playerKingdom?.StringId ?? "none"}; kingdomName={playerKingdom?.Name}; " +
            $"settlement={settlement?.StringId ?? "none"}; settlementName={settlement?.Name}; menu={currentMenu}";

        yield return
            $"section=models; lifeDeath={(CampaignOptions.IsLifeDeathCycleDisabled ? "disabled" : "enabled")}; " +
            $"marriage={Campaign.Current.Models.MarriageModel?.GetType().FullName ?? "null"}; " +
            $"pregnancy={Campaign.Current.Models.PregnancyModel?.GetType().FullName ?? "null"}; " +
            $"death={Campaign.Current.Models.HeroDeathProbabilityCalculationModel?.GetType().FullName ?? "null"}";

        foreach (var rule in KaiRaceLifecycle.DescribeRules())
            yield return "section=family_rule; " + rule;

        yield return
            $"section=family_population; dawiAuto={KaiDawiWomenBehavior.AutomaticPopulationEnabled}; " +
            $"vampireAuto={KaiVampirePopulationBehavior.AutomaticPopulationEnabled}; " +
            $"greenskinAuto={KaiGreenskinPopulationBehavior.AutomaticPopulationEnabled}";

        var diplomacy = Campaign.Current.GetCampaignBehavior<KaiDiplomacyBehavior>();
        var family = Campaign.Current.GetCampaignBehavior<KaiFamilyAffairsBehavior>();
        var culture = Campaign.Current.GetCampaignBehavior<KaiCultureAssimilationBehavior>();
        var dynastic = Campaign.Current.GetCampaignBehavior<KaiDynasticMarriageBehavior>();
        var messenger = Campaign.Current.GetCampaignBehavior<KaiMessengerBehavior>();
        var familyDiagnostics = Campaign.Current.GetCampaignBehavior<KaiFamilyDiagnosticsBehavior>();
        var worldMarriage = Campaign.Current.GetCampaignBehavior<KaiWorldMarriageBehavior>();
        var education = Campaign.Current.GetCampaignBehavior<KaiLoreEducationBehavior>();
        var bloodKiss = Campaign.Current.GetCampaignBehavior<KaiBloodKissBehavior>();
        var mercy = Campaign.Current.GetCampaignBehavior<KaiMercyRelationBehavior>();
        var dawi = Campaign.Current.GetCampaignBehavior<KaiDawiWomenBehavior>();
        var vampire = Campaign.Current.GetCampaignBehavior<KaiVampirePopulationBehavior>();
        var greenskin = Campaign.Current.GetCampaignBehavior<KaiGreenskinPopulationBehavior>();
        var realmHouse = Campaign.Current.GetCampaignBehavior<KaiRealmHouseGrowthBehavior>();

        yield return
            $"section=behaviors; diplomacy={State(diplomacy != null, diplomacy?.RuntimeEnabled == true)}; " +
            $"family={Present(family)}; culture={Present(culture)}; dynastic={Present(dynastic)}; " +
            $"messenger={Present(messenger)}; familyDiagnostics={Present(familyDiagnostics)}; worldMarriage={Present(worldMarriage)}; " +
            $"education={Present(education)}; bloodKiss={Present(bloodKiss)}; mercy={Present(mercy)}; " +
            $"dawi={Present(dawi)}; vampire={Present(vampire)}; greenskin={Present(greenskin)}; realmHouse={Present(realmHouse)}";

        yield return
            $"section=ui; familyEntry=town_tavern/adoption; marriageEntry=lord_dialogue; messengerEntry=encyclopedia_hero; " +
            $"{KaiFamilyAffairsBehavior.DescribeUiStatus()}";

        if (diplomacy != null)
        {
            foreach (var line in diplomacy.DescribeActivePacts())
                yield return "section=nap; " + line;

            foreach (var line in diplomacy.DescribeDiplomaticHistory())
                yield return "section=diplomacy_history; " + line;
        }

        if (playerClan != null && dynastic != null)
        {
            foreach (var clan in Clan.All
                         .Where(c => c != null && !c.IsEliminated && c != playerClan)
                         .OrderBy(c => c.StringId, StringComparer.Ordinal))
            {
                if (dynastic.IsDynasticBondActive(playerClan, clan))
                    yield return $"section=dynastic_bond; playerClan={playerClan.StringId}; otherClan={clan.StringId}; otherName={clan.Name}; active=true";
            }
        }

        if (playerClan != null)
        {
            foreach (var member in playerClan.Heroes
                         .Where(h => h != null && h.IsAlive)
                         .OrderBy(h => h.StringId, StringComparer.Ordinal))
            {
                yield return
                    $"section=family_member; hero={member.StringId}; name={member.Name}; age={member.Age:0.0}; " +
                    $"female={member.IsFemale}; lord={member.IsLord}; spouse={member.Spouse?.StringId ?? "none"}; " +
                    $"spouseName={member.Spouse?.Name}";
            }
        }

        if (education != null)
        {
            foreach (var line in education.DescribePlayerClanStatus())
                yield return "section=education; " + line;
        }

        if (dawi != null)
        {
            foreach (var line in dawi.DescribeStatus())
                yield return "section=dawi; " + line;
        }

        if (vampire != null)
        {
            foreach (var line in vampire.DescribeStatus())
                yield return "section=vampire; " + line;
        }

        if (greenskin != null)
        {
            foreach (var line in greenskin.DescribeStatus())
                yield return "section=greenskin; " + line;
        }

        if (realmHouse != null)
        {
            foreach (var line in realmHouse.DescribeStatus())
                yield return "section=realm_house; " + line;
        }

        if (culture != null)
            yield return "section=settlement_culture; " + culture.DescribeCurrentSettlement();

        yield return
            $"section=counts; kingdoms={Kingdom.All.Count(k => k != null && !k.IsEliminated)}; " +
            $"clans={Clan.All.Count(c => c != null && !c.IsEliminated)}; " +
            $"marriedPairs={Hero.AllAliveHeroes.Count(h => h?.Spouse != null && h.Spouse.IsAlive && string.CompareOrdinal(h.StringId, h.Spouse.StringId) < 0)}";
    }

    private static string Present(object value) => value == null ? "missing" : "ok";

    private static string State(bool present, bool enabled)
        => !present ? "missing" : (enabled ? "ok" : "blocked");
}
