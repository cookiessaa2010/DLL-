[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$BannerlordRoot,

    [switch]$Install
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$ExpectedVersion = '1.3.15.110062'
$ProjectRoot = $PSScriptRoot
$ProjectFile = Join-Path $ProjectRoot 'src\KaiTOR_Diplomacy.csproj'
$Manifest = Join-Path $ProjectRoot 'module\SubModule.xml'
$BuildRoot = Join-Path $ProjectRoot 'build'
$ModuleOut = Join-Path $BuildRoot 'Module\KaiTOR_Diplomacy'
$BinOut = Join-Path $ModuleOut 'bin\Win64_Shipping_Client'

$root = (Resolve-Path -LiteralPath $BannerlordRoot).Path
$exe = Join-Path $root 'bin\Win64_Shipping_Client\Bannerlord.exe'
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
    throw "Bannerlord.exe not found: $exe"
}

$versionInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
$reported = @($versionInfo.FileVersion, $versionInfo.ProductVersion) | Where-Object { $_ } | Select-Object -Unique
if (-not ($reported | Where-Object { $_ -like "$ExpectedVersion*" })) {
    throw "KaiTOR Diplomacy targets Bannerlord $ExpectedVersion; found: $($reported -join ', ')"
}

foreach ($required in @(
    'TaleWorlds.CampaignSystem.dll',
    'TaleWorlds.Core.dll',
    'TaleWorlds.Library.dll',
    'TaleWorlds.Localization.dll',
    'TaleWorlds.MountAndBlade.dll'
)) {
    $path = Join-Path $root "bin\Win64_Shipping_Client\$required"
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required Bannerlord assembly missing: $path"
    }
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet SDK was not found in PATH.'
}

Remove-Item -LiteralPath $BuildRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $BinOut -Force | Out-Null

& dotnet build $ProjectFile -c Release "-p:BannerlordRoot=$root" --nologo
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE"
}

$dll = Join-Path $BuildRoot 'bin\Win64_Shipping_Client\KaiTOR_Diplomacy.dll'
if (-not (Test-Path -LiteralPath $dll -PathType Leaf)) {
    throw "Build succeeded but output DLL was not found: $dll"
}

Copy-Item -LiteralPath $Manifest -Destination (Join-Path $ModuleOut 'SubModule.xml') -Force
Copy-Item -LiteralPath $dll -Destination (Join-Path $BinOut 'KaiTOR_Diplomacy.dll') -Force

$pdb = [IO.Path]::ChangeExtension($dll, '.pdb')
if (Test-Path -LiteralPath $pdb -PathType Leaf) {
    Copy-Item -LiteralPath $pdb -Destination (Join-Path $BinOut 'KaiTOR_Diplomacy.pdb') -Force
}

Write-Output 'KaiTOR Diplomacy build: PASS'
Write-Output "  Bannerlord: $ExpectedVersion"
Write-Output "  Module:     $ModuleOut"

if ($Install) {
    $target = Join-Path $root 'Modules\KaiTOR_Diplomacy'
    if (Test-Path -LiteralPath $target) {
        Remove-Item -LiteralPath $target -Recurse -Force
    }
    Copy-Item -LiteralPath $ModuleOut -Destination $target -Recurse -Force
    Write-Output "  Installed:  $target"
}
