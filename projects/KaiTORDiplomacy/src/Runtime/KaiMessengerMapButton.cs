using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection.Encyclopedia.Pages;
using TaleWorlds.Core;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Native Gauntlet messenger button for the hero encyclopedia page.
///
/// The previous implementation appended a generic hyperlink to InformationText. Bannerlord's
/// EncyclopediaHeroPage RichTextWidget does not bind a hyperlink-click command there, so the
/// text rendered but could not be clicked. This implementation adds a small independent
/// Gauntlet layer to the existing encyclopedia screen. It requires no UIExtenderEx/ButterLib
/// dependency and is removed when the hero page is finalized.
/// </summary>
internal static class KaiMessengerMapButton
{
    private const string LayerName = "KaiTORMessengerEncyclopediaLayer";
    private const string MovieName = "KaiTORMessengerEncyclopedia";
    private const int LayerOrder = 220;

    private static ScreenBase _ownerScreen;
    private static GauntletLayer _layer;
    private static GauntletMovieIdentifier _movie;
    private static KaiMessengerEncyclopediaVM _dataSource;

    [HarmonyPatch(typeof(EncyclopediaHeroPageVM), nameof(EncyclopediaHeroPageVM.RefreshValues))]
    private static class EncyclopediaRefreshPatch
    {
        [HarmonyPostfix]
        private static void Postfix(EncyclopediaHeroPageVM __instance)
        {
            try
            {
                var hero = __instance?.Obj as Hero;
                ShowFor(hero);
            }
            catch (Exception ex)
            {
                KaiRuntimeLog.Exception("MESSENGER_UI_FAILED", ex, "stage=refresh_patch");
            }
        }
    }

    [HarmonyPatch(typeof(EncyclopediaHeroPageVM), nameof(EncyclopediaHeroPageVM.OnFinalize))]
    private static class EncyclopediaFinalizePatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            Hide("hero_page_finalize");
        }
    }

    internal static void RefreshFor(Hero hero)
    {
        try
        {
            if (_dataSource == null || hero == null || _dataSource.Target != hero)
                return;

            _dataSource.RefreshState();
        }
        catch (Exception ex)
        {
            KaiRuntimeLog.Exception("MESSENGER_UI_FAILED", ex, "stage=refresh_state");
        }
    }

    private static void ShowFor(Hero hero)
    {
        if (hero == null || hero == Hero.MainHero || !hero.IsLord || !hero.IsAlive)
        {
            Hide("hero_not_eligible");
            return;
        }

        var screen = ScreenManager.TopScreen;
        if (screen == null ||
            screen.GetType().Name.IndexOf("Encyclopedia", StringComparison.OrdinalIgnoreCase) < 0)
        {
            Hide("encyclopedia_screen_missing");
            return;
        }

        if (_ownerScreen == screen && _dataSource != null && _dataSource.Target == hero && _layer != null)
        {
            _dataSource.RefreshState();
            return;
        }

        Hide("replace_target");

        var behavior = Campaign.Current?.GetCampaignBehavior<KaiMessengerBehavior>();
        if (behavior == null)
        {
            KaiRuntimeLog.Write("MESSENGER_UI_FAILED", $"stage=create; target={hero.StringId}; reason=behavior_missing");
            return;
        }

        try
        {
            _ownerScreen = screen;
            _dataSource = new KaiMessengerEncyclopediaVM(hero, behavior);
            _layer = new GauntletLayer(LayerName, LayerOrder)
            {
                IsFocusLayer = false
            };

            // No focus/input restriction takeover: the encyclopedia keeps its own navigation
            // and scrolling, while this layer contributes only one clickable button.
            _ownerScreen.AddLayer(_layer);
            _movie = _layer.LoadMovie(MovieName, _dataSource);

            KaiRuntimeLog.Write(
                "MESSENGER_UI_READY",
                $"target={hero.StringId}; screen={screen.GetType().Name}; layer={LayerName}; movie={MovieName}");
        }
        catch (Exception ex)
        {
            KaiRuntimeLog.Exception("MESSENGER_UI_FAILED", ex, $"stage=create; target={hero.StringId}");
            Hide("create_failed");
        }
    }

    private static void Hide(string reason)
    {
        try
        {
            if (_dataSource != null)
                _dataSource.OnFinalize();

            if (_layer != null && _movie != null)
                _layer.ReleaseMovie(_movie);

            if (_ownerScreen != null && _layer != null && _ownerScreen.HasLayer(_layer))
                _ownerScreen.RemoveLayer(_layer);
        }
        catch (Exception ex)
        {
            KaiRuntimeLog.Exception("MESSENGER_UI_FAILED", ex, $"stage=hide; reason={reason}");
        }
        finally
        {
            _movie = null;
            _layer = null;
            _dataSource = null;
            _ownerScreen = null;
        }
    }

    private sealed class KaiMessengerEncyclopediaVM : ViewModel
    {
        private readonly KaiMessengerBehavior _behavior;
        private bool _isEnabled;
        private string _actionText;
        private string _costText;

        internal KaiMessengerEncyclopediaVM(Hero target, KaiMessengerBehavior behavior)
        {
            Target = target;
            _behavior = behavior;
            RefreshState();
        }

        internal Hero Target { get; }

        [DataSourceProperty]
        public bool IsEnabled
        {
            get => _isEnabled;
            private set
            {
                if (value == _isEnabled)
                    return;
                _isEnabled = value;
                OnPropertyChangedWithValue(value, nameof(IsEnabled));
            }
        }

        [DataSourceProperty]
        public string ActionText
        {
            get => _actionText;
            private set
            {
                if (value == _actionText)
                    return;
                _actionText = value;
                OnPropertyChangedWithValue(value, nameof(ActionText));
            }
        }

        [DataSourceProperty]
        public string CostText
        {
            get => _costText;
            private set
            {
                if (value == _costText)
                    return;
                _costText = value;
                OnPropertyChangedWithValue(value, nameof(CostText));
            }
        }

        public void ExecuteSendMessenger()
        {
            if (Target == null || _behavior == null)
                return;

            KaiRuntimeLog.Write("MESSENGER_CLICK", $"target={Target.StringId}");

            if (!_behavior.CanSend(Target, out var reason))
            {
                KaiRuntimeLog.Write("MESSENGER_CLICK_BLOCKED", $"target={Target.StringId}; reason={reason}");
                InformationManager.DisplayMessage(new InformationMessage(reason));
                RefreshState();
                return;
            }

            _behavior.RequestSend(Target);
        }

        internal void RefreshState()
        {
            if (Target == null || _behavior == null)
            {
                IsEnabled = false;
                ActionText = "Гонец недоступен";
                CostText = string.Empty;
                return;
            }

            if (_behavior.HasMessengerFor(Target))
            {
                IsEnabled = false;
                ActionText = "Гонец в пути";
                CostText = string.Empty;
                return;
            }

            if (_behavior.CanSend(Target, out _))
            {
                IsEnabled = true;
                ActionText = "Отправить гонца";
                CostText = $"{_behavior.GetSendCost(Target):N0} дин.";
                return;
            }

            IsEnabled = false;
            ActionText = "Гонец недоступен";
            CostText = string.Empty;
        }
    }
}
