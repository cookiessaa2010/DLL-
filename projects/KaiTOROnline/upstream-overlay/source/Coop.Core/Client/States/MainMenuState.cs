using Common;
using Common.Logging;
using Common.Messaging;
using Common.Network;
using Common.Network.Session.Messages;
using Coop.Core.Client.Messages;
using Coop.Core.Common;
using Coop.Core.Common.Session;
using GameInterface;
using GameInterface.Services.GameState.Interfaces;
using GameInterface.Services.UI.Interfaces;
using GameInterface.Services.UI.JoinCancel;
using GameInterface.Services.UI.Messages;
using Serilog;
using System.Threading;
using TaleWorlds.Library;

namespace Coop.Core.Client.States;

/// <summary>
/// State Logic Controller for the Main Menu Client State. Owns the connecting screen for as long as
/// the attempt is dialing: the engine's loading window for art and status, a coop layer for cancel.
/// </summary>
public class MainMenuState : ClientStateBase
{
    private static readonly ILogger Logger = LogManager.GetLogger<MainMenuState>();

    private readonly IMessageBroker messageBroker;
    private readonly INetwork network;
    private readonly IGameInterface gameInterface;
    private readonly IGameStateInterface gameStateInterface;
    private readonly ILoadingInterface loadingInterface;
    private readonly IJoinAttemptOverlay joinAttemptOverlay;
    private readonly JoinAttemptPresentation joinAttempt;
    private readonly ICoopFinalizer coopFinalizer;

    private volatile bool shown;
    private volatile bool connected;
    private volatile bool cancelling;

    public MainMenuState(
        IClientLogic logic,
        IMessageBroker messageBroker,
        INetwork network,
        IGameInterface gameInterface,
        IGameStateInterface gameStateInterface,
        ILoadingInterface loadingInterface,
        IJoinAttemptOverlay joinAttemptOverlay,
        JoinAttemptPresentation joinAttempt,
        ICoopFinalizer coopFinalizer) : base(logic)
    {
        this.messageBroker = messageBroker;
        this.network = network;
        this.gameInterface = gameInterface;
        this.gameStateInterface = gameStateInterface;
        this.loadingInterface = loadingInterface;
        this.joinAttemptOverlay = joinAttemptOverlay;
        this.joinAttempt = joinAttempt;
        this.coopFinalizer = coopFinalizer;
        loadingInterface.HideLoadingScreen();
        messageBroker.Subscribe<NetworkConnected>(Handle_NetworkConnected);
        messageBroker.Subscribe<CancelJoinAttempt>(Handle_CancelJoinAttempt);
    }

    public override void Dispose()
    {
        messageBroker.Unsubscribe<NetworkConnected>(Handle_NetworkConnected);
        messageBroker.Unsubscribe<CancelJoinAttempt>(Handle_CancelJoinAttempt);

        if (connected) return;

        HideJoinAttempt();
    }

    public override void Connect()
    {
        ShowJoinAttempt();
        network.Start();
    }

    internal void Handle_NetworkConnected(MessagePayload<NetworkConnected> obj)
    {
        connected = true;
        shown = false;

        using (GameThread.ActivateCancellation(CancellationToken.None))
        {
            GameThread.RunSafe(joinAttemptOverlay.Hide, context: "HideJoinAttemptOverlay");
        }

        loadingInterface.ShowLoadingScreen(
            GetLocalizedJoinTitle(),
            global::GameInterface.Services.UI.KaiTORUiText.Get("kaitor_applying_patches", "Applying patches..."));
        gameInterface.PatchAll();
        loadingInterface.SetLoadingMessage(
            GetLocalizedJoinTitle(),
            global::GameInterface.Services.UI.KaiTORUiText.Get("kaitor_validating_modules", "Validating modules..."));
        Logic.ValidateModules();
    }

    internal void Handle_CancelJoinAttempt(MessagePayload<CancelJoinAttempt> obj)
    {
        if (!shown || connected || cancelling) return;

        cancelling = true;

        using (GameThread.ActivateCancellation(CancellationToken.None))
        {
            GameThread.EnqueueSafe(FinishCancel, context: nameof(CancelJoinAttempt));
        }
    }

