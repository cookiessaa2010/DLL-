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

    [switch]$Wait,

    [switch]$NoExitOnWait
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$ExpectedVersion = '1.3.15.110062'
$DefaultModuleList = Join-Path $PSScriptRoot 'modules.vanilla-1.3.15.txt'
$RuntimePreflight = Join-Path $PSScriptRoot 'Test-KaiTORRuntimePreflight.ps1'

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
    if ($Value.Length -gt 0 -and $Value -notmatch '[\s\"]') {
        return $Value
    }

    $quoteChar = [char]34
    $backslashChar = [char]92
    $backslash = [string]$backslashChar
    $builder = [Text.StringBuilder]::new()
    [void]$builder.Append($quoteChar)
    $backslashes = 0

    foreach ($ch in $Value.ToCharArray()) {
        if ($ch -eq $backslashChar) {
            $backslashes++
            continue
        }

        if ($ch -eq $quoteChar) {
            # Backslashes before a literal quote are doubled, plus one escapes the quote.
            [void]$builder.Append(($backslash * (($backslashes * 2) + 1)))
            [void]$builder.Append($quoteChar)
            $backslashes = 0
            continue
        }

        if ($backslashes -gt 0) {
            [void]$builder.Append(($backslash * $backslashes))
            $backslashes = 0
        }
        [void]$builder.Append($ch)
    }

    # Backslashes before the closing quote must be doubled.
    if ($backslashes -gt 0) {
        [void]$builder.Append(($backslash * ($backslashes * 2)))
    }
    [void]$builder.Append($quoteChar)
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
    # Preserve array identity explicitly. Windows PowerShell 5.1 collapses a one-item
    # pipeline result to a scalar, and StrictMode then rejects scalar .Count access.
    $reported = @(
        @($version.FileVersion, $version.ProductVersion) |
            Where-Object { $_ } |
            Select-Object -Unique
    )
    $matches = @($reported | Where-Object { $_ -like "$ExpectedVersion*" })
    if ($matches.Count -eq 0) {
        $shown = if ($reported.Count -gt 0) { $reported -join ', ' } else { '<not reported>' }
        throw "Expected Bannerlord $ExpectedVersion, but Bannerlord.exe reports: $shown. Use -SkipVersionCheck only for diagnostics."
    }
}

if (-not $ModuleIds -or @($ModuleIds).Count -eq 0) {
    $source = if ($ModuleListPath) { $ModuleListPath } else { $DefaultModuleList }
    $ModuleIds = @(Get-ModuleIdsFromFile -Path $source)
}

if (-not ($ModuleIds -contains 'Coop')) {
    throw "The module list must contain 'Coop'."
}

# The launcher must never bypass the stricter runtime contract. This validates the exact
# module set that will be placed in Bannerlord's _MODULES_ token, including Coop metadata
# and the managed runtime assemblies produced by the 1.3.15 backport build.
if (-not (Test-Path -LiteralPath $RuntimePreflight -PathType Leaf)) {
    throw "KaiTOR runtime preflight script is missing: $RuntimePreflight"
}
$preflightArgs = @{
    BannerlordRoot = $root
    ModuleIds = $ModuleIds
    SkipVersionCheck = $SkipVersionCheck
}
$preflightOutput = @(& $RuntimePreflight @preflightArgs)
if (-not ($preflightOutput -match 'KaiTOR Online runtime preflight: PASS')) {
    throw 'KaiTOR runtime preflight did not report PASS; refusing to launch the campaign server.'
}
Write-Output 'Runtime preflight: PASS (launch authorized).'

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

# Never print the password. The safe summary is emitted on the success stream so CI can
# assert the exact launch contract without observing the real secret.
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

Write-Output 'KaiTOR Online campaign-server launch contract'
Write-Output "  Bannerlord: $exe"
Write-Output "  Target:     $ExpectedVersion"
Write-Output "  Save:       $SaveName"
Write-Output "  Visibility: $Visibility"
Write-Output "  Modules:    $($ModuleIds -join ', ')"
Write-Output "  Command:    Bannerlord.exe $safeArguments"
Write-Output '  Coop UDP:   4200 (pinned upstream default; no CLI override in this build)'

if ($DryRun) {
    Write-Output 'DRY RUN: process not started.'
    return
}

$process = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $workingDirectory -PassThru
Write-Output "Started Bannerlord campaign-server process PID $($process.Id)."
Write-Output 'The /coopsave path auto-starts Coop when Bannerlord reaches InitialState and then loads the named save.'

if ($Wait) {
    $process.WaitForExit()
    $exitCode = $process.ExitCode
    Write-Output "Campaign-server process exited with code $exitCode."

    # Wrapper launchers (notably TOR Workshop staging) need their finally blocks to run
    # after the child exits so temporary module links are always cleaned up.
    if ($NoExitOnWait) {
        if ($exitCode -ne 0) {
            throw "Campaign-server process exited with code $exitCode."
        }
        return
    }

    exit $exitCode
}
