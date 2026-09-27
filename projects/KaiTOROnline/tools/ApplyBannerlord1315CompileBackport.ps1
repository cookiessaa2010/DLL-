param(
    [Parameter(Mandatory = $true)]
    [string]$UpstreamRoot
)

$ErrorActionPreference = 'Stop'

function Replace-Exact {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Old,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$New
    )

    $fullPath = Join-Path $UpstreamRoot $Path
    if (-not (Test-Path $fullPath)) {
        throw "Missing upstream file: $Path"
    }

    $text = [IO.File]::ReadAllText($fullPath) -replace "`r`n", "`n"
    $oldNormalized = $Old -replace "`r`n", "`n"
    $newNormalized = $New -replace "`r`n", "`n"

    if (-not $text.Contains($oldNormalized)) {
        throw "Anchor not found in $Path`n--- anchor ---`n$oldNormalized"
    }

    [IO.File]::WriteAllText(
        $fullPath,
        $text.Replace($oldNormalized, $newNormalized),
        [Text.UTF8Encoding]::new($false))
}

# Bannerlord 1.3.15's IMapEventVisual has an extra battleSizeValue argument.
Replace-Exact `
    'source/GameInterface/Services/MapEvents/MapEventBattleFactory.cs' `
    '        public void Initialize(CampaignVec2 position, bool isVisible) { }' `
    '        public void Initialize(CampaignVec2 position, int battleSizeValue, bool isVisible) { }'

Replace-Exact `
    'source/GameInterface/Services/MapEvents/Initialization/MapEventInitializationBarrier.cs' `
    '        using (new AllowedThread()) visual.Initialize(position, visual.MapEvent.IsVisible);' `
    '        using (new AllowedThread()) visual.Initialize(position, visual.MapEvent.GetBattleSizeValue(), visual.MapEvent.IsVisible);'

# 1.3.15 has the four classic party roles only. FirstMate/Navigator are later naval roles.
Replace-Exact `
    'source/GameInterface/Services/MobileParties/MobilePartySync.cs' `
    @'
        autoSyncBuilder.AddProperty(AccessTools.Property(typeof(MobileParty), nameof(MobileParty.Surgeon)));
        autoSyncBuilder.AddProperty(AccessTools.Property(typeof(MobileParty), nameof(MobileParty.FirstMate)));
        autoSyncBuilder.AddProperty(AccessTools.Property(typeof(MobileParty), nameof(MobileParty.Navigator)));
        autoSyncBuilder.AddProperty(AccessTools.Property(typeof(MobileParty), nameof(MobileParty.PartyTradeTaxGold)));
'@ `
    @'
        autoSyncBuilder.AddProperty(AccessTools.Property(typeof(MobileParty), nameof(MobileParty.Surgeon)));
        // Bannerlord 1.3.15 has no FirstMate/Navigator party roles.
        autoSyncBuilder.AddProperty(AccessTools.Property(typeof(MobileParty), nameof(MobileParty.PartyTradeTaxGold)));
'@

# Preserve protobuf field 7 for wire compatibility, but 1.3.15 NauticalInformation has no
# UsesNavalSimulatedWater native field. The surrogate therefore sends/accepts the default value.
Replace-Exact `
    'source/GameInterface/Surrogates/AtmosphereInfoSurrogate.cs' `
    '        UsesNavalSimulatedWater = n.UsesNavalSimulatedWater;' `
    '        UsesNavalSimulatedWater = 0;'

Replace-Exact `
    'source/GameInterface/Surrogates/AtmosphereInfoSurrogate.cs' `
    @'
        IsRiverBattle = s.IsRiverBattle,
        IsInsideStorm = s.IsInsideStorm,
        UsesNavalSimulatedWater = s.UsesNavalSimulatedWater,
'@ `
    @'
        IsRiverBattle = s.IsRiverBattle,
        IsInsideStorm = s.IsInsideStorm,
'@

# Kingdom 1.3.15 exposes fief + settlement caches but no separate _townsCache.
Replace-Exact `
    'source/GameInterface/Services/Kingdoms/KingdomSync.cs' `
    '            autoSyncBuilder.AddField(AccessTools.Field(typeof(Kingdom), nameof(Kingdom._townsCache)));' `
    '            // Bannerlord 1.3.15 has no separate Kingdom._townsCache.'

Replace-Exact `
    'source/GameInterface/Services/Kingdoms/KingdomRegistry.cs' `
    '        obj._townsCache ??= new MBList<Town>();' `
    '        // Bannerlord 1.3.15 has no separate Kingdom._townsCache.'

Replace-Exact `
    'source/GameInterface/Services/Kingdoms/KingdomCollectionSync.cs' `
    '        Republish(kingdom, nameof(Kingdom._townsCache), kingdom._townsCache);' `
    '        // Bannerlord 1.3.15 has no separate Kingdom._townsCache.'

Replace-Exact `
    'source/GameInterface/Services/Kingdoms/KingdomCollectionSync.cs' `
    @'
        Add(kingdom, nameof(Kingdom._fiefsCache), ref kingdom._fiefsCache, town, publish);
        if (IsTown(town))
        {
            AddTown(kingdom, town, publish);
        }

        var settlement = GetSettlement(town);
'@ `
    @'
        Add(kingdom, nameof(Kingdom._fiefsCache), ref kingdom._fiefsCache, town, publish);

        var settlement = GetSettlement(town);
