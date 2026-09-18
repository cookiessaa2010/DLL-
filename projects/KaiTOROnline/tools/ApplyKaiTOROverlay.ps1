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

Write-Host 'KaiTOR source/UI/deploy overlay applied.'
