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

$mismatches = New-Object System.Collections.Generic.List[string]
foreach ($entry in $managed) {
    try {
        $assembly = [Reflection.Assembly]::LoadFile($entry.File.FullName)
        foreach ($reference in $assembly.GetReferencedAssemblies()) {
            $key = $reference.Name.ToLowerInvariant()
            if (-not $localByName.ContainsKey($key)) { continue }

            $actual = $localByName[$key]
            if ($reference.Version -ne $actual.Version) {
                $mismatches.Add(
                    "$($entry.File.Name) requests $($reference.Name) $($reference.Version), " +
                    "but packaged $($actual.File.Name) is $($actual.Version)")
            }
        }
    }
    catch {
        throw "Failed to inspect managed references in $($entry.File.FullName): $($_.Exception.Message)"
    }
}

if ($mismatches.Count -gt 0) {
    $mismatches | Sort-Object -Unique | ForEach-Object { Write-Host "VERSION MISMATCH: $_" }
    throw "Found $($mismatches.Count) packaged managed-assembly reference version mismatch(es)."
}

Write-Host "Managed dependency audit passed for $($managed.Count) packaged DLL(s) in $root."