'@

Replace-Exact `
    'source/GameInterface/Services/Kingdoms/KingdomCollectionSync.cs' `
    @'
        Remove(kingdom, nameof(Kingdom._fiefsCache), kingdom._fiefsCache, town, publish);
        if (IsTown(town))
        {
            RemoveTown(kingdom, town, publish);
        }

        var settlement = GetSettlement(town);
'@ `
    @'
        Remove(kingdom, nameof(Kingdom._fiefsCache), kingdom._fiefsCache, town, publish);

        var settlement = GetSettlement(town);
'@

Replace-Exact `
    'source/GameInterface/Services/Kingdoms/KingdomCollectionSync.cs' `
    @'
    public static void AddTown(Kingdom kingdom, Town town, bool publish) =>
        Add(kingdom, nameof(Kingdom._townsCache), ref kingdom._townsCache, town, publish);

    public static void RemoveTown(Kingdom kingdom, Town town, bool publish) =>
        Remove(kingdom, nameof(Kingdom._townsCache), kingdom._townsCache, town, publish);

'@ `
    ''

# Trade agreements were introduced after 1.3.15. Keep the native tribute/call-to-war income path,
# but omit only the two trade-agreement members that do not exist on the 1.3.15 finance model.
Replace-Exact `
    'source/GameInterface/Services/Clans/Interfaces/DefaultClanFinanceModelInterface.cs' `
    @'
        if (!clan.IsUnderMercenaryService)
        {
            model.AddIncomeFromTribute(clan, ref goldChange, applyWithdrawals, includeDetails);
            model.AddIncomeFromCallToWarAgrements(clan, ref goldChange, applyWithdrawals);
            if (clan.Kingdom != null && model.TradeAgreementsBehavior != null)
            {
                model.AddIncomeFromTradeAgreements(clan, ref goldChange, applyWithdrawals, includeDetails);
            }
        }
'@ `
    @'
        if (!clan.IsUnderMercenaryService)
        {
            model.AddIncomeFromTribute(clan, ref goldChange, applyWithdrawals, includeDetails);
            model.AddIncomeFromCallToWarAgrements(clan, ref goldChange, applyWithdrawals);
        }
'@

# Bannerlord 1.3.15 commits battle rewards through MapEventSide rather than the later
# per-MapEventParty Commit* convenience methods. Preserve upstream Coop's phase-by-phase safety
# loop, but backport each phase to the exact 1.3.15 semantics instead of dropping rewards.
Replace-Exact `
    'source/GameInterface/Services/MapEvents/Patches/MapEventPatches.cs' `
    'using TaleWorlds.CampaignSystem;' `
    "using TaleWorlds.CampaignSystem;`nusing TaleWorlds.CampaignSystem.Actions;"

Replace-Exact `
    'source/GameInterface/Services/MapEvents/Patches/MapEventPatches.cs' `
    @'
    private static readonly Action<MapEventParty>[] CommitResultPhases =
    {
        party => party.CommitXpGain(),
        CommitRenownChanges,
        party => party.CommitInfluenceChanges(),
        party => party.CommitMoraleChanges(),
        party => party.CommitGoldChanges()
    };

    private readonly record struct MapEventRewardState(List<RemovedMapEventParty> RemovedParties, float[] StrengthOfSide, float[] RenownValues, float[] InfluenceValues);

    private static void CommitRenownChanges(MapEventParty party)
    {
        Hero leaderHero = party.Party.LeaderHero;
        if (CanCommitRenownChanges(leaderHero))
        {
            party.CommitRenownChanges();
            return;
        }

        if (party.GainedRenown <= 0f)
            return;

        Logger.Error(
            "Skipped {Renown} renown for map event party {PartyId} because leader hero {HeroId} has no clan",
            party.GainedRenown,
            party.Party.Id,
            leaderHero.StringId);
    }

    internal static bool CanCommitRenownChanges(Hero leaderHero) =>
        leaderHero == null || leaderHero.Clan != null;
