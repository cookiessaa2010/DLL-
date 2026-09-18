using Common.Logging;
using Common.Messaging;
using Common.Network;
using Common.Network.Session;
using Common.Network.Session.Messages;
using GameInterface.Services.UI.CoopOptions;
using GameInterface.Services.UI.Donate;
using GameInterface.Services.UI.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json.Serialization;
using Serilog;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ScreenSystem;

namespace GameInterface.Services.UI;
/// <summary>
/// Available password-status filters for hosted steam lobbies
/// </summary>
public enum SteamLobbyPasswordFilter
{
    Any,
    NoPassword,
    PasswordRequired,
}

/// <summary>View model for direct connection and public standalone Steam-lobby discovery.</summary>
public class CoopConnectMenuVM : ViewModel, IDisposable
{
    private static readonly ILogger Logger = LogManager.GetLogger<CoopConnectMenuVM>();

    public const string DirectTabId = "direct";
    public const string SteamLobbiesTabId = "steam_lobbies";
    public const string OptionsTabId = "Connection";
    public const string OptionsSectionId = "DirectConnection";
    public const int SteamLobbyPageSize = 4;

    private const string DefaultServerAddress = "localhost";
    private const int DefaultConnectionPort = 4200;

    public event Action SteamLobbiesTabActivated;

    private readonly ISteamLobbyBrowser steamLobbyBrowser;
    private readonly IMessageBroker messageBroker;
    private readonly ICoopOptionsStore optionsStore;
    private readonly List<SteamLobbyListItemVM> discoveredSteamLobbies = new();

    private CoopConnectionTabVM selectedTab;
    private string steamLobbyHostSearchText = string.Empty;
    private SteamLobbyPasswordFilter steamLobbyPasswordFilter;
    private int minimumSteamLobbyPlayers;
    private string steamLobbyStatusText = string.Empty;
    private bool isRefreshingSteamLobbies;
    private bool disposed;
    private int lobbyRequestGeneration;
    private int filteredSteamLobbyCount;
    private long filteredSteamLobbyPlayerCount;
    private int steamLobbyPageIndex;

    public string JoinButtonText => KaiTORUiText.Get("kaitor_join", "Join");
    public string RefreshButtonText => KaiTORUiText.Get("kaitor_refresh", "Refresh");
    public string SearchButtonText => KaiTORUiText.Get("kaitor_search", "Search");
    public string PreviousPageButtonText => KaiTORUiText.Get("kaitor_previous", "Previous");
    public string NextPageButtonText => KaiTORUiText.Get("kaitor_next", "Next");
    public string DiscordButtonText => KaiTORUiText.Get("kaitor_discord", "Discord");
    public string PatreonButtonText => KaiTORUiText.Get("kaitor_patreon", "Patreon");
    public string DonateButtonText => KaiTORUiText.Get("kaitor_donate", "Donate");
    public string CreditsButtonText => KaiTORUiText.Get("kaitor_credits", "Credits");
    public string MovieTextHeader => KaiTORUiText.Get("kaitor_join_header", "Join KaiTOR Co-op");
    public string BrandSubtitleText => KaiTORUiText.Get("kaitor_brand_subtitle", "Four heroes. One living Old World.");
    public string CommunityText => KaiTORUiText.Get("kaitor_community", "Community");
    public string DirectConnectionHeaderText => KaiTORUiText.Get("kaitor_direct", "Direct Connection");
    public string SteamLobbiesHeaderText => KaiTORUiText.Format(
        "kaitor_hosted_steam_servers",
        "Hosted Steam Servers ({SERVER_COUNT} servers; {PLAYER_COUNT} players)",
        ("SERVER_COUNT", filteredSteamLobbyCount),
        ("PLAYER_COUNT", filteredSteamLobbyPlayerCount));
    public string SteamLobbyPageText => KaiTORUiText.Format(
        "kaitor_page_of",
        "Page {CURRENT_PAGE} of {PAGE_COUNT}",
        ("CURRENT_PAGE", CurrentSteamLobbyPage),
        ("PAGE_COUNT", SteamLobbyPageCount));
    public int CurrentSteamLobbyPage => filteredSteamLobbyCount == 0 ? 0 : steamLobbyPageIndex + 1;
    public int SteamLobbyPageCount => filteredSteamLobbyCount == 0
        ? 0
        : ((filteredSteamLobbyCount - 1) / SteamLobbyPageSize) + 1;
    public string HostSearchLabelText => KaiTORUiText.Get("kaitor_host_name", "Host Name");
    public string HostSearchPlaceholderText => KaiTORUiText.Get("kaitor_host_search_placeholder", "Type a host name...");
    public string PasswordFilterLabelText => KaiTORUiText.Get("kaitor_password", "Password");
    public string MinimumPlayersFilterLabelText => KaiTORUiText.Get("kaitor_minimum_players", "Minimum Players");
    public string HostColumnText => KaiTORUiText.Get("kaitor_host_name", "Host Name");
    public string ConnectedPlayersColumnText => KaiTORUiText.Get("kaitor_connected_players", "Connected Players");
    public string PasswordColumnText => KaiTORUiText.Get("kaitor_access", "Access");
    public string CompatibilityColumnText => KaiTORUiText.Get("kaitor_status", "Status");
    public string IpText => KaiTORUiText.Get("kaitor_server_address", "Server Address:");
    public string PasswordText => KaiTORUiText.Get("kaitor_password_colon", "Password:");

