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

# Bannerlord 1.3.15 exposes the spawn phase as MissionAgentSpawnLogic.SpawnPhase.
Replace-Exact `
    'source/Missions/Battles/CoopBattleMissionSpawnHandler.cs' `
    'MissionSpawnPhase' `
    'MissionAgentSpawnLogic.SpawnPhase'

# The later DefaultBattleMissionAgentSpawnLogic type is the 1.4.x split of the concrete
# MissionAgentSpawnLogic that still owns the same three-argument constructor in 1.3.15.
foreach ($path in @(
    'source/Missions/Battles/ReinforcementFielder.cs',
    'source/Missions/Battles/CoopFieldBattleLauncher.cs',
    'source/Missions/Battles/CoopSiegeBattleLauncher.cs',
    'source/Missions/Battles/BattleTeamDiagnostics.cs'
)) {
    Replace-Exact $path 'DefaultBattleMissionAgentSpawnLogic' 'MissionAgentSpawnLogic'
}

# In 1.3.15 the current reinforcement settings are exposed as ReinforcementSpawnSettings.
foreach ($path in @(
    'source/Missions/Battles/CoopBattleMissionSpawnHandler.cs',
    'source/Missions/Battles/ReinforcementFielder.cs'
)) {
    Replace-Exact $path '.SpawnSettings' '.ReinforcementSpawnSettings'
}

# Bannerlord 1.3.15 stores the same reserved-troop counter on MissionAgentSpawnLogic._missionSides
# rather than the later _battleSideSpawnContexts collection. Preserve the real reservation count so
# refreshed lifetime quotas do not double-count already reserved reinforcements.
Replace-Exact `
    'source/Missions/Battles/CoopBattleMissionSpawnHandler.cs' `
    '_missionAgentSpawnLogic._battleSideSpawnContexts[(int)side].ReservedTroopsCount' `
    '_missionAgentSpawnLogic._missionSides[(int)side].ReservedTroopsCount'

# Hideout ambush APIs drifted after 1.3.15. The older IMissionAgentSpawnLogic exposes a
# parameterless GetReinforcementInterval(), and LocateTheMainCampObjective/Objectives namespace
# did not exist yet. Keep the cooperative hideout controller but omit only the newer objective UI.
Replace-Exact `
    'source/Missions/Hideouts/CoopHideoutNativeControllers.cs' `
    'using SandBox.Missions.MissionLogics.Hideout.Objectives;' `
    ''

Replace-Exact `
    'source/Missions/Hideouts/CoopHideoutNativeControllers.cs' `
    '    public float GetReinforcementInterval(BattleSideEnum side = BattleSideEnum.None) => 0f;' `
    '    public float GetReinforcementInterval() => 0f;'

Replace-Exact `
    'source/Missions/Hideouts/CoopHideoutNativeControllers.cs' `
    @'
            _locateTheMainCampObjective = new LocateTheMainCampObjective(Mission);
            _missionObjectiveLogic.StartObjective(_locateTheMainCampObjective);
'@ `
    @'
            // Bannerlord 1.3.15 predates LocateTheMainCampObjective; native hideout state remains authoritative.
'@

# Mission.InitialPlayerAgent/_initialPlayerAgent were introduced after 1.3.15. On 1.3.15,
# MainAgent is the authoritative local player-agent slot. Preserve the same guards and promotion
# behavior using MainAgent, which native deployment already consumes on this branch.
Replace-Exact `
    'source/Missions/Battles/PuppetSpawner.cs' `
    'Mission.Current.InitialPlayerAgent' `
    'Mission.Current.MainAgent'

Replace-Exact `
    'source/Missions/Battles/CoopBattleMissionSpawnHandler.cs' `
    'mission.InitialPlayerAgent' `
    'mission.MainAgent'
Replace-Exact `
    'source/Missions/Battles/BattleAuthorityMigrator.cs' `
    @'
            if (mission.InitialPlayerAgent == null)
                mission._initialPlayerAgent = agent;
'@ `
    @'
            // Bannerlord 1.3.15 has no separate InitialPlayerAgent slot; MainAgent is assigned below.
