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

# The newer co-op result pipeline split result commits into methods that 1.3.15 does not expose.
# Battles are a later KaiTOR milestone, so retain XP as the only directly available commit phase while
# keeping the clan-safety helper for callers/tests.
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
        party => party.CommitXpGain()
    };

    internal static bool CanCommitRenownChanges(Hero leaderHero) =>
        leaderHero == null || leaderHero.Clan != null;
'@

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

# The public helper used by the newer siege aftermath implementation does not exist in 1.3.15.
# Keep an empty contribution table during the 0.0.1 shared-map bootstrap; real siege result semantics
# are restored when the battle milestone is backported.
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
        // Bannerlord 1.3.15 exposes no equivalent public contribution helper.
        var contributions = new Dictionary<MobileParty, float>();
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

    <!-- Companion/party-role APIs changed substantially after 1.3.15. -->
    <Compile Remove="Services\Companions\**\*.cs" />
    <Compile Remove="Services\Armies\Patches\ArmyManagementCalculationPatches.cs" />
    <Compile Remove="Services\Armies\Patches\ArmyDialogPatches.cs" />
    <Compile Remove="Services\Armies\Handlers\ArmyFormationPositionHandler.cs" />
    <Compile Remove="Services\MobileParties\Patches\Disable\DisablePartyRolesCampaignBehavior.cs" />
    <Compile Remove="Services\MobileParties\Patches\Disable\DisableMobilePartyTrainingBehavior.cs" />
    <Compile Remove="Services\MobileParties\Patches\PartyRolesPatches.cs" />
    <Compile Remove="Services\MobileParties\Handlers\PartyRolesHandler.cs" />

    <!-- Captivity implementation is not needed for shared-map bootstrap, but its tiny message
         contracts are retained because several core handlers still subscribe/publish them. -->
    <Compile Remove="Services\PlayerCaptivityService\Commands\**\*.cs" />
    <Compile Remove="Services\PlayerCaptivityService\Handlers\**\*.cs" />
    <Compile Remove="Services\PlayerCaptivityService\Patches\**\*.cs" />
    <Compile Remove="Services\PlayerCaptivityService\PlayerCaptivityConfig.cs" />
    <Compile Remove="Services\PlayerCaptivityService\PlayerCaptivityLogger.cs" />
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

    <!-- Battle/encounter result pipeline is a later milestone. Shared utility contracts that are
         also referenced by campaign-map registries stay compiled until a 1.3.15-specific adapter
         proves they must be replaced. -->
    <Compile Remove="Services\MapEventParties\MapEventPartySync.cs" />
    <Compile Remove="Services\MapEventComponents\Handlers\RaidProductionRewardsHandler.cs" />
    <Compile Remove="Services\MapEvents\Interfaces\PlayerEncounterInterface.cs" />
    <Compile Remove="Services\MapEvents\Interfaces\MapEventResultsInterface.cs" />
    <Compile Remove="Services\MapEvents\Handlers\MapEventResultsHandler.cs" />
    <Compile Remove="Services\MapEvents\Handlers\BattleSimulationRunHandler.cs" />
    <Compile Remove="Services\MapEvents\Patches\PlayerEncounterPatches.cs" />
    <Compile Remove="Services\MapEvents\Patches\BattleModeEncounterOptionsPatch.cs" />
    <Compile Remove="Services\MapEventSides\Patches\MapEventSideDestructionPatches.cs" />

    <!-- Battle-only hooks using post-1.3.15 spawn APIs; battle networking is out of scope for 0.0.1. -->
    <Compile Remove="Services\MapEvents\Patches\BattleSpawnDiagnosticPatch.cs" />
    <Compile Remove="Services\MapEvents\Patches\BattleTroopSupplierInjectionPatch.cs" />
    <Compile Remove="Services\MapEvents\Patches\CoopBattleDepletionPatch.cs" />
    <Compile Remove="Services\MapEvents\Patches\CoopEmptyTeamDeploymentPatch.cs" />
    <Compile Remove="Services\MapEvents\Patches\MissionSpawnCapacityPatch.cs" />
    <Compile Remove="Services\MapEventParties\Patches\MapEventPartyPatches.cs" />
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
Write-Host 'Campaign-map core retained; post-1.3.15/battle-only systems are conditionally disabled for 0.0.1.'