    [DataSourceProperty]
    public HintViewModel ServerAddressHint { get; } = new HintViewModel(KaiTORUiText.Object(
        "kaitor_server_address_hint",
        "The co-op server IP address or host name. Add a custom port after a colon, for example localhost:4300. Port 4200 is used when omitted."));

    [DataSourceProperty]
    public HintViewModel PasswordHint { get; } = new HintViewModel(KaiTORUiText.Object(
        "kaitor_password_hint",
        "The session password set by the host. Leave empty if no password is configured."));

    [DataSourceProperty]
    public string PasswordFilterButtonText => SteamLobbyPasswordFilter switch
    {
        SteamLobbyPasswordFilter.NoPassword => KaiTORUiText.Get("kaitor_no_password", "No Password"),
        SteamLobbyPasswordFilter.PasswordRequired => KaiTORUiText.Get("kaitor_password_required", "Password Required"),
        _ => KaiTORUiText.Get("kaitor_any_password", "Any Password"),
    };

    public string connectIP = DefaultServerAddress;
    public string connectPassword = "";

    public CoopConnectMenuVM()
        : this(SessionDiscovery.SteamLobbyBrowser, MessageBroker.Instance, new CoopOptionsStore())
    {
    }

    public CoopConnectMenuVM(ISteamLobbyBrowser steamLobbyBrowser, IMessageBroker messageBroker)
        : this(steamLobbyBrowser, messageBroker, new CoopOptionsStore())
    {
    }

    public CoopConnectMenuVM(
        ISteamLobbyBrowser steamLobbyBrowser,
        IMessageBroker messageBroker,
        ICoopOptionsStore optionsStore)
    {
        this.steamLobbyBrowser = steamLobbyBrowser;
        this.messageBroker = messageBroker ?? throw new ArgumentNullException(nameof(messageBroker));
        this.optionsStore = optionsStore ?? throw new ArgumentNullException(nameof(optionsStore));
        connectIP = LoadLastServerAddress();

        Tabs = new MBBindingList<CoopConnectionTabVM>
        {
            new CoopConnectionTabVM(DirectTabId, KaiTORUiText.Get("kaitor_direct", "Direct Connection"), SelectTab),
            new CoopConnectionTabVM(SteamLobbiesTabId, KaiTORUiText.Get("kaitor_steam_lobbies", "Steam Lobbies"), SelectTab),
        };
        SteamLobbies = new MBBindingList<SteamLobbyListItemVM>();

        SelectTab(Tabs[0]);
    }

    [DataSourceProperty]
    public MBBindingList<CoopConnectionTabVM> Tabs { get; }

    [DataSourceProperty]
    public MBBindingList<SteamLobbyListItemVM> SteamLobbies { get; }

