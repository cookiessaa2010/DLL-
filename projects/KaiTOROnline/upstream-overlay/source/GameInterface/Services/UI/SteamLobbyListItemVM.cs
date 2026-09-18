using Common;
using System;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace GameInterface.Services.UI;

/// <summary>
/// Display data and selection command for one hosted standalone server lobby.
/// </summary>
public sealed class SteamLobbyListItemVM : ViewModel
{
    private const string CompatibleStatusColor = "#F4E1C4FF";
    private const string IncompatibleStatusColor = "#FF8080FF";

    private readonly Action<ulong> onJoin;

    public SteamLobbyListItemVM(
        ulong lobbyId,
        string ownerName,
        int connectedPlayers,
        int protocolVersion,
        string modVersion,
        bool passwordRequired,
        bool isCompatible,
        Action<ulong> onJoin)
    {
        LobbyId = lobbyId;
        OwnerName = ownerName?.Trim() ?? string.Empty;
        ConnectedPlayers = Math.Max(0, connectedPlayers);
        ProtocolVersion = protocolVersion;
        ModVersion = modVersion ?? string.Empty;
        PasswordRequired = passwordRequired;
        IsCompatible = isCompatible;
        this.onJoin = onJoin ?? throw new ArgumentNullException(nameof(onJoin));

        string hintText;
        if (!ModInformation.MatchesBuildVersion(ModVersion))
        {
            string hostVersion = string.IsNullOrWhiteSpace(ModVersion) ? KaiTORUiText.Get("kaitor_unknown_version", "unknown") : ModVersion;
            hintText = KaiTORUiText.Format(
                "kaitor_host_version_hint",
                "The host version is {HOST_VERSION}; your version is {CLIENT_VERSION}.",
                ("HOST_VERSION", hostVersion),
                ("CLIENT_VERSION", ModInformation.BuildVersion));
        }
        else
        {
            hintText = KaiTORUiText.Format(
                "kaitor_protocol_version_hint",
                "The host protocol version is {HOST_PROTOCOL}; your protocol version is {CLIENT_PROTOCOL}.",
                ("HOST_PROTOCOL", ProtocolVersion),
                ("CLIENT_PROTOCOL", Common.Network.Session.SessionJoinInfo.CurrentVersion));
        }
        StatusHint = new HintViewModel(new TextObject(hintText));
    }

    public ulong LobbyId { get; }
    public string OwnerName { get; }
    public int ConnectedPlayers { get; }
    public int ProtocolVersion { get; }
    public string ModVersion { get; }
    public bool PasswordRequired { get; }
    public bool IsCompatible { get; }

    [DataSourceProperty]
    public string HostText => !string.IsNullOrWhiteSpace(OwnerName) ? OwnerName : KaiTORUiText.Get("kaitor_unknown_host", "Unknown host");

    [DataSourceProperty]
    public string ConnectedPlayersText => ConnectedPlayers.ToString();

    [DataSourceProperty]
    public string StatusText => IsCompatible ? KaiTORUiText.Get("kaitor_compatible", "Compatible") : KaiTORUiText.Get("kaitor_incompatible", "Incompatible");

    [DataSourceProperty]
    public string StatusColor => IsCompatible ? CompatibleStatusColor : IncompatibleStatusColor;

    [DataSourceProperty]
    public string PasswordText => PasswordRequired ? KaiTORUiText.Get("kaitor_password_required_short", "Password required") : KaiTORUiText.Get("kaitor_no_password_short", "No password");

    [DataSourceProperty]
    public bool IsCompatibleStatusVisible => IsCompatible;

    [DataSourceProperty]
    public bool IsStatusHintVisible => !IsCompatible;

    [DataSourceProperty]
    public string StatusHintLabelText => KaiTORUiText.Get("kaitor_incompatible_hint_label", "Incompatible (i)");

    [DataSourceProperty]
    public HintViewModel StatusHint { get; }

    [DataSourceProperty]
    public bool IsJoinDisabled => !IsCompatible;

    public void ExecuteJoin()
    {
        if (!IsCompatible) return;

        onJoin(LobbyId);
    }
}
