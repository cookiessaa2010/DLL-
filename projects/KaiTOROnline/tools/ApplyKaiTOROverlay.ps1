param(
    [Parameter(Mandatory = $true)]
    [string]$UpstreamRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = (Resolve-Path -LiteralPath $UpstreamRoot).Path
$overlayRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\upstream-overlay')).Path

Get-ChildItem -LiteralPath $overlayRoot -Recurse -File | ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($overlayRoot, $_.FullName)
    $target = Join-Path $root $relative
    $parent = Split-Path -Parent $target
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
    Copy-Item -LiteralPath $_.FullName -Destination $target -Force
    Write-Host "KaiTOR overlay: $relative"
}


# Some upstream files live below a class named GameInterface, which shadows the root
# namespace in qualified references. Normalize KaiTOR localization references after copy.
Get-ChildItem -LiteralPath (Join-Path $root 'source') -Recurse -File -Filter '*.cs' | ForEach-Object {
    $text = [IO.File]::ReadAllText($_.FullName)
    $normalized = $text.Replace('GameInterface.Services.UI.KaiTORUiText', 'global::GameInterface.Services.UI.KaiTORUiText')
    if ($normalized -ne $text) {
        [IO.File]::WriteAllText($_.FullName, $normalized, [Text.UTF8Encoding]::new($true))
    }
}

Write-Host 'KaiTOR source/UI/deploy overlay applied.'