    [DataSourceProperty]
    public SteamLobbyPasswordFilter SteamLobbyPasswordFilter
    {
        get => steamLobbyPasswordFilter;
        private set
        {
            if (steamLobbyPasswordFilter == value) return;
            steamLobbyPasswordFilter = value;
            OnPropertyChanged(nameof(SteamLobbyPasswordFilter));
            OnPropertyChanged(nameof(PasswordFilterButtonText));

            if (!disposed && !IsRefreshingSteamLobbies)
            {
                ApplySteamLobbyHostFilter(resetPage: true);
            }
        }
    }
    [DataSourceProperty]
    public int MinimumSteamLobbyPlayers
    {
        get => minimumSteamLobbyPlayers;
        set
        {
            value = Math.Max(0, value);
            if (minimumSteamLobbyPlayers == value) return;

            minimumSteamLobbyPlayers = value;
            OnPropertyChanged(nameof(MinimumSteamLobbyPlayers));

            if (!disposed && !IsRefreshingSteamLobbies)
            {
                ApplySteamLobbyHostFilter(resetPage: true);
            }
        }
    }
    [DataSourceProperty]
    public string SteamLobbyHostSearchText
    {
        get => steamLobbyHostSearchText;
        set
        {
            value ??= string.Empty;
            if (steamLobbyHostSearchText == value) return;

            steamLobbyHostSearchText = value;
            OnPropertyChanged(nameof(SteamLobbyHostSearchText));

            if (!disposed && !IsRefreshingSteamLobbies)
            {
                ApplySteamLobbyHostFilter(resetPage: true);
            }
        }
    }

    [DataSourceProperty]
    public CoopConnectionTabVM SelectedTab
    {
        get => selectedTab;
        private set
        {
            if (selectedTab == value) return;

            selectedTab = value;
            OnPropertyChanged(nameof(SelectedTab));
            OnPropertyChanged(nameof(IsDirectTabVisible));
            OnPropertyChanged(nameof(IsSteamLobbiesTabVisible));
        }
    }

    [DataSourceProperty]
    public bool IsDirectTabVisible => SelectedTab?.Id == DirectTabId;

    [DataSourceProperty]
    public bool IsSteamLobbiesTabVisible => SelectedTab?.Id == SteamLobbiesTabId;

    [DataSourceProperty]
    public bool IsRefreshingSteamLobbies
    {
        get => isRefreshingSteamLobbies;
        private set
        {
            if (isRefreshingSteamLobbies == value) return;

            isRefreshingSteamLobbies = value;
            OnPropertyChanged(nameof(IsRefreshingSteamLobbies));
            OnPropertyChanged(nameof(IsRefreshSteamLobbiesDisabled));
            OnPropertyChanged(nameof(IsSearchSteamLobbiesDisabled));
            OnPropertyChanged(nameof(IsPreviousSteamLobbyPageDisabled));
            OnPropertyChanged(nameof(IsNextSteamLobbyPageDisabled));
        }
    }

    [DataSourceProperty]
    public bool IsRefreshSteamLobbiesDisabled => steamLobbyBrowser == null || IsRefreshingSteamLobbies;

    [DataSourceProperty]
    public bool IsSearchSteamLobbiesDisabled => IsRefreshingSteamLobbies;

    [DataSourceProperty]
    public bool IsSteamLobbyPaginationVisible => SteamLobbyPageCount > 1;

    [DataSourceProperty]
    public bool IsPreviousSteamLobbyPageDisabled => IsRefreshingSteamLobbies || steamLobbyPageIndex == 0;

    [DataSourceProperty]
    public bool IsNextSteamLobbyPageDisabled => IsRefreshingSteamLobbies ||
        steamLobbyPageIndex >= SteamLobbyPageCount - 1;

    [DataSourceProperty]
    public string SteamLobbyStatusText
    {
        get => steamLobbyStatusText;
        private set
        {
            value ??= string.Empty;
            if (steamLobbyStatusText == value) return;

            steamLobbyStatusText = value;
            OnPropertyChanged(nameof(SteamLobbyStatusText));
            OnPropertyChanged(nameof(IsSteamLobbyStatusVisible));
        }
    }

    [DataSourceProperty]
    public bool IsSteamLobbyStatusVisible => !string.IsNullOrEmpty(SteamLobbyStatusText);

