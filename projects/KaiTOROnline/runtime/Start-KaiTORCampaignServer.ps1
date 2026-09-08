[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$BannerlordRoot,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$SaveName,

    [string[]]$ModuleIds,

    [string]$ModuleListPath,

    [ValidateSet('public', 'friends_only', 'none')]
    [string]$Visibility = 'none',

    [ValidateLength(0, 128)]
    [string]$Password = '',

    [switch]$ManagedMode,

    [switch]$SkipVersionCheck,

    [switch]$DryRun,

    [switch]$Wait
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$ExpectedVersion = '1.3.15.110062'
$DefaultModuleList = Join-Path $PSScriptRoot 'modules.vanilla-1.3.15.txt'

function Get-ModuleIdsFromFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Module list not found: $Path"
    }

    $ids = @(
        Get-Content -LiteralPath $Path |
            ForEach-Object { $_.Trim() } |
            Where-Object { $_ -and -not $_.StartsWith('#') }
    )

    if ($ids.Count -eq 0) {
        throw "Module list is empty: $Path"
    }

    return $ids
}

function Quote-WindowsArgument {
    param([AllowEmptyString()][string]$Value)

    # Matches Bannerlord Coop's ServerLaunchArguments quoting contract. Windows parses
    # backslashes specially only when they precede a quote or the closing quote.
    if ($Value.Length -gt 0 -and $Value -notmatch '[\s"]') {
        return $Value
    }

    $builder = [Text.StringBuilder]::new()
    [void]$builder.Append('"'.Substring(1))
    $backslashes = 0

    foreach ($ch in $Value.ToCharArray()) {
        if ($ch -eq '\') {
            $backslashes++
            continue
        }

        if ($ch -eq '"'.Substring(1)) {
            if ($backslashes -gt 0) {
                [void]$builder.Append(('\' * ($backslashes * 2)))
            }
            [void]$builder.Append('\')
            [void]$builder.Append('"'.Substring(1))
            $backslashes = 0
            continue
        }

        if ($backslashes -gt 0) {
            [void]$builder.Append(('\' * $backslashes))
            $backslashes = 0
        }
        [void]$builder.Append($ch)
    }

    if ($backslashes -gt 0) {
        [void]$builder.Append(('\' * ($backslashes * 2)))
    }
    [void]$builder.Append('"'.Substring(1))
    return $builder.ToString()
}

function Build-ModuleToken {
    param([Parameter(Mandatory = $true)][string[]]$Ids)

    $seen = @{}
    $parts = [Collections.Generic.List[string]]::new()
    foreach ($rawId in $Ids) {
        $id = if ($null -eq $rawId) { '' } else { $rawId.Trim() }
        if (-not $id) { throw 'Module ids cannot contain blank entries.' }
        if ($id.Contains('*')) { throw "Invalid module id '$id': '*' is reserved by Bannerlord's module token." }
        if ($seen.ContainsKey($id.ToLowerInvariant())) { throw "Duplicate module id: $id" }
        $seen[$id.ToLowerInvariant()] = $true
        $parts.Add("*$id")
    }

    return '_MODULES_' + ($parts -join '') + '*_MODULES_'
}

$root = (Resolve-Path -LiteralPath $BannerlordRoot).Path
$exe = Join-Path $root 'bin\Win64_Shipping_Client\Bannerlord.exe'
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
    # Also accept callers pointing directly at the Win64_Shipping_Client directory.
    $candidate = Join-Path $root 'Bannerlord.exe'
    if (Test-Path -LiteralPath $candidate -PathType Leaf) {
        $exe = $candidate
        $root = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $exe))
    }
    else {
        throw "Bannerlord.exe not found below '$BannerlordRoot'. Expected bin\Win64_Shipping_Client\Bannerlord.exe."
    }
}

if (-not $SkipVersionCheck) {
    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
    $reported = @($version.FileVersion, $version.ProductVersion) |
        Where-Object { $_ } |
        Select-Object -Unique
    $matches = @($reported | Where-Object { $_ -like "$ExpectedVersion*" })
    if ($matches.Count -eq 0) {
        $shown = if ($reported.Count) { $reported -join ', ' } else { '<not reported>' }
        throw "Expected Bannerlord $ExpectedVersion, but Bannerlord.exe reports: $shown. Use -SkipVersionCheck only for diagnostics."
    }
}

if (-not $ModuleIds -or $ModuleIds.Count -eq 0) {
    $source = if ($ModuleListPath) { $ModuleListPath } else { $DefaultModuleList }
    $ModuleIds = Get-ModuleIdsFromFile -Path $source
}

if (-not ($ModuleIds -contains 'Coop')) {
    throw "The module list must contain 'Coop'."
}

$missingModules = @()
foreach ($id in $ModuleIds) {
    $subModule = Join-Path $root ("Modules\{0}\SubModule.xml" -f $id)
    if (-not (Test-Path -LiteralPath $subModule -PathType Leaf)) {
        $missingModules += $id
    }
}
if ($missingModules.Count -gt 0) {
    throw "Missing module(s) under '$root\Modules': $($missingModules -join ', '). Install/copy them before starting the server."
}

$moduleToken = Build-ModuleToken -Ids $ModuleIds
$tokens = [Collections.Generic.List[string]]::new()
$tokens.Add('/singleplayer')
$tokens.Add('/server')
$tokens.Add($moduleToken)
$tokens.Add('/coopsave')
$tokens.Add($SaveName)

# ManagedMode reproduces the in-game Host launch marker. The pinned Coop code only uses
# OwnerProcessId as a managed-server marker; it does not kill the child when this process exits.
if ($ManagedMode) {
    $tokens.Add('/coopowner')
    $tokens.Add($PID.ToString([Globalization.CultureInfo]::InvariantCulture))
}

$tokens.Add('/coopvisibility')
$tokens.Add($Visibility)
if ($Password) {
    $tokens.Add('/cooppassword')
    $tokens.Add($Password)
}

$arguments = ($tokens | ForEach-Object { Quote-WindowsArgument $_ }) -join ' '
$workingDirectory = Split-Path -Parent $exe

# Never print the password. This is the safe command summary used both interactively and in CI.
$safeTokens = [Collections.Generic.List[string]]::new()
for ($i = 0; $i -lt $tokens.Count; $i++) {
    if ($tokens[$i] -eq '/cooppassword' -and $i + 1 -lt $tokens.Count) {
        $safeTokens.Add('/cooppassword')
        $safeTokens.Add('<redacted>')
        $i++
        continue
    }
    $safeTokens.Add($tokens[$i])
}
$safeArguments = ($safeTokens | ForEach-Object { Quote-WindowsArgument $_ }) -join ' '

Write-Host 'KaiTOR Online campaign-server launch contract'
Write-Host "  Bannerlord: $exe"
Write-Host "  Target:     $ExpectedVersion"
Write-Host "  Save:       $SaveName"
Write-Host "  Visibility: $Visibility"
Write-Host "  Modules:    $($ModuleIds -join ', ')"
Write-Host "  Command:    Bannerlord.exe $safeArguments"
Write-Host '  Coop UDP:   4200 (pinned upstream default; no CLI override in this build)'

if ($DryRun) {
    Write-Host 'DRY RUN: process not started.'
    return
}

$process = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $workingDirectory -PassThru
Write-Host "Started Bannerlord campaign-server process PID $($process.Id)."
Write-Host 'The /coopsave path auto-starts Coop when Bannerlord reaches InitialState and then loads the named save.'

if ($Wait) {
    $process.WaitForExit()
    Write-Host "Campaign-server process exited with code $($process.ExitCode)."
    exit $process.ExitCode
}