    private void FinishCancel()
    {
        try
        {
            if (connected) return;

            Logger.Information("Player cancelled a {Intent} join attempt", joinAttempt.Intent);

            // Held lobby membership would keep a host slot and make a retried join no-op.
            if (joinAttempt.Intent == JoinIntent.PlayerSteam)
            {
                messageBroker.Publish(this, new SessionJoinAbandoned());
            }

            coopFinalizer.Finalize(closeText: null);

            HideJoinAttempt();
            InformationManager.DisplayMessage(new InformationMessage(GetLocalizedCancelledNotice()));
        }
        catch
        {
            cancelling = false;
            throw;
        }
    }

    private void ShowJoinAttempt()
    {
        shown = true;

        GameThread.RunSafe(() =>
        {
            // A hide that ran while this was queued cannot take down a layer that is not up yet.
            if (!shown) return;

            loadingInterface.ShowLoadingScreen(GetLocalizedJoinTitle(), GetLocalizedJoinDescription());
            try
            {
                joinAttemptOverlay.Show(GetLocalizedCancelLabel());
            }
            catch
            {
                loadingInterface.HideLoadingScreen();
                throw;
            }
        }, context: "ShowJoinAttempt");
    }

    private string GetLocalizedJoinTitle()
    {
        return joinAttempt.Intent == JoinIntent.HostLoopback
            ? global::GameInterface.Services.UI.KaiTORUiText.Get("kaitor_hosting_title", "Hosting KaiTOR Co-op")
            : global::GameInterface.Services.UI.KaiTORUiText.Get("kaitor_connecting_title", "Connecting to KaiTOR Co-op");
    }

    private string GetLocalizedJoinDescription()
    {
        return joinAttempt.Intent switch
        {
            JoinIntent.PlayerDirect => global::GameInterface.Services.UI.KaiTORUiText.Get("kaitor_contacting_server", "Contacting the server..."),
            JoinIntent.PlayerSteam => global::GameInterface.Services.UI.KaiTORUiText.Get("kaitor_contacting_steam", "Contacting the host through Steam..."),
            JoinIntent.HostLoopback => global::GameInterface.Services.UI.KaiTORUiText.Get("kaitor_waiting_server", "Waiting for the server to load the campaign save..."),
            _ => joinAttempt.Description,
        };
    }

    private string GetLocalizedCancelLabel()
    {
        return joinAttempt.Intent == JoinIntent.HostLoopback
            ? global::GameInterface.Services.UI.KaiTORUiText.Get("kaitor_stop_waiting", "Stop Waiting")
            : global::GameInterface.Services.UI.KaiTORUiText.Get("kaitor_cancel", "Cancel");
    }

    private string GetLocalizedCancelledNotice()
    {
        return joinAttempt.Intent == JoinIntent.HostLoopback
            ? global::GameInterface.Services.UI.KaiTORUiText.Get(
                "kaitor_stopped_waiting_notice",
                "Stopped waiting for the co-op server. Its window stays open until you close it.")
            : global::GameInterface.Services.UI.KaiTORUiText.Get(
                "kaitor_connection_cancelled",
                "Connection attempt cancelled.");
    }

    private void HideJoinAttempt()
    {
        if (!shown) return;

        shown = false;

        using (GameThread.ActivateCancellation(CancellationToken.None))
        {
            GameThread.RunSafe(joinAttemptOverlay.Hide, context: "HideJoinAttemptOverlay");
            GameThread.RunSafe(loadingInterface.HideLoadingScreen, context: "HideJoinAttemptWindow");
        }
    }

    public override void Disconnect()
    {
        gameStateInterface.GoToMainMenu();
    }

    public override void EnterMainMenu()
    {
    }

    public override void ExitGame()
    {
    }

    public override void LoadSavedData()
    {
    }

    public override void StartCharacterCreation()
    {
    }

    public override void EnterCampaignState()
    {
    }

    public override void EnterMissionState()
    {
    }

    public override void ValidateModules()
    {
        Logic.SetState<ValidateModuleState>();
    }
}
