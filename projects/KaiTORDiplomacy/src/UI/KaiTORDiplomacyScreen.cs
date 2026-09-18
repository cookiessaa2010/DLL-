using KaiTOR.Diplomacy.Runtime;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.ScreenSystem;

namespace KaiTOR.Diplomacy.UI;

public sealed class KaiTORDiplomacyScreen : ScreenBase
{
    public const string LayerName = "KaiTORDiplomacyLayer";
    public const string MovieName = "KaiTORDiplomacyHubUIMovie";

    private static bool _isOpen;

    private KaiTORDiplomacyVM _dataSource;
    private GauntletLayer _gauntletLayer;
    private GauntletMovieIdentifier _gauntletMovie;

    public static bool TryOpen()
    {
        if (_isOpen)
            return false;

        try
        {
            ScreenManager.PushScreen(new KaiTORDiplomacyScreen());
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

        _dataSource = new KaiTORDiplomacyVM();
        _dataSource.CloseRequested += HandleClose;
        _dataSource.FamilyRequested += HandleFamily;
        _dataSource.CultureRequested += HandleCulture;
        _dataSource.FallbackHubRequested += HandleFallbackHub;

        _gauntletLayer = new GauntletLayer(LayerName, 100)
        {
            IsFocusLayer = true
        };

        AddLayer(_gauntletLayer);
        _gauntletLayer.InputRestrictions.SetInputRestrictions();
        _gauntletMovie = _gauntletLayer.LoadMovie(MovieName, _dataSource);

        KaiRuntimeLog.Write("GAUNTLET_UI_OPEN", $"movie={MovieName}; layer={LayerName}");
    }

    protected override void OnActivate()
    {
        base.OnActivate();
        if (_gauntletLayer != null)
            ScreenManager.TrySetFocus(_gauntletLayer);
    }

    protected override void OnDeactivate()
    {
        base.OnDeactivate();
        if (_gauntletLayer == null) return;

        _gauntletLayer.IsFocusLayer = false;
        ScreenManager.TryLoseFocus(_gauntletLayer);
    }

    protected override void OnFinalize()
    {
        if (_dataSource != null)
        {
            _dataSource.CloseRequested -= HandleClose;
            _dataSource.FamilyRequested -= HandleFamily;
            _dataSource.CultureRequested -= HandleCulture;
            _dataSource.FallbackHubRequested -= HandleFallbackHub;
            _dataSource.Dispose();
        }

        if (_gauntletLayer != null)
            RemoveLayer(_gauntletLayer);

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

    private static void HandleFamily()
    {
        ScreenManager.PopScreen();
        var result = KaiFamilyAffairsBehavior.OpenFamilyMenuFromConsole();
        KaiRuntimeLog.Write("GAUNTLET_UI_ROUTE", $"target=family; result={result}");
    }

    private static void HandleCulture()
    {
        ScreenManager.PopScreen();

        var behavior = Campaign.Current?.GetCampaignBehavior<KaiCultureAssimilationBehavior>();
        if (behavior == null)
        {
            KaiRuntimeLog.Write("GAUNTLET_UI_ROUTE_FAILED", "target=culture; reason=behavior_missing");
            return;
        }

        behavior.OpenCultureChangeDialog();
        KaiRuntimeLog.Write("GAUNTLET_UI_ROUTE", "target=culture");
    }

    private static void HandleFallbackHub()
    {
        ScreenManager.PopScreen();
        var result = KaiDiplomacyHubBehavior.OpenHubFromExternalUi();
        KaiRuntimeLog.Write("GAUNTLET_UI_ROUTE", $"target=fallback_hub; result={result}");
    }
}
