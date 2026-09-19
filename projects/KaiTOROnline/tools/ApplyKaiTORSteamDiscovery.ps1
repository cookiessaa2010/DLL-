param(
    [Parameter(Mandatory = $true)]
    [string]$UpstreamRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = (Resolve-Path -LiteralPath $UpstreamRoot).Path

function Replace-Required {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Old,
        [Parameter(Mandatory = $true)][string]$New,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "KaiTOR Steam discovery target missing: $Path"
    }

    $text = [IO.File]::ReadAllText($Path)
    # Normalize line endings before matching so the patch is stable on Windows runners
    # regardless of checkout autocrlf behavior.
    $text = $text.Replace("`r`n", "`n")
    $oldNormalized = $Old.Replace("`r`n", "`n")
    $newNormalized = $New.Replace("`r`n", "`n")

    if (-not $text.Contains($oldNormalized)) {
        if ($text.Contains($newNormalized)) {
            Write-Host "KaiTOR Steam discovery: $Label already applied."
            return
        }
        throw "KaiTOR Steam discovery anchor missing: $Label"
    }

    $text = $text.Replace($oldNormalized, $newNormalized)
    [IO.File]::WriteAllText($Path, $text, [Text.UTF8Encoding]::new($true))
    Write-Host "KaiTOR Steam discovery: $Label"
}

$connectVm = Join-Path $root 'source/GameInterface/Services/UI/CoopConnectMenuVM.cs'
Replace-Required -Path $connectVm `
    -Old 'public string HostSearchLabelText => KaiTORUiText.Get("kaitor_host_name", "Host Name");' `
    -New 'public string HostSearchLabelText => KaiTORUiText.Get("kaitor_server_name", "Server Name");' `
    -Label 'server-name search label'
Replace-Required -Path $connectVm `
    -Old 'public string HostSearchPlaceholderText => KaiTORUiText.Get("kaitor_host_search_placeholder", "Type a host name...");' `
    -New 'public string HostSearchPlaceholderText => KaiTORUiText.Get("kaitor_server_search_placeholder", "Type a server name...");' `
    -Label 'server-name search placeholder'
Replace-Required -Path $connectVm `
    -Old 'public string HostColumnText => KaiTORUiText.Get("kaitor_host_name", "Host Name");' `
    -New 'public string HostColumnText => KaiTORUiText.Get("kaitor_server_name", "Server Name");' `
    -Label 'server-name lobby column'

$oldTabs = @'
        Tabs = new MBBindingList<CoopConnectionTabVM>
        {
            new CoopConnectionTabVM(DirectTabId, KaiTORUiText.Get("kaitor_direct", "Direct Connection"), SelectTab),
            new CoopConnectionTabVM(SteamLobbiesTabId, KaiTORUiText.Get("kaitor_steam_lobbies", "Steam Lobbies"), SelectTab),
        };
'@
$newTabs = @'
        Tabs = new MBBindingList<CoopConnectionTabVM>
        {
            new CoopConnectionTabVM(SteamLobbiesTabId, KaiTORUiText.Get("kaitor_steam_lobbies", "Steam Lobbies"), SelectTab),
            new CoopConnectionTabVM(DirectTabId, KaiTORUiText.Get("kaitor_direct", "Direct Connection"), SelectTab),
        };
'@
Replace-Required -Path $connectVm -Old $oldTabs -New $newTabs -Label 'Steam Lobbies promoted to the default first tab'

$lobbyItemVm = Join-Path $root 'source/GameInterface/Services/UI/SteamLobbyListItemVM.cs'
Replace-Required -Path $lobbyItemVm `
    -Old 'public string ConnectedPlayersText => ConnectedPlayers.ToString();' `
    -New 'public string ConnectedPlayersText => $"{Math.Min(ConnectedPlayers, 4)} / 4";' `
    -Label 'four-player capacity display'

$oldStatus = 'public string StatusText => IsCompatible ? KaiTORUiText.Get("kaitor_compatible", "Compatible") : KaiTORUiText.Get("kaitor_incompatible", "Incompatible");'
$newStatus = @'
public string StatusText
    {
        get
        {
            string compatibility = IsCompatible
                ? KaiTORUiText.Get("kaitor_compatible", "Compatible")
                : KaiTORUiText.Get("kaitor_incompatible", "Incompatible");
            string version = ModVersion?.Trim() ?? string.Empty;
            int metadataSeparator = version.IndexOf('+');
            if (metadataSeparator > 0) version = version.Substring(0, metadataSeparator);
            if (!string.IsNullOrWhiteSpace(version) && !version.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                version = "v" + version;

            return string.IsNullOrWhiteSpace(version) ? compatibility : $"{version} · {compatibility}";
        }
    }
'@
Replace-Required -Path $lobbyItemVm -Old $oldStatus -New $newStatus -Label 'compact version plus compatibility status'

$advertiser = Join-Path $root 'source/Coop.Steam/SteamLobbyAdvertiser.cs'
$oldAdvertisedName = @'
            if (!lobbyApi.SetLobbyData(
                lobbyId, LobbyDataCodec.OwnerNameKey, lobbyApi.LocalPersonaName ?? string.Empty))
            {
                Logger.Warning("Could not advertise the Steam lobby owner's display name");
            }
'@
$newAdvertisedName = @'
            string kaiTorServerName = Environment.GetEnvironmentVariable("KAITOR_SERVER_NAME");
            string advertisedName = string.IsNullOrWhiteSpace(kaiTorServerName)
                ? lobbyApi.LocalPersonaName ?? string.Empty
                : kaiTorServerName.Trim();
            if (advertisedName.Length > 64) advertisedName = advertisedName.Substring(0, 64);

            if (!lobbyApi.SetLobbyData(
                lobbyId, LobbyDataCodec.OwnerNameKey, advertisedName))
            {
                Logger.Warning("Could not advertise the Steam lobby server display name");
            }
'@
Replace-Required -Path $advertiser -Old $oldAdvertisedName -New $newAdvertisedName -Label 'KaiTOR server name advertised through Steam lobby metadata'

Write-Host 'KaiTOR Steam discovery patch: PASS.'
Write-Host '  Default join surface: Steam Lobbies'
Write-Host '  Server name source: KAITOR_SERVER_NAME (Steam persona fallback)'
Write-Host '  Player display: x / 4'
Write-Host '  Status display: compact mod version + compatibility'
