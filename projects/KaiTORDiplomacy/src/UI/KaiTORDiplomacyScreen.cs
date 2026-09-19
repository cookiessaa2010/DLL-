using KaiTOR.Diplomacy.Runtime;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace KaiTOR.Diplomacy.UI;

public sealed class KaiTORDiplomacyScreen : ScreenBase
{
    public const string LayerName = "KaiTORDiplomacyLayer";
    public const string MovieName = "KaiTORDiplomacyHubUIMovie";
    public const string DiagnosticStage = "DIPLOMACY-FINISH-safe-functional-dashboard";

    private static bool _isOpen;

    private readonly Kingdom _initialKingdom;
    private KaiTORDiplomacyVM _dataSource;
    private GauntletLayer _gauntletLayer;
    private GauntletMovieIdentifier _gauntletMovie;

    private KaiTORDiplomacyScreen(Kingdom initialKingdom)
    {
        _initialKingdom = initialKingdom;
    }

    public static bool TryOpen(Kingdom initialKingdom = null)
    {
        if (_isOpen)
            return false;

        try
        {
            ScreenManager.PushScreen(new KaiTORDiplomacyScreen(initialKingdom));
            return true;
        }
        catch (System.Exception ex)
        {
            KaiRuntimeLog.Exception("GAUNTLET_UI_FAILED", ex, "stage=push_screen");
            _isOpen = false;
            return false;
        }
    }

    protected override void OnInitialize()
    {
        base.OnInitialize();
        _isOpen = true;

        _dataSource = new KaiTORDiplomacyVM(_initialKingdom);
        _dataSource.CloseRequested += HandleClose;
        _dataSource.CultureRequested += HandleCulture;

        _gauntletLayer = new GauntletLayer(LayerName, 100)
        {
            IsFocusLayer = true
        };

        AddLayer(_gauntletLayer);
        _gauntletLayer.InputRestrictions.SetInputRestrictions();
        KaiRuntimeLog.Write("GAUNTLET_UI_LOAD_BEGIN", $"stage={DiagnosticStage}; movie={MovieName}; layer={LayerName}");
        _gauntletMovie = _gauntletLayer.LoadMovie(MovieName, _dataSource);
        KaiRuntimeLog.Write("GAUNTLET_UI_LOAD_END", $"stage={DiagnosticStage}; movie={MovieName}; layer={LayerName}");
        KaiRuntimeLog.Write("GAUNTLET_UI_OPEN", $"stage={DiagnosticStage}; movie={MovieName}; layer={LayerName}");
    }

    protected override void OnActivate()
    {
        base.OnActivate();
        KaiRuntimeLog.Write("GAUNTLET_UI_ACTIVATE", $"stage={DiagnosticStage}");
        if (_gauntletLayer != null)
            ScreenManager.TrySetFocus(_gauntletLayer);
    }

    protected override void OnDeactivate()
    {
        base.OnDeactivate();
        if (_gauntletLayer == null) return;

        _gauntletLayer.IsFocusLayer = false;
        _gauntletLayer.InputRestrictions.ResetInputRestrictions();
        ScreenManager.TryLoseFocus(_gauntletLayer);
    }

    protected override void OnFinalize()
    {
        if (_dataSource != null)
        {
            _dataSource.CloseRequested -= HandleClose;
            _dataSource.CultureRequested -= HandleCulture;
            _dataSource.Dispose();
        }

        if (_gauntletLayer != null)
        {
            _gauntletLayer.IsFocusLayer = false;
            _gauntletLayer.InputRestrictions.ResetInputRestrictions();
            ScreenManager.TryLoseFocus(_gauntletLayer);

            if (_gauntletMovie != null)
                _gauntletLayer.ReleaseMovie(_gauntletMovie);

            RemoveLayer(_gauntletLayer);
        }

        _gauntletMovie = null;
        _gauntletLayer = null;
        _dataSource = null;
        _isOpen = false;

        KaiRuntimeLog.Write("GAUNTLET_UI_CLOSE", $"movie={MovieName}");
        base.OnFinalize();
    }

    private static void HandleClose()
    {
        ScreenManager.PopScreen();
    }

    private static void HandleCulture()
    {
        var behavior = Campaign.Current?.GetCampaignBehavior<KaiCultureAssimilationBehavior>();
        if (behavior == null)
        {
            KaiRuntimeLog.Write("GAUNTLET_UI_ROUTE_FAILED", "target=culture; reason=behavior_missing");
            InformationManager.DisplayMessage(new InformationMessage("Смена культуры сейчас недоступна."));
            return;
        }

        ScreenManager.PopScreen();
        behavior.OpenCultureChangeDialog();
        KaiRuntimeLog.Write("GAUNTLET_UI_ROUTE", "target=culture");
    }

}
