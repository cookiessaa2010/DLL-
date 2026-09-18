param(
    [Parameter(Mandatory = $true)]
    [string]$UpstreamRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = (Resolve-Path -LiteralPath $UpstreamRoot).Path
$projectPath = Join-Path $root 'source/Coop/Coop.csproj'
$propsPath = Join-Path $root 'source/Directory.Build.props'
$templatePath = Join-Path $root 'deploy/SubModule.xml'

foreach ($required in @($projectPath, $propsPath, $templatePath)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Missing upstream metadata input: $required"
    }
}

$project = [IO.File]::ReadAllText($projectPath)
if ($project.Contains('<GameVersion>v1.4.8</GameVersion>')) {
    $project = $project.Replace('<GameVersion>v1.4.8</GameVersion>', '<GameVersion>v1.3.15</GameVersion>')
}
elseif (-not $project.Contains('<GameVersion>v1.3.15</GameVersion>')) {
    throw 'Expected Coop GameVersion anchor not found.'
}
[IO.File]::WriteAllText($projectPath, $project, [Text.UTF8Encoding]::new($false))

$props = [IO.File]::ReadAllText($propsPath)
if ($props.Contains('<CoopVersion>0.1.5</CoopVersion>')) {
    $props = $props.Replace('<CoopVersion>0.1.5</CoopVersion>', '<CoopVersion>1.3.15.10</CoopVersion>')
}
elseif (-not $props.Contains('<CoopVersion>1.3.15.10</CoopVersion>')) {
    throw 'Expected CoopVersion anchor not found.'
}
[IO.File]::WriteAllText($propsPath, $props, [Text.UTF8Encoding]::new($false))

$template = [IO.File]::ReadAllText($templatePath)
$template = $template.Replace('<Name value="Coop"/>', '<Name value="KaiTOR Co-op"/>')

$story = '        <DependedModule Id="StoryMode" DependentVersion="${game_version}"/>'
$torDeps = @'
        <DependedModule Id="StoryMode" DependentVersion="${game_version}"/>
        <DependedModule Id="TOR_Armory" DependentVersion="${game_version}"/>
        <DependedModule Id="TOR_Environment" DependentVersion="${game_version}"/>
        <DependedModule Id="TOR_Core" DependentVersion="${game_version}"/>
'@
if (-not $template.Contains('Id="TOR_Core"')) {
    if (-not $template.Contains($story)) {
        throw 'Expected StoryMode dependency anchor not found in deploy/SubModule.xml.'
    }
    $template = $template.Replace($story, $torDeps.TrimEnd())
}
[IO.File]::WriteAllText($templatePath, $template, [Text.UTF8Encoding]::new($false))

Write-Host 'KaiTOR module metadata locked:'
Write-Host '  Display name: KaiTOR Co-op'
Write-Host '  Module Id:    Coop'
Write-Host '  Version:      v1.3.15.10'
Write-Host '  Game/TOR:     v1.3.15'
Write-Host '  TOR deps:     TOR_Armory -> TOR_Environment -> TOR_Core'
