using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace KaiTOR.Diplomacy.UI;

public sealed class KaiDiplomacyKingdomItemVM : ViewModel
{
    private readonly Action<KaiDiplomacyKingdomItemVM> _select;
    private bool _isSelected;
    private string _statusText;
    private string _trustText;

    internal KaiDiplomacyKingdomItemVM(
        Kingdom kingdom,
        string statusText,
        string trustText,
        bool isSelected,
        Action<KaiDiplomacyKingdomItemVM> select)
    {
        Kingdom = kingdom;
        NameText = kingdom?.Name?.ToString() ?? string.Empty;
        _statusText = statusText ?? string.Empty;
        _trustText = trustText ?? string.Empty;
        _isSelected = isSelected;
        _select = select;
    }

    internal Kingdom Kingdom { get; }

    [DataSourceProperty]
    public string NameText { get; }

    [DataSourceProperty]
    public string StatusText
    {
        get => _statusText;
        internal set
        {
            if (_statusText == value) return;
            _statusText = value ?? string.Empty;
            OnPropertyChanged(nameof(StatusText));
        }
    }

    [DataSourceProperty]
    public string TrustText
    {
        get => _trustText;
        internal set
        {
            if (_trustText == value) return;
            _trustText = value ?? string.Empty;
            OnPropertyChanged(nameof(TrustText));
        }
    }

    [DataSourceProperty]
    public bool IsSelected
    {
        get => _isSelected;
        internal set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged(nameof(IsSelected));
        }
    }

    public void ExecuteSelect()
    {
        _select?.Invoke(this);
    }
}