'@

# 1.3.15 hideout ambush keeps prior allies and boss identity in older fields and builds its enemy
# origin list in the base constructor. Adapt the cooperative wrapper to that exact state shape.
Replace-Exact `
    'source/Missions/Hideouts/CoopHideoutNativeControllers.cs' `
    'using TaleWorlds.CampaignSystem;' `
    "using TaleWorlds.CampaignSystem;`nusing TaleWorlds.CampaignSystem.Roster;"

Replace-Exact `
    'source/Missions/Hideouts/CoopHideoutNativeControllers.cs' `
    @'
    private readonly CoopHideoutMissionLogic coop;
    private bool locationListenerRegistered;

    public CoopHideoutAmbushController(CoopHideoutMissionLogic coop, IMissionTroopSupplier[] suppliers)
        : base(suppliers, BattleSideEnum.Attacker, 0) => this.coop = coop;
'@ `
    @'
    private readonly CoopHideoutMissionLogic coop;
    private readonly IMissionTroopSupplier[] coopSuppliers;
    private bool locationListenerRegistered;

    public CoopHideoutAmbushController(CoopHideoutMissionLogic coop, IMissionTroopSupplier[] suppliers)
        : base(BattleSideEnum.Attacker, new FlattenedTroopRoster())
    {
        this.coop = coop;
        coopSuppliers = suppliers;
    }
'@

Replace-Exact `
    'source/Missions/Hideouts/CoopHideoutNativeControllers.cs' `
    '    public IEnumerable<IAgentOriginBase> GetAllTroopsForSide(BattleSideEnum side) => _suppliers[(int)side].GetAllTroops();' `
    '    public IEnumerable<IAgentOriginBase> GetAllTroopsForSide(BattleSideEnum side) => coopSuppliers[(int)side].GetAllTroops();'

Replace-Exact `
    'source/Missions/Hideouts/CoopHideoutNativeControllers.cs' `
    @'
        _playerTroopCount = coop.Attacker.NumTroopsNotSupplied;
        InitializeTroops();
'@ `
    @'
        // The 1.3.15 base constructor already initialized enemy origins. Cooperative player
        // reserves are supplied by CoopHideoutMissionLogic instead of the native prior-allies list.
'@

Replace-Exact `
    'source/Missions/Hideouts/CoopHideoutNativeControllers.cs' `
    '_overriddenHideoutBossAgentOrigin' `
    '_overriddenHideoutBossCharacterObject'

Replace-Exact `
    'source/Missions/Hideouts/CoopHideoutNativeControllers.cs' `
    '_playerPriorTroops' `
    '_priorAllyTroops'

# The clear-objective tracking collection was added later; 1.3.15 has no equivalent state to mutate.
Replace-Exact `
    'source/Missions/Hideouts/CoopHideoutNativeControllers.cs' `
    @'
        else
            _clearObjectiveTargetAgents.Remove(affectedAgent);
'@ `
    @'
        // 1.3.15 has no _clearObjectiveTargetAgents collection for the main-agent removal path.
'@

# 1.3.15 Mission.SpawnTroop includes forceDismounted immediately before position/direction.
Replace-Exact `
    'source/Missions/Hideouts/CoopHideoutMissionLogic.cs' `
    '            var agent = Mission.SpawnTroop(origin, true, true, false, false, 0, 0, true, true, position, direction);' `
    '            var agent = Mission.SpawnTroop(origin, true, true, false, false, 0, 0, true, true, false, position, direction);'

# SaveLoadVM initialization was made async in 1.4.7. In 1.3.15 the base constructor populates
# the synchronous save groups, so there is no InitializeAsync method to await.
Replace-Exact `
    'source/Missions/View/MissionsLoadUI.cs' `
    '            base.InitializeAsync().GetAwaiter().GetResult();' `
    '            // Bannerlord 1.3.15 SaveLoadVM initializes synchronously in its constructor.'

Write-Host 'Applied KaiTOR Bannerlord 1.3.15 Missions backport.'
