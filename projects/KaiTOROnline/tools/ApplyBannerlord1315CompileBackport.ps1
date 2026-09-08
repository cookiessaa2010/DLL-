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
    <Compile Remove="Services\Clans\Interfaces\DefaultClanFinanceModelInterface.cs" />
    <Compile Remove="Services\Workshops\Interfaces\WorkshopsCampaignBehaviorInterface.cs" />

    <!-- Companion/party-role APIs changed substantially after 1.3.15. -->
    <Compile Remove="Services\Companions\**\*.cs" />
    <Compile Remove="Services\Armies\Patches\ArmyManagementCalculationPatches.cs" />
    <Compile Remove="Services\Armies\Patches\ArmyDialogPatches.cs" />
    <Compile Remove="Services\Armies\Handlers\ArmyFormationPositionHandler.cs" />
    <Compile Remove="Services\MobileParties\Patches\Disable\DisablePartyRolesCampaignBehavior.cs" />
    <Compile Remove="Services\MobileParties\Patches\Disable\DisableMobilePartyTrainingBehavior.cs" />
    <Compile Remove="Services\MobileParties\Patches\PartyRolesPatches.cs" />
    <Compile Remove="Services\MobileParties\Handlers\PartyRolesHandler.cs" />

    <!-- Captivity/loot/UI helpers are not needed to prove four independent parties on the map. -->
    <Compile Remove="Services\PlayerCaptivityService\**\*.cs" />
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

    <!-- Battle/encounter result pipeline is a later milestone. -->
    <Compile Remove="Services\MapEventParties\MapEventPartySync.cs" />
    <Compile Remove="Services\MapEventComponents\Handlers\RaidProductionRewardsHandler.cs" />
    <Compile Remove="Services\MapEvents\MainPartyBattleRewardsCache.cs" />
    <Compile Remove="Services\MapEvents\Interfaces\PlayerEncounterInterface.cs" />
    <Compile Remove="Services\MapEvents\Interfaces\MapEventResultsInterface.cs" />
    <Compile Remove="Services\MapEvents\Handlers\BattleSimulationRunHandler.cs" />
    <Compile Remove="Services\MapEvents\Patches\PlayerEncounterPatches.cs" />
    <Compile Remove="Services\MapEvents\Patches\MapEventPatches.cs" />
    <Compile Remove="Services\MapEvents\Patches\BattleModeEncounterOptionsPatch.cs" />
    <Compile Remove="Services\MapEventSides\Patches\MapEventSideDestructionPatches.cs" />
    <Compile Remove="Services\SiegeEvents\Patches\SiegeAftermathPatches.cs" />

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
