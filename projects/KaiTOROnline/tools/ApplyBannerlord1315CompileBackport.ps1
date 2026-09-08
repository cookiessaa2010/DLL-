param(
    [Parameter(Mandatory = $true)]
    [string]$UpstreamRoot
)

$ErrorActionPreference = 'Stop'

function Replace-Exact {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Old,
        [Parameter(Mandatory = $true)][string]$New
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

# The current Coop development branch contains synchronization/diagnostic patches for APIs that
# were introduced after Bannerlord 1.3.15. They are deliberately disabled for the first 1.3.15
# campaign-map prototype. This is a compile/bootstrap backport, not a claim that these features are
# complete: each removed patch is tracked for a later semantic 1.3.15 implementation.
$removeBlock = @'
  <ItemGroup Condition="'$(KaiTORBannerlord1315)' == 'true'">
    <!-- Post-1.3.15 alliance / trade-agreement hooks. -->
    <Compile Remove="Services\Alliances\AllianceCampaignBehaviorPatches.cs" />
    <Compile Remove="Services\Alliances\DisableAllianceCampaignBehavior.cs" />
    <Compile Remove="Services\Kingdoms\Patches\TradeAgreementsCampaignBehaviorPatches.cs" />

    <!-- Army/party-role hooks whose target methods changed after 1.3.15. -->
    <Compile Remove="Services\Armies\Patches\ArmyManagementCalculationPatches.cs" />
    <Compile Remove="Services\MobileParties\Patches\Disable\DisablePartyRolesCampaignBehavior.cs" />
    <Compile Remove="Services\MobileParties\Patches\PartyRolesPatches.cs" />

    <!-- Campaign/UI hooks whose exact target method does not exist in 1.3.15. -->
    <Compile Remove="Services\PlayerCaptivityService\Patches\PlayerStartCaptivityPatches.cs" />
    <Compile Remove="Services\SiegeEvents\Patches\BesiegerCampEjectExemptionPatch.cs" />
    <Compile Remove="Services\Settlements\Patches\SettlementMenuOverlayVMPatches.cs" />
    <Compile Remove="Services\Characters\Patches\DisableCharacterDevelopmentCampaignBehavior.cs" />
    <Compile Remove="Services\HeroDevelopers\Patches\ResetSkillsPatches.cs" />

    <!-- Battle-only hooks using post-1.3.15 spawn APIs; battle networking is out of scope for 0.0.1. -->
    <Compile Remove="Services\MapEvents\Patches\BattleSpawnDiagnosticPatch.cs" />
    <Compile Remove="Services\MapEvents\Patches\BattleTroopSupplierInjectionPatch.cs" />
    <Compile Remove="Services\MapEvents\Patches\CoopBattleDepletionPatch.cs" />
    <Compile Remove="Services\MapEvents\Patches\CoopEmptyTeamDeploymentPatch.cs" />
    <Compile Remove="Services\MapEvents\Patches\MissionSpawnCapacityPatch.cs" />

    <!-- CommitGoldChanges is post-1.3.15; preserve the rest of map-event work for a later split. -->
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
Write-Host 'Battle/post-1.3.15-only patches are disabled only when KaiTORBannerlord1315=true.'
