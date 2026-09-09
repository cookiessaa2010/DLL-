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

# Look for durable module/runtime evidence rather than one exact TaleWorlds log sentence.
# The live TOR acceptance must prove that the complete minimal TOR stack from
# modules.tor-1.3.15.txt reached the campaign process together with Coop.
$requiredEvidence = @(
    @{ Name = 'TOR_Armory'; Pattern = '(?im)\bTOR_Armory(?:\.dll)?\b' },
    @{ Name = 'TOR_Environment'; Pattern = '(?im)\bTOR_Environment(?:\.dll)?\b' },
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
    @{ Name = 'TOR module initialization failure'; Pattern = '(?im)TOR_(?:Armory|Environment|Core)[^\r\n]{0,160}(?:failed|failure|fatal|exception)' },
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

$offsets = [ordered]@{}
foreach ($evidence in $requiredEvidence) {
    $offsets[$evidence.Name] = $text.IndexOf($evidence.Name, [System.StringComparison]::OrdinalIgnoreCase)
}

$expectedOrder = @('TOR_Armory', 'TOR_Environment', 'TOR_Core', 'Coop')
for ($i = 1; $i -lt $expectedOrder.Count; $i++) {
    $previous = $expectedOrder[$i - 1]
    $current = $expectedOrder[$i]
    if ($offsets[$previous] -ge $offsets[$current]) {
        throw "KaiTOR TOR runtime module order invalid: expected $previous before $current."
    }
}

Write-Output "Runtime log: $resolved"
foreach ($evidence in $requiredEvidence) {
    Write-Output "$($evidence.Name) runtime evidence: PASS"
}
Write-Output ("First evidence offsets: TOR_Armory={0}, TOR_Environment={1}, TOR_Core={2}, Coop={3}" -f `
    $offsets.TOR_Armory, $offsets.TOR_Environment, $offsets.TOR_Core, $offsets.Coop)
Write-Output 'TOR runtime module order: PASS'
Write-Output 'KaiTOR Online TOR runtime log acceptance: PASS'
