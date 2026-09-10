param(
    [Parameter(Mandatory = $true)]
    [string]$UpstreamRoot
)

$ErrorActionPreference = 'Stop'

$path = Join-Path $UpstreamRoot 'source/GameInterface/Services/Players/PlayerPartyRestorer.cs'
if (-not (Test-Path -LiteralPath $path)) {
    throw "Missing upstream player party restorer: $path"
}

$text = [IO.File]::ReadAllText($path) -replace "`r`n", "`n"

$old = @'
        if (!objectManager.TryGetObjectWithLogging(player.HeroId, out Hero hero)) return false;
        if (hero.Clan == null || hero.CharacterObject == null)
'@ -replace "`r`n", "`n"

$new = @'
        if (!objectManager.TryGetObjectWithLogging(player.HeroId, out Hero hero)) return false;

        // Never fabricate or reactivate a campaign party for a permanently dead player hero.
        // A successor/respawn policy must be an explicit multiplayer transition rather than an
        // accidental side effect of reconnect or save restoration.
        if (hero.IsDead)
        {
            Logger.Warning(
                "Cannot restore player {ControllerId}: hero {HeroId} is dead; successor handling is required",
                player.ControllerId,
                player.HeroId);
            return false;
        }

        if (hero.Clan == null || hero.CharacterObject == null)
'@ -replace "`r`n", "`n"

if (-not $text.Contains($old)) {
    throw "Dead-player recovery anchor not found in PlayerPartyRestorer.cs"
}

$text = $text.Replace($old, $new)
[IO.File]::WriteAllText($path, $text, [Text.UTF8Encoding]::new($false))

Write-Host 'KaiTOR dead-player recovery lifecycle guard applied successfully.'