    [DataSourceProperty]
    public string Ip
    {
        get => connectIP;
        set
        {
            if (value == connectIP) return;

            connectIP = value;
            OnPropertyChanged(nameof(Ip));
        }
    }

    [DataSourceProperty]
    public string Password
    {
        get => connectPassword;
        set
        {
            if (value == connectPassword) return;

            connectPassword = value;
            OnPropertyChanged(nameof(Password));
        }
    }

    public void ActionCycleSteamLobbyPasswordFilter()
    {
        if (disposed || IsRefreshingSteamLobbies) return;

        SteamLobbyPasswordFilter = SteamLobbyPasswordFilter switch
        {
            SteamLobbyPasswordFilter.Any => SteamLobbyPasswordFilter.NoPassword,
            SteamLobbyPasswordFilter.NoPassword => SteamLobbyPasswordFilter.PasswordRequired,
            _ => SteamLobbyPasswordFilter.Any,
        };
    }
    public void ActionRefreshSteamLobbies()
    {
        if (disposed || IsRefreshingSteamLobbies) return;

        discoveredSteamLobbies.Clear();
        ClearSteamLobbyDisplay();

        if (steamLobbyBrowser == null)
        {
            SteamLobbyStatusText = KaiTORUiText.Get("kaitor_lobby_unavailable", "Steam lobby discovery is unavailable.");
            return;
        }

        int generation = ++lobbyRequestGeneration;
        IsRefreshingSteamLobbies = true;
        SteamLobbyStatusText = KaiTORUiText.Get("kaitor_lobby_searching", "Searching for hosted Steam lobbies...");

        try
        {
            steamLobbyBrowser.RequestLobbies(
                (lobbies, error) => CompleteLobbyRefresh(generation, lobbies, error));
        }
        catch (Exception ex)
        {
            CompleteLobbyRefresh(generation, Array.Empty<SteamLobbySummary>(),
                KaiTORUiText.Format("kaitor_lobby_search_failed", "Could not search Steam lobbies: {ERROR}", ("ERROR", ex.Message)));
        }
    }

    public void ActionSearchSteamLobbies()
    {
        if (disposed || IsRefreshingSteamLobbies) return;

        ApplySteamLobbyHostFilter(resetPage: true);
    }

    public void ActionPreviousSteamLobbyPage()
    {
        if (disposed || IsPreviousSteamLobbyPageDisabled) return;

        steamLobbyPageIndex--;
        ApplySteamLobbyHostFilter(resetPage: false);
    }

    public void ActionNextSteamLobbyPage()
    {
        if (disposed || IsNextSteamLobbyPageDisabled) return;

        steamLobbyPageIndex++;
        ApplySteamLobbyHostFilter(resetPage: false);
    }