'@ `
    @'
    private static readonly Action<MapEventParty>[] CommitResultPhases =
    {
        party => party.CommitXpGain(),
        CommitRenownChanges,
        CommitInfluenceChanges,
        CommitMoraleChanges,
        CommitGoldChanges
    };

    private readonly record struct MapEventRewardState(List<RemovedMapEventParty> RemovedParties, float[] StrengthOfSide, float[] RenownValues, float[] InfluenceValues);

    private static void CommitRenownChanges(MapEventParty party)
    {
        Hero leaderHero = party.Party.LeaderHero;
        if (leaderHero == null)
            return;

        if (!CanCommitRenownChanges(leaderHero))
        {
            if (party.GainedRenown > 0f)
            {
                Logger.Error(
                    "Skipped {Renown} renown for map event party {PartyId} because leader hero {HeroId} has no clan",
                    party.GainedRenown,
                    party.Party.Id,
                    leaderHero.StringId);
            }
            return;
        }

        if (party.GainedRenown > 0.001f)
            GainRenownAction.Apply(leaderHero, party.GainedRenown, true);
    }

    private static void CommitInfluenceChanges(MapEventParty party)
    {
        Hero leaderHero = party.Party.LeaderHero;
        if (leaderHero != null && party.GainedInfluence > 0.001f)
            GainKingdomInfluenceAction.ApplyForBattle(leaderHero, party.GainedInfluence);
    }

    private static void CommitMoraleChanges(MapEventParty party)
    {
        if (party.Party.MobileParty != null)
            party.Party.MobileParty.RecentEventsMorale += party.MoraleChange;
    }

    private static void CommitGoldChanges(MapEventParty party)
    {
        var partyBase = party.Party;
        Hero leaderHero = partyBase.LeaderHero;
        if (leaderHero != null)
        {
            if (party.PlunderedGold > 0 && leaderHero.IsPlayerHero())
                MessageBroker.Instance.Publish(party, new NotifyGoldPlundered(leaderHero, party.PlunderedGold));

            if (party.PlunderedGold > 0)
                GiveGoldAction.ApplyBetweenCharacters(null, leaderHero, party.PlunderedGold, true);

            if (party.GoldLost > 0)
                GiveGoldAction.ApplyBetweenCharacters(leaderHero, null, party.GoldLost, true);

            return;
        }

        if (partyBase.IsMobile && partyBase.MobileParty.IsPartyTradeActive)
        {
            partyBase.MobileParty.PartyTradeGold -= party.GoldLost;
            partyBase.MobileParty.PartyTradeGold += party.PlunderedGold;
        }
    }

    internal static bool CanCommitRenownChanges(Hero leaderHero) =>
        leaderHero == null || leaderHero.Clan != null;
'@

# The 1.3.15 MapEventSide equivalent has the older name and calculates the same side
# RenownValue/InfluenceValue inputs after retreated parties have been temporarily removed.
Replace-Exact `
    'source/GameInterface/Services/MapEvents/Patches/MapEventPatches.cs' `
    '            side?.CalculateRenownAndInfluenceValuesOnPartyInvolved(__instance.StrengthOfSide);' `
    '            side?.CalculateRenownAndInfluenceValues(__instance.StrengthOfSide);'

# Voice key UI compatibility: 1.3.15 GameKeyOptionVM takes (GameKey, request, onKeySet)
# and predates ExtraInformationText.
Replace-Exact `
    'source/GameInterface/Services/UI/CoopOptions/Providers/VoiceTab/Sections/VoicePushToTalkKeyVM.cs' `
    @'
            }, null)
'@ `
    @'
            })
'@

Replace-Exact `
    'source/GameInterface/Services/UI/CoopOptions/Providers/VoiceTab/Sections/VoicePushToTalkKeyVM.cs' `
    '        ExtraInformationText = "Controller push-to-talk remains D-pad right.";'`
    '        // Bannerlord 1.3.15 KeyOptionVM has no ExtraInformationText property.'

# Aging/equipment API compatibility. 1.3.15 returns equipment rosters instead of a direct Equipment.
Replace-Exact `
    'source/GameInterface/Services/Heroes/Interfaces/AgingCampaignBehaviorInterface.cs' `
    @'
            Equipment battleEquipment = Campaign.Current.Models.EquipmentSelectionModel.GetEquipmentForHeroComeOfAge(hero, Equipment.EquipmentType.Battle);
            Equipment civilianEquipment = Campaign.Current.Models.EquipmentSelectionModel.GetEquipmentForHeroComeOfAge(hero, Equipment.EquipmentType.Civilian);

            battleEquipment ??= MBEquipmentRosterExtensions.All.Find(x => x.StringId == "generic_bat_dummy").GetBattleEquipments().First<Equipment>();
            civilianEquipment ??= MBEquipmentRosterExtensions.All.Find(x => x.StringId == "generic_civ_dummy").GetCivilianEquipments().First<Equipment>();

            EquipmentHelper.AssignHeroEquipmentFromEquipment(hero, battleEquipment);
            EquipmentHelper.AssignHeroEquipmentFromEquipment(hero, civilianEquipment);
'@ `
    @'
            MBList<MBEquipmentRoster> battleRosters =
                Campaign.Current.Models.EquipmentSelectionModel.GetEquipmentRostersForHeroComeOfAge(hero, false);
            MBList<MBEquipmentRoster> civilianRosters =
                Campaign.Current.Models.EquipmentSelectionModel.GetEquipmentRostersForHeroComeOfAge(hero, true);

            if (battleRosters.IsEmpty<MBEquipmentRoster>())
                battleRosters.Add(MBEquipmentRosterExtensions.All.Find(x => x.StringId == "generic_bat_dummy"));
            if (civilianRosters.IsEmpty<MBEquipmentRoster>())
                civilianRosters.Add(MBEquipmentRosterExtensions.All.Find(x => x.StringId == "generic_civ_dummy"));

            Equipment battleEquipment = battleRosters.GetRandomElement<MBEquipmentRoster>().AllEquipments.GetRandomElement<Equipment>();
            Equipment civilianEquipment = civilianRosters.GetRandomElement<MBEquipmentRoster>().AllEquipments.GetRandomElement<Equipment>();

            EquipmentHelper.AssignHeroEquipmentFromEquipment(hero, battleEquipment);
            EquipmentHelper.AssignHeroEquipmentFromEquipment(hero, civilianEquipment);
