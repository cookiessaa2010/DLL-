[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$LogPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not (Test-Path -LiteralPath $LogPath -PathType Leaf)) {
    throw "KaiTOR TOR runtime log not found: $LogPath"
}

$resolved = (Resolve-Path -LiteralPath $LogPath).Path
$text = Get-Content -LiteralPath $resolved -Raw
if ([string]::IsNullOrWhiteSpace($text)) {
    throw "KaiTOR TOR runtime log is empty: $resolved"
}

# This validator intentionally looks for durable module/runtime evidence rather than
# one exact TaleWorlds log sentence. Bannerlord/TOR wording can vary between builds,
# but a usable TOR campaign process must expose both TOR_Core and Coop in its runtime log.
$requiredEvidence = @(
    @{ Name = 'TOR_Core'; Pattern = '(?im)\bTOR_Core(?:\.dll)?\b' },
    @{ Name = 'Coop'; Pattern = '(?im)\bCoop(?:\.Core)?(?:\.dll)?\b' }
)

foreach ($evidence in $requiredEvidence) {
    if ($text -notmatch $evidence.Pattern) {
        throw "KaiTOR TOR runtime evidence missing: $($evidence.Name) was not observed in $resolved"
    }
}

$fatalPatterns = @(
    @{ Name = 'unhandled exception'; Pattern = '(?im)\bunhandled\s+exception\b' },
    @{ Name = 'managed assembly load failure'; Pattern = '(?im)could\s+not\s+load\s+(?:file\s+or\s+)?assembly' },
    @{ Name = 'module load failure'; Pattern = '(?im)(?:failed|failure|error)\s+(?:to\s+)?load\s+(?:the\s+)?module\b' },
    @{ Name = 'TOR module initialization failure'; Pattern = '(?im)TOR_Core[^\r\n]{0,160}(?:failed|failure|fatal|exception)' },
    @{ Name = 'managed type load incompatibility'; Pattern = '(?im)\b(?:System\.)?TypeLoadException\b' },
    @{ Name = 'managed missing method incompatibility'; Pattern = '(?im)\b(?:System\.)?MissingMethodException\b' },
    @{ Name = 'managed missing field incompatibility'; Pattern = '(?im)\b(?:System\.)?MissingFieldException\b' },
    @{ Name = 'managed file load incompatibility'; Pattern = '(?im)\b(?:System\.IO\.)?FileLoadException\b' },
    @{ Name = 'managed bad image incompatibility'; Pattern = '(?im)\b(?:System\.)?BadImageFormatException\b' }
)

foreach ($fatal in $fatalPatterns) {
    if ($text -match $fatal.Pattern) {
        throw "KaiTOR TOR runtime log contains $($fatal.Name); TOR runtime acceptance rejected."
    }
}

$torIndex = $text.IndexOf('TOR_Core', [System.StringComparison]::OrdinalIgnoreCase)
$coopIndex = $text.IndexOf('Coop', [System.StringComparison]::OrdinalIgnoreCase)

Write-Output "Runtime log: $resolved"
Write-Output 'TOR_Core runtime evidence: PASS'
Write-Output 'Coop runtime evidence: PASS'
if ($torIndex -ge 0 -and $coopIndex -ge 0) {
    Write-Output ("First evidence offsets: TOR_Core={0}, Coop={1}" -f $torIndex, $coopIndex)
}
Write-Output 'KaiTOR Online TOR runtime log acceptance: PASS'