    public void ActionConnect()
    {
        if (!TryParseServerAddress(connectIP, out var host, out var port))
        {
            InformationManager.DisplayMessage(new InformationMessage(
                KaiTORUiText.Get("kaitor_invalid_server_address", "ERROR: Enter a valid server address with an optional port.")));
            return;
        }

        if (!ConnectionPassword.IsValid(connectPassword))
        {
            InformationManager.DisplayMessage(new InformationMessage(
                KaiTORUiText.Format("kaitor_password_too_long", "ERROR: The password cannot exceed {MAX_LENGTH} characters.", ("MAX_LENGTH", ConnectionPassword.MaxLength))));
            return;
        }

        try
        {
            bool steamInvites = SessionDiscovery.SteamAvailable && IsLoopbackAddress(host);

            IPAddress ip;

            if (IPAddress.TryParse(host, out var enteredIp))
            {
                ip = enteredIp;
            }
            else
            {
                var addresses = Dns.GetHostAddresses(host);
                ip = addresses.FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork);

                if (ip == null)
                {
                    InformationManager.DisplayMessage(new InformationMessage(KaiTORUiText.Get("kaitor_no_ipv4", "ERROR: No IPv4 address found for host.")));
                    return;
                }
            }

            messageBroker.Publish(this, new AttemptJoin(ip, port, connectPassword, steamInvites));
            SaveLastServerAddress();
        }
        catch (Exception ex)
        {
            InformationManager.DisplayMessage(new InformationMessage(
                KaiTORUiText.Format("kaitor_address_resolution_failed", "ERROR: The connection address could not be resolved: {ERROR}", ("ERROR", ex.Message))));
        }
    }

    public void ActionCancel()
    {
        ScreenManager.PopScreen();
    }

    public void ActionDiscord() => CommunityLinks.OpenDiscord();

    public void ActionPatreon() => CommunityLinks.OpenPatreon();

    // Opens a popup listing the individual donation platforms above a close button.
    public void ActionDonate() => CommunityLinks.ShowDonatePopup();

    // Opens a popup listing contributor, community, and supporter names.
    public void ActionCredits() => CommunityLinks.ShowCreditsPopup();

    public void Dispose()
    {
        if (disposed) return;

        disposed = true;
        lobbyRequestGeneration++;
        IsRefreshingSteamLobbies = false;
        discoveredSteamLobbies.Clear();
        ClearSteamLobbyDisplay();
    }

    private void SelectTab(CoopConnectionTabVM tab)
    {
        if (disposed || tab == null || SelectedTab == tab) return;

        if (SelectedTab != null)
        {
            SelectedTab.IsSelected = false;
        }

        SelectedTab = tab;
        SelectedTab.IsSelected = true;

        if (SelectedTab.Id == SteamLobbiesTabId)
        {
            SteamLobbiesTabActivated?.Invoke();
            ActionRefreshSteamLobbies();
        }
    }

    private void CompleteLobbyRefresh(
        int generation,
        IReadOnlyList<SteamLobbySummary> lobbies,
        string error)
    {
        if (disposed || generation != lobbyRequestGeneration) return;

        IsRefreshingSteamLobbies = false;

        if (!string.IsNullOrWhiteSpace(error))
        {
            ClearSteamLobbyDisplay();
            SteamLobbyStatusText = error;
            return;
        }

        lobbies ??= Array.Empty<SteamLobbySummary>();

        foreach (var lobby in lobbies)
        {
            if (lobby.LobbyId == 0) continue;

            discoveredSteamLobbies.Add(new SteamLobbyListItemVM(
                lobby.LobbyId,
                lobby.OwnerName,
                lobby.ConnectedPlayers,
                lobby.ProtocolVersion,
                lobby.ModVersion,
                lobby.PasswordRequired,
                lobby.IsCompatible,
                RequestSteamLobbyJoin));
        }

        ApplySteamLobbyHostFilter(resetPage: true);
    }

    private void ApplySteamLobbyHostFilter(bool resetPage)
    {
        string searchText = SteamLobbyHostSearchText.Trim();
        var filteredLobbies = discoveredSteamLobbies
            .Where(lobby => searchText.Length == 0 ||
                            lobby.HostText.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
            .Where(MatchesSteamLobbyPasswordFilter)
            .Where(lobby => lobby.ConnectedPlayers >= MinimumSteamLobbyPlayers)
            .ToList();

        filteredSteamLobbyCount = filteredLobbies.Count;
        filteredSteamLobbyPlayerCount = filteredLobbies.Sum(
            lobby => (long)lobby.ConnectedPlayers);
        if (resetPage)
        {
            steamLobbyPageIndex = 0;
        }
        else
        {
            steamLobbyPageIndex = Math.Min(steamLobbyPageIndex, Math.Max(0, SteamLobbyPageCount - 1));
        }

        SteamLobbies.Clear();
        foreach (var lobby in filteredLobbies
            .Skip(steamLobbyPageIndex * SteamLobbyPageSize)
            .Take(SteamLobbyPageSize))
        {
            SteamLobbies.Add(lobby);
        }

        NotifySteamLobbyDisplayChanged();

        if (filteredSteamLobbyCount > 0)
        {
            SteamLobbyStatusText = string.Empty;
        }
        else if (discoveredSteamLobbies.Count == 0)
        {
            SteamLobbyStatusText = KaiTORUiText.Get("kaitor_no_lobbies", "No hosted Steam lobbies were found.");
        }
        else
        {
            SteamLobbyStatusText = KaiTORUiText.Get("kaitor_no_lobbies_filtered", "No hosted Steam lobbies match the current filters.");
        }
    }

    private bool MatchesSteamLobbyPasswordFilter(SteamLobbyListItemVM lobby)
    {
        return SteamLobbyPasswordFilter switch
        {
            SteamLobbyPasswordFilter.NoPassword => !lobby.PasswordRequired,
            SteamLobbyPasswordFilter.PasswordRequired => lobby.PasswordRequired,
            _ => true,
        };
    }

    private void ClearSteamLobbyDisplay()
    {
        filteredSteamLobbyCount = 0;
        filteredSteamLobbyPlayerCount = 0;
        steamLobbyPageIndex = 0;
        SteamLobbies.Clear();
        NotifySteamLobbyDisplayChanged();
    }

    private void NotifySteamLobbyDisplayChanged()
    {
        OnPropertyChanged(nameof(SteamLobbiesHeaderText));
        OnPropertyChanged(nameof(SteamLobbyPageText));
        OnPropertyChanged(nameof(CurrentSteamLobbyPage));
        OnPropertyChanged(nameof(SteamLobbyPageCount));
        OnPropertyChanged(nameof(IsSteamLobbyPaginationVisible));
        OnPropertyChanged(nameof(IsPreviousSteamLobbyPageDisabled));
        OnPropertyChanged(nameof(IsNextSteamLobbyPageDisabled));
    }

    private void RequestSteamLobbyJoin(ulong lobbyId)
    {
        if (disposed || lobbyId == 0) return;

        messageBroker.Publish(this, new JoinSteamLobby(lobbyId));
    }

    private string LoadLastServerAddress()
    {
        try
        {
            var options = optionsStore.LoadOrDefault();
            if (options.TryGetSection(
                    OptionsTabId,
                    OptionsSectionId,
                    out DirectConnectionOptions saved) &&
                TryParseServerAddress(saved.LastServerAddress, out _, out _))
            {
                return saved.LastServerAddress.Trim();
            }
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Last direct connection address could not be loaded");
        }

        return DefaultServerAddress;
    }

    private void SaveLastServerAddress()
    {
        try
        {
            var options = optionsStore.LoadOrDefault();
            options.SetSection(
                OptionsTabId,
                OptionsSectionId,
                new DirectConnectionOptions { LastServerAddress = connectIP.Trim() });
            optionsStore.Save(options);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Last direct connection address could not be saved");
        }
    }

    public static bool TryParseServerAddress(string enteredAddress, out string host, out int port)
    {
        host = string.Empty;
        port = DefaultConnectionPort;

        if (string.IsNullOrWhiteSpace(enteredAddress)) return false;

        string address = enteredAddress.Trim();
        if (address[0] == '[')
        {
            int closingBracket = address.IndexOf(']');
            if (closingBracket <= 1) return false;

            host = address.Substring(1, closingBracket - 1);
            string suffix = address.Substring(closingBracket + 1);
            if (suffix.Length == 0) return true;

            return suffix[0] == ':' && TryParsePort(suffix.Substring(1), out port);
        }

        int firstColon = address.IndexOf(':');
        int lastColon = address.LastIndexOf(':');
        if (firstColon >= 0 && firstColon == lastColon)
        {
            host = address.Substring(0, firstColon).Trim();
            return host.Length > 0 && TryParsePort(address.Substring(firstColon + 1), out port);
        }

        if (firstColon >= 0 && !IPAddress.TryParse(address, out _)) return false;

        host = address;
        return true;
    }

    private static bool TryParsePort(string enteredPort, out int port)
    {
        return int.TryParse(enteredPort, out port) &&
            port > IPEndPoint.MinPort && port <= IPEndPoint.MaxPort;
    }

    private static bool IsLoopbackAddress(string address)
    {
        return string.Equals(address, "localhost", StringComparison.OrdinalIgnoreCase) ||
            (IPAddress.TryParse(address, out var ip) && IPAddress.IsLoopback(ip));
    }
}

/// <summary>Persisted address from the last direct connection attempt.</summary>
public class DirectConnectionOptions
{
    [JsonPropertyName("lastServerAddress")]
    public string LastServerAddress { get; set; }
}