'@

Replace-Exact `
    'source/GameInterface/Services/Heroes/Interfaces/AgingCampaignBehaviorInterface.cs' `
    @'
        Equipment equipmentForHeroReachesTeenAge = Campaign.Current.Models.EquipmentSelectionModel.GetEquipmentForHeroReachesTeenAge(hero);
        if (equipmentForHeroReachesTeenAge != null)
        {
            EquipmentHelper.AssignHeroEquipmentFromEquipment(hero, equipmentForHeroReachesTeenAge);
            new Equipment(Equipment.EquipmentType.Battle).FillFrom(equipmentForHeroReachesTeenAge, false);
            EquipmentHelper.AssignHeroEquipmentFromEquipment(hero, equipmentForHeroReachesTeenAge);
        }
'@ `
    @'
        MBEquipmentRoster teenRoster =
            Campaign.Current.Models.EquipmentSelectionModel
                .GetEquipmentRostersForHeroReachesTeenAge(hero)
                .GetRandomElementInefficiently<MBEquipmentRoster>();
        if (teenRoster != null)
        {
            Equipment equipmentForHeroReachesTeenAge =
                teenRoster.GetCivilianEquipments().GetRandomElementInefficiently<Equipment>();
            EquipmentHelper.AssignHeroEquipmentFromEquipment(hero, equipmentForHeroReachesTeenAge);
            new Equipment(Equipment.EquipmentType.Battle).FillFrom(equipmentForHeroReachesTeenAge, false);
            EquipmentHelper.AssignHeroEquipmentFromEquipment(hero, equipmentForHeroReachesTeenAge);
        }
'@

# Player captivity hook changed name/visibility in 1.3.15: the same native capture loop is
# MapEvent.LootDefeatedPartyMembers(MBReadOnlyList<MapEventParty>, MBReadOnlyList<MapEventParty>).
# Harmony may target the private method by name; prefix parameters are unchanged.
Replace-Exact `
    'source/GameInterface/Services/PlayerCaptivityService/Patches/PlayerStartCaptivityPatches.cs' `
    '[HarmonyPatch(typeof(MapEvent), nameof(MapEvent.CaptureDefeatedPartyMembers))]' `
    '[HarmonyPatch(typeof(MapEvent), "LootDefeatedPartyMembers")]'

# ---- Full battle/captivity pipeline compatibility for Bannerlord 1.3.15 ----

# 1.3.15 has no MobileParty.DisembarkToPosition helper. ReleasePlayerFromCaptivity already
# restores the authoritative releasePosition immediately afterwards, so avoid the later helper.
Replace-Exact `
    'source/GameInterface/Services/PlayerCaptivityService/Handlers/PlayerCaptivityServerHandler.cs' `
    '            playerParty.DisembarkToPosition(captorParty.Settlement.GatePosition);' `
    '            // Bannerlord 1.3.15: releasePosition below is the authoritative settlement exit position.'

# WasEverInLootingPhase is a later MapEvent field. 1.3.15 RaidEventComponent carries the actual
# rewards/damage state but no equivalent persistent flag; keep the wire field false and do not write
# a non-existent property on clients.
Replace-Exact `
    'source/GameInterface/Services/MapEventComponents/Handlers/RaidProductionRewardsHandler.cs' `
    '            component.MapEvent?.WasEverInLootingPhase == true,' `
    '            false,'

Replace-Exact `
    'source/GameInterface/Services/MapEventComponents/Handlers/RaidProductionRewardsHandler.cs' `
    @'
                if (data.WasEverInLootingPhase && component.MapEvent != null)
                    component.MapEvent.WasEverInLootingPhase = true;
'@ `
    @'
                // Bannerlord 1.3.15 has no MapEvent.WasEverInLootingPhase state.
'@

# 1.3.15 stores raw reward scalars on MapEventParty.
Replace-Exact `
    'source/GameInterface/Services/MapEventParties/MapEventPartySync.cs' `
    '            autoSyncBuilder.AddProperty(AccessTools.Property(typeof(MapEventParty), nameof(MapEventParty.GainedRenownExplained)));' `
    '            autoSyncBuilder.AddProperty(AccessTools.Property(typeof(MapEventParty), nameof(MapEventParty.GainedRenown)));'
Replace-Exact `
    'source/GameInterface/Services/MapEventParties/MapEventPartySync.cs' `
    '            autoSyncBuilder.AddProperty(AccessTools.Property(typeof(MapEventParty), nameof(MapEventParty.GainedInfluenceExplained)));' `
    '            autoSyncBuilder.AddProperty(AccessTools.Property(typeof(MapEventParty), nameof(MapEventParty.GainedInfluence)));'
