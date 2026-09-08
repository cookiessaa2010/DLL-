param(
    [Parameter(Mandatory = $true)]
    [string]$UpstreamRoot
)

$ErrorActionPreference = 'Stop'

$projectPath = Join-Path $UpstreamRoot 'source/Coop/Coop.csproj'
if (-not (Test-Path $projectPath -PathType Leaf)) {
    throw "Missing upstream Coop project: $projectPath"
}

$text = [IO.File]::ReadAllText($projectPath) -replace "`r`n", "`n"
$old = '<GameVersion>v1.4.8</GameVersion>'
$new = '<GameVersion>v1.3.15</GameVersion>'

if (-not $text.Contains($old)) {
    if ($text.Contains($new)) {
        Write-Host 'Coop module metadata is already locked to Bannerlord v1.3.15.'
        exit 0
    }

    throw "Expected upstream GameVersion anchor not found in source/Coop/Coop.csproj"
}

[IO.File]::WriteAllText(
    $projectPath,
    $text.Replace($old, $new),
    [Text.UTF8Encoding]::new($false))

Write-Host 'Locked Coop SubModule generation to Bannerlord v1.3.15.'
