param(
    [Parameter(Mandatory = $true)]
    [string]$Directory
)

$ErrorActionPreference = 'Stop'

$root = Resolve-Path $Directory
$managed = @()

foreach ($file in Get-ChildItem $root -File -Filter '*.dll') {
    try {
        $name = [Reflection.AssemblyName]::GetAssemblyName($file.FullName)
        $managed += [pscustomobject]@{
            File = $file
            Name = $name.Name
            Version = $name.Version
        }
    }
    catch [BadImageFormatException] {
        Write-Host "Skipping native/non-managed DLL: $($file.Name)"
    }
}

$localByName = @{}
foreach ($entry in $managed) {
    $key = $entry.Name.ToLowerInvariant()
    if ($localByName.ContainsKey($key)) {
        throw "Duplicate managed assembly simple name '$($entry.Name)' in $root"
    }
    $localByName[$key] = $entry
}

# .NET Framework/NuGet dependency graphs routinely package a newer compatible assembly than the
# exact AssemblyRef recorded by a consumer. Treating every exact-version difference as fatal creates
# false positives (for example 9.0.0.0 -> 9.0.0.2 or 4.2.0.1 -> 4.2.1.0). What matters for this
# preflight is a local dependency that is older than the version the consumer was compiled against.
$backLevel = New-Object System.Collections.Generic.List[string]
$forwardDifferences = New-Object System.Collections.Generic.List[string]
foreach ($entry in $managed) {
    try {
        $assembly = [Reflection.Assembly]::LoadFile($entry.File.FullName)
        foreach ($reference in $assembly.GetReferencedAssemblies()) {
            $key = $reference.Name.ToLowerInvariant()
            if (-not $localByName.ContainsKey($key)) { continue }

            $actual = $localByName[$key]
            if ($reference.Version -eq $actual.Version) { continue }

            if ($actual.Version -lt $reference.Version) {
                $backLevel.Add(
                    "$($entry.File.Name) requests $($reference.Name) $($reference.Version), " +
                    "but packaged $($actual.File.Name) is older at $($actual.Version)")
            }
            else {
                $forwardDifferences.Add(
                    "$($entry.File.Name) requests $($reference.Name) $($reference.Version), " +
                    "packaged $($actual.File.Name) is newer at $($actual.Version)")
            }
        }
    }
    catch {
        throw "Failed to inspect managed references in $($entry.File.FullName): $($_.Exception.Message)"
    }
}

if ($forwardDifferences.Count -gt 0) {
    $forwardDifferences | Sort-Object -Unique | ForEach-Object { Write-Host "FORWARD VERSION: $_" }
}

if ($backLevel.Count -gt 0) {
    $backLevel | Sort-Object -Unique | ForEach-Object { Write-Host "BACK-LEVEL DEPENDENCY: $_" }
    throw "Found $($backLevel.Count) packaged managed assembly reference(s) older than their consumers require."
}

Write-Host "Managed dependency audit passed for $($managed.Count) packaged DLL(s) in $root."
Write-Host "Accepted $($forwardDifferences.Count) forward-version reference difference(s); no packaged dependency is back-level."