Replace-Exact `
    'source/GameInterface/Services/MapEventParties/MapEventPartySync.cs' `
    '            autoSyncBuilder.AddProperty(AccessTools.Property(typeof(MapEventParty), nameof(MapEventParty.GainedMoraleExplained)));' `
    '            autoSyncBuilder.AddProperty(AccessTools.Property(typeof(MapEventParty), nameof(MapEventParty.MoraleChange)));'

# PlayerEncounter's 1.3.15 state machine calls DoLootParty for the LootParty state.
Replace-Exact `
    'source/GameInterface/Services/MapEvents/Interfaces/PlayerEncounterInterface.cs' `
    '                        playerEncounter.DoLootMembersAndPrisonersOfParty();' `
    '                        playerEncounter.DoLootParty();'

# Reward snapshots exposed to UI are ExplainedNumber in Coop but raw floats in 1.3.15.
Replace-Exact `
    'source/GameInterface/Services/MapEvents/Patches/PlayerEncounterPatches.cs' `
    '            renownChange = mapEventParty.GainedRenownExplained;' `
    '            renownChange = new ExplainedNumber(mapEventParty.GainedRenown, false, null);'
Replace-Exact `
    'source/GameInterface/Services/MapEvents/Patches/PlayerEncounterPatches.cs' `
    '            influenceChange = mapEventParty.GainedInfluenceExplained;' `
    '            influenceChange = new ExplainedNumber(mapEventParty.GainedInfluence, false, null);'
Replace-Exact `
    'source/GameInterface/Services/MapEvents/Patches/PlayerEncounterPatches.cs' `
    '            moraleChange = mapEventParty.GainedMoraleExplained;' `
    '            moraleChange = new ExplainedNumber(mapEventParty.MoraleChange, false, null);'

# MapEvent.FindWinnerPartyToGetCurrentLootObjectBasedOnChances is an instance method in 1.3.15.
$resultsPath = Join-Path $UpstreamRoot 'source/GameInterface/Services/MapEvents/Interfaces/MapEventResultsInterface.cs'
$resultsText = [IO.File]::ReadAllText($resultsPath) -replace "`r`n", "`n"
$staticWinnerCall = 'MapEvent.FindWinnerPartyToGetCurrentLootObjectBasedOnChances('
if (-not $resultsText.Contains($staticWinnerCall)) {
    throw 'Expected static winner-party calls not found in MapEventResultsInterface.cs'
}
$resultsText = $resultsText.Replace(
    $staticWinnerCall,
    'mapEvent.FindWinnerPartyToGetCurrentLootObjectBasedOnChances(')


# Four loot helpers did not previously need the MapEvent because newer Bannerlord exposed the
# recipient selector as static. In 1.3.15 it is an instance method, so thread the authoritative
# MapEvent through those helper calls explicitly.
$resultsText = $resultsText.Replace(
    'LootDefeatedPartyCasualties(winnerParties, defeatedParties, winnerPlayerParties, winningSideIncludesPlayers, playerLootData.LootedItems);',
    'LootDefeatedPartyCasualties(mapEvent, winnerParties, defeatedParties, winnerPlayerParties, winningSideIncludesPlayers, playerLootData.LootedItems);')
$resultsText = $resultsText.Replace(
    'LootDefeatedPartyItems(winnerParties, defeatedParties, playerLootData.LootedItems);',
    'LootDefeatedPartyItems(mapEvent, winnerParties, defeatedParties, playerLootData.LootedItems);')
$resultsText = $resultsText.Replace(
    'LootDefeatedPartyPrisoners(winnerParties, defeatedParties, playerLootData.LootedMembers);',
    'LootDefeatedPartyPrisoners(mapEvent, winnerParties, defeatedParties, playerLootData.LootedMembers);')

$resultsText = $resultsText.Replace(
    @'
    private void LootDefeatedPartyCasualties(
        MBReadOnlyList<MapEventParty> winnerParties,
'@ -replace "`r`n", "`n",
    @'
    private void LootDefeatedPartyCasualties(
        MapEvent mapEvent,
        MBReadOnlyList<MapEventParty> winnerParties,
'@ -replace "`r`n", "`n")
$resultsText = $resultsText.Replace(
    @'
    private void LootDefeatedPartyItems(
        MBReadOnlyList<MapEventParty> winnerParties,
'@ -replace "`r`n", "`n",
    @'
    private void LootDefeatedPartyItems(
        MapEvent mapEvent,
        MBReadOnlyList<MapEventParty> winnerParties,
'@ -replace "`r`n", "`n")
$resultsText = $resultsText.Replace(
    @'
    private void LootDefeatedPartyPrisoners(
        MBReadOnlyList<MapEventParty> winnerParties,
'@ -replace "`r`n", "`n",
    @'
    private void LootDefeatedPartyPrisoners(
        MapEvent mapEvent,
        MBReadOnlyList<MapEventParty> winnerParties,
'@ -replace "`r`n", "`n")

$modernCapture = @'
        MBList<KeyValuePair<MapEventParty, float>> woundedCaptureChances;
        MBList<KeyValuePair<MapEventParty, float>> healthyCaptureChances;

        Campaign.Current.Models.BattleRewardModel.GetCaptureMemberChancesForWinnerParties(mapEvent, winnerParties, out woundedCaptureChances, out healthyCaptureChances);
        float playerPartyMemberScatterChance = Campaign.Current.Models.BattleRewardModel.GetMainPartyMemberScatterChance();
'@ -replace "`r`n", "`n"
$legacyCapture = @'
        MBReadOnlyList<KeyValuePair<MapEventParty, float>> memberCaptureChances =
            Campaign.Current.Models.BattleRewardModel.GetLootMemberChancesForWinnerParties(winnerParties);
        MBList<KeyValuePair<MapEventParty, float>> woundedCaptureChances = memberCaptureChances.ToMBList();
        MBList<KeyValuePair<MapEventParty, float>> healthyCaptureChances = memberCaptureChances.ToMBList();
        bool defeatedSideSurrendered = mapEvent.GetMapEventSide(mapEvent.DefeatedSide).IsSurrendered;
        float playerPartyMemberScatterChance = Campaign.Current.Models.BattleRewardModel.GetMainPartyMemberScatterChance();
'@ -replace "`r`n", "`n"
if (-not $resultsText.Contains($modernCapture)) {
    throw 'Modern capture-chance block not found in MapEventResultsInterface.cs'
}
$resultsText = $resultsText.Replace($modernCapture, $legacyCapture)
$resultsText = $resultsText.Replace(
    'if (Campaign.Current.Models.BattleRewardModel.CanTroopBeTakenPrisoner(character))',
    'if (true) // 1.3.15 native captures wounded regular troops without a separate model gate')
$resultsText = $resultsText.Replace(
    'if (healthyCaptureChances.Count > 0)',
    'if (defeatedSideSurrendered && healthyCaptureChances.Count > 0)')
[IO.File]::WriteAllText($resultsPath, $resultsText, [Text.UTF8Encoding]::new($false))

# MapEventPatches now publishes the plunder notification from the 1.3.15 gold adapter. Import the
# existing Coop hero extension and notification contract explicitly.
Replace-Exact `
    'source/GameInterface/Services/MapEvents/Patches/MapEventPatches.cs' `
    'using GameInterface.Services.MapEventParties.Messages;' `
    "using GameInterface.Services.MapEventParties.Messages;`nusing GameInterface.Services.Heroes.Extensions;`nusing GameInterface.Services.UI.Notifications.Messages;"

# Late-join simulation bookkeeping changed after 1.3.15. The old branch has no allocation lock or
# participating-troop setter; MapEventParty.Update + EnqueueTroopSpawnProbabilities is the native path.
Replace-Exact `
    'source/GameInterface/Services/MapEvents/Handlers/BattleSimulationRunHandler.cs' `
    @'
            if (side._troopAllocationsLocked)
                return;

'@ `
    @'
            // Bannerlord 1.3.15 has no _troopAllocationsLocked flag.
'@
Replace-Exact `
    'source/GameInterface/Services/MapEvents/Handlers/BattleSimulationRunHandler.cs' `
    '            joiningParty.SetParticipatingTroopCount(sizeOfParty);' `
    '            // Bannerlord 1.3.15 derives participation from MapEventParty.Update().'
Replace-Exact `
    'source/GameInterface/Services/MapEvents/Handlers/BattleSimulationRunHandler.cs' `
    '            sim.MapEvent.CommitXpGains();' `
    '            side.CommitXpGains();'

# 1.3.15 exposes IsNavalMapEvent but predates MapEventHelper.IsNavalRaid.
Replace-Exact `
    'source/GameInterface/Services/MapEvents/Patches/BattleModeEncounterOptionsPatch.cs' `
    @'
        var isNavalOrder = mapEvent.IsNavalMapEvent ||
                           (MapEventHelper.IsNavalRaid(mapEvent) && mapEvent.PlayerSide == BattleSideEnum.Attacker);
'@ `
    '        var isNavalOrder = mapEvent.IsNavalMapEvent;'

# Native 1.3.15 RemovePartyInternal has no simulation invalidation call.
Replace-Exact `
    'source/GameInterface/Services/MapEventSides/Patches/MapEventSideDestructionPatches.cs' `
    '        __instance.InvalidateSimulationSetup();' `
    '        // Bannerlord 1.3.15 has no InvalidateSimulationSetup().'

# Core battle spawn patches use type names introduced after 1.3.15. The same responsibilities
# live on MissionAgentSpawnLogic and its nested MissionSide in 1.3.15; publicized references expose
# the private engine members these Harmony patches already require.
foreach ($path in @(
    'source/GameInterface/Services/MapEvents/Patches/BattleTroopSupplierInjectionPatch.cs',
    'source/GameInterface/Services/MapEvents/Patches/CoopBattleDepletionPatch.cs',
    'source/GameInterface/Services/MapEvents/Patches/CoopEmptyTeamDeploymentPatch.cs'
)) {
    $battlePatchPath = Join-Path $UpstreamRoot $path
    $battlePatchText = [IO.File]::ReadAllText($battlePatchPath)
    if ($battlePatchText.Contains('DefaultBattleMissionAgentSpawnLogic')) {
        $battlePatchText = $battlePatchText.Replace('DefaultBattleMissionAgentSpawnLogic', 'MissionAgentSpawnLogic')
        [IO.File]::WriteAllText($battlePatchPath, $battlePatchText, [Text.UTF8Encoding]::new($false))
    }
}

foreach ($path in @(
    'source/GameInterface/Services/MapEvents/Patches/BattleSpawnDiagnosticPatch.cs',
    'source/GameInterface/Services/MapEvents/Patches/MissionSpawnCapacityPatch.cs'
)) {
    $battleSidePath = Join-Path $UpstreamRoot $path
    $battleSideText = [IO.File]::ReadAllText($battleSidePath)
    if ($battleSideText.Contains('MissionBattleSideSpawnContext')) {
        $battleSideText = $battleSideText.Replace(
            'MissionBattleSideSpawnContext',
            'MissionAgentSpawnLogic.MissionSide')
        [IO.File]::WriteAllText($battleSidePath, $battleSideText, [Text.UTF8Encoding]::new($false))
    }
}

# ForceSpawnPlayerMounted belongs to the later side-spawn context. 1.3.15's MissionSide has no
# equivalent flag; its native SpawnTroops path uses SpawnWithHorses and forceDismounted=false.
Replace-Exact `
    'source/GameInterface/Services/MapEvents/Patches/MissionSpawnCapacityPatch.cs' `
    '__instance._reservedTroops, __instance.SpawnWithHorses, __instance.ForceSpawnPlayerMounted);' `
    '__instance._reservedTroops, __instance.SpawnWithHorses, false);'

# MapEventParty.CommitGoldChanges did not exist yet in 1.3.15. Gold is committed by our
# 1.3.15 MapEventPatches adapter, so remove only the obsolete Harmony target and preserve the
# plunder notification by publishing it from that adapter before the gold transfer.
Replace-Exact `
    'source/GameInterface/Services/MapEventParties/Patches/MapEventPartyPatches.cs' `
    @'
    [HarmonyPatch(nameof(MapEventParty.CommitGoldChanges))]
    [HarmonyPrefix]
    public static bool CommitGoldChangesPrefix(MapEventParty __instance)
    {
        if (ModInformation.IsClient) return false;

        // Plundering gold is a different message to the regular gold change
        Hero leaderHero = __instance.Party.LeaderHero;
        if (__instance.PlunderedGold > 0 && leaderHero != null && leaderHero.IsPlayerHero())
        {
            MessageBroker.Instance.Publish(__instance, new NotifyGoldPlundered(leaderHero, __instance.PlunderedGold));
        }

        return true;
    }
'@ `
    ''

# 1.3.15 has no explicit simulation-setup invalidation API; leader replacement followed by the native
# leader modifier recache is sufficient for the bootstrap path.
Replace-Exact `
    'source/GameInterface/Services/MapEvents/Patches/MapEventPatches.cs' `
    '            side.InvalidateSimulationSetup();' `
    '            // Bannerlord 1.3.15 has no MapEventSide.InvalidateSimulationSetup().'

# 1.3.15 stores raw map-event reward values rather than the later ExplainedNumber properties.
Replace-Exact `
    'source/GameInterface/Services/MapEvents/MainPartyBattleRewardsCache.cs' `
    @'
        _snapshot = new Snapshot(mapEvent, mapEventParty.GainedRenownExplained, mapEventParty.GainedInfluenceExplained,
            mapEventParty.GainedMoraleExplained, contributionRate);
'@ `
    @'
        _snapshot = new Snapshot(
            mapEvent,
            new ExplainedNumber(mapEventParty.GainedRenown, false, null),
            new ExplainedNumber(mapEventParty.GainedInfluence, false, null),
            new ExplainedNumber(mapEventParty.MoraleChange, false, null),
            contributionRate);
'@

# 1.3.15 computes siege aftermath contribution through MapEvent.GetBattleRewards instead of
# SiegeAftermathCampaignBehavior.GetLootPercentagesOfPartiesOnSideForSiegeAftermath. Mirror the
# native 1.3.15 OnMapEventEnded loop so army gold/morale distribution keeps real contributions.
Replace-Exact `
    'source/GameInterface/Services/SiegeEvents/Patches/SiegeAftermathPatches.cs' `
    @'
        var contributions = new Dictionary<MobileParty, float>();
        foreach (var item in __instance.GetLootPercentagesOfPartiesOnSideForSiegeAftermath(mapEvent, battleSide))
        {
            if (item.Item1.IsMobile && !contributions.ContainsKey(item.Item1.MobileParty))
            {
                contributions.Add(item.Item1.MobileParty, item.Item2);
            }
        }
'@ `
    @'
        var contributions = new Dictionary<MobileParty, float>();
        foreach (MapEventParty mapEventParty in mapEvent.PartiesOnSide(battleSide))
        {
            mapEvent.GetBattleRewards(
                mapEventParty.Party,
                out float _,
                out float __,
                out float ___,
                out float ____,
                out float contribution);

            if (mapEventParty.Party.IsMobile &&
                !contributions.ContainsKey(mapEventParty.Party.MobileParty))
            {
                contributions.Add(mapEventParty.Party.MobileParty, contribution);
            }
        }
'@

# The upstream SDK-style GameInterface project still carries legacy GUID/Name metadata on its
# Common ProjectReference. Newer hosted MSBuild rejects that metadata when the KaiTOR global
# property is supplied. SDK projects do not require it, so normalize the reference for this staged
# backport build.
Replace-Exact `
    'source/GameInterface/GameInterface.csproj' `
    @'
    <ProjectReference Include="..\Common\Common.csproj">
      <Project>{e474a5b5-3f73-46cb-b68c-e8fa5199950b}</Project>
      <Name>Common</Name>
    </ProjectReference>
'@ `
    '    <ProjectReference Include="..\Common\Common.csproj" />'

# 0.0.1 is deliberately a campaign-map/bootstrap milestone. Systems below either target APIs that
# were added after 1.3.15 or are battle/economy/diplomacy surfaces outside the first acceptance test.
# They are conditionally removed only from the staged KaiTOR 1.3.15 build and will be reintroduced
# one subsystem at a time with semantic adapters after the shared-map server is alive.
$removeBlock = @'
  <ItemGroup Condition="'$(KaiTORBannerlord1315)' == 'true'">
    <!-- Post-1.3.15 diplomacy/economy surfaces. -->
    <Compile Remove="Services\Alliances\**\*.cs" />
    <Compile Remove="Services\Kingdoms\Patches\TradeAgreementsCampaignBehaviorPatches.cs" />
    <Compile Remove="Services\Kingdoms\Patches\DefaultKingdomDecisionPermissionModelPatches.cs" />
    <Compile Remove="Services\Kingdoms\Patches\CoopKingdomDecisionProposalBehaviorPatch.cs" />
    <Compile Remove="Services\Kingdoms\Handlers\TradeAgreementsHandler.cs" />
    <Compile Remove="Services\Kingdoms\Commands\KingdomDebugCommand.cs" />
    <Compile Remove="Services\Workshops\Interfaces\WorkshopsCampaignBehaviorInterface.cs" />
    <Compile Remove="Services\Workshops\Handlers\WorkshopWarehouseHandler.cs" />
    <Compile Remove="Services\Workshops\Patches\WorkshopsCampaignBehaviorPatches.cs" />

    <!-- Companion/party-role APIs changed substantially after 1.3.15. -->
    <Compile Remove="Services\Companions\**\*.cs" />
    <Compile Remove="Services\Armies\Patches\ArmyManagementCalculationPatches.cs" />
    <Compile Remove="Services\Armies\Patches\ArmyDialogPatches.cs" />
    <Compile Remove="Services\Armies\Handlers\ArmyFormationPositionHandler.cs" />
    <Compile Remove="Services\MobileParties\Patches\Disable\DisablePartyRolesCampaignBehavior.cs" />
    <Compile Remove="Services\MobileParties\Patches\Disable\DisableMobilePartyTrainingBehavior.cs" />
    <Compile Remove="Services\MobileParties\Patches\PartyRolesPatches.cs" />
    <Compile Remove="Services\MobileParties\Handlers\PartyRolesHandler.cs" />

    <!-- Player captivity is part of the battle lifecycle and remains compiled on 1.3.15. -->
    <Compile Remove="Services\ItemRosters\Patches\AllowItemRostersInGUI.cs" />
    <Compile Remove="Services\Bandits\Patches\BanditInteractionsCampaignBehaviorPatches.cs" />
    <Compile Remove="Services\Inventory\Patches\InventoryLogicPatches.cs" />
    <Compile Remove="Services\HeroDevelopers\Commands\HeroDeveloperCommands.cs" />

    <!-- Campaign/UI hooks whose exact target method does not exist in 1.3.15. -->
    <Compile Remove="Services\SiegeEvents\Patches\BesiegerCampEjectExemptionPatch.cs" />
    <Compile Remove="Services\Settlements\Patches\SettlementMenuOverlayVMPatches.cs" />
    <Compile Remove="Services\Characters\Patches\DisableCharacterDevelopmentCampaignBehavior.cs" />
    <Compile Remove="Services\HeroDevelopers\Patches\ResetSkillsPatches.cs" />
    <Compile Remove="Services\Heroes\Patches\HeroFieldPatches.cs" />

    <!-- Core battle/encounter synchronization is retained on 1.3.15. Any API drift in these
         surfaces must be adapted explicitly above; silently compiling them out hides runtime defects. -->
  </ItemGroup>
'@

$projectPath = 'source/GameInterface/GameInterface.csproj'
$fullProjectPath = Join-Path $UpstreamRoot $projectPath
$projectText = [IO.File]::ReadAllText($fullProjectPath) -replace "`r`n", "`n"

if ($projectText.Contains('KaiTORBannerlord1315')) {
    throw 'GameInterface.csproj already contains the KaiTOR 1.3.15 compile block.'
}

if (-not $projectText.Contains('</Project>')) {
    throw 'GameInterface.csproj closing tag not found.'
}

$projectText = $projectText.Replace('</Project>', "$removeBlock`n</Project>")
[IO.File]::WriteAllText($fullProjectPath, $projectText, [Text.UTF8Encoding]::new($false))

Write-Host 'Applied KaiTOR Bannerlord 1.3.15 GameInterface compile backport.'
Write-Host 'Bannerlord 1.3.15 backport applied with core battle/encounter synchronization retained.'
