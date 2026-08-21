using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;

namespace SmartDropZone
{
    /// <summary>
    /// Settings window. Non-modal: every change is applied to the shelf
    /// immediately via <see cref="SettingsChanged"/>.
    /// </summary>
    public partial class SettingsWindow : Window
    {
        /// <summary>Raised whenever a control changes; carries the new settings snapshot.</summary>
        public event Action<AppSettings>? SettingsChanged;

        private readonly AppSettings _current;

        public SettingsWindow(AppSettings current)
        {
            InitializeComponent();
            _current = current;

            DockLeftRadio.IsChecked = current.DockEdge == DockEdge.Left;
            DockTopRadio.IsChecked = current.DockEdge == DockEdge.Top;
            DockRightRadio.IsChecked = current.DockEdge == DockEdge.Right;
            DockFreeRadio.IsChecked = current.DockEdge == DockEdge.Free;
            AlwaysOnTopCheck.IsChecked = current.AlwaysOnTop;
            AlwaysOpenCheck.IsChecked = current.AlwaysOpen;
            StartWithWindowsCheck.IsChecked = current.StartWithWindows;
            ExpandOnHoverCheck.IsChecked = current.AutoOpenCapsule;
            AnimateCheck.IsChecked = current.Animate;
            HoldToDetachCheck.IsChecked = current.HoldToDetach;
            HoldToDockCheck.IsChecked = current.HoldToDock;
            HoldDelaySlider.Value = current.HoldDelayMs;
            HoldFillSlider.Value = current.HoldFillMs;
            UpdateHoldLabels();
            SortNameRadio.IsChecked = current.SortMode == SortMode.Name;
            SortTypeRadio.IsChecked = current.SortMode == SortMode.Type;
            SortDateRadio.IsChecked = current.SortMode == SortMode.DateAdded;
            ViewListRadio.IsChecked = current.ViewMode == ViewMode.List;
            ViewIconsRadio.IsChecked = current.ViewMode == ViewMode.Icons;

            ThemeSlateRadio.IsChecked = current.Theme == AppTheme.Slate;
            ThemeOceanRadio.IsChecked = current.Theme == AppTheme.Ocean;
            ThemeForestRadio.IsChecked = current.Theme == AppTheme.Forest;
            ThemeEmberRadio.IsChecked = current.Theme == AppTheme.Ember;
            ThemeVioletRadio.IsChecked = current.Theme == AppTheme.Violet;
            ThemeLightRadio.IsChecked = current.Theme == AppTheme.Light;
            OpacitySlider.Value = current.Opacity * 100.0;
            UpdateOpacityLabel();

            CollapseDelaySlider.Value = current.CollapseDelayMs;
            UpdateDelayLabel();

            AnimationSlider.Value = current.AnimationMs;
            UpdateAnimationLabel();

            DockLeftRadio.Checked += (_, _) => NotifyChanged();
            DockTopRadio.Checked += (_, _) => NotifyChanged();
            DockRightRadio.Checked += (_, _) => NotifyChanged();
            DockFreeRadio.Checked += (_, _) => NotifyChanged();
            AlwaysOnTopCheck.Checked += (_, _) => NotifyChanged();
            AlwaysOnTopCheck.Unchecked += (_, _) => NotifyChanged();
            AlwaysOpenCheck.Checked += (_, _) => NotifyChanged();
            AlwaysOpenCheck.Unchecked += (_, _) => NotifyChanged();
            StartWithWindowsCheck.Checked += (_, _) => NotifyChanged();
            StartWithWindowsCheck.Unchecked += (_, _) => NotifyChanged();
            ExpandOnHoverCheck.Checked += (_, _) => NotifyChanged();
            ExpandOnHoverCheck.Unchecked += (_, _) => NotifyChanged();
            AnimateCheck.Checked += (_, _) => NotifyChanged();
            AnimateCheck.Unchecked += (_, _) => NotifyChanged();
            HoldToDetachCheck.Checked += (_, _) => NotifyChanged();
            HoldToDetachCheck.Unchecked += (_, _) => NotifyChanged();
            HoldToDockCheck.Checked += (_, _) => NotifyChanged();
            HoldToDockCheck.Unchecked += (_, _) => NotifyChanged();
            HoldDelaySlider.ValueChanged += (_, _) => { UpdateHoldLabels(); NotifyChanged(); };
            HoldFillSlider.ValueChanged += (_, _) => { UpdateHoldLabels(); NotifyChanged(); };
            SortNameRadio.Checked += (_, _) => NotifyChanged();
            SortTypeRadio.Checked += (_, _) => NotifyChanged();
            SortDateRadio.Checked += (_, _) => NotifyChanged();
            ViewListRadio.Checked += (_, _) => NotifyChanged();
            ViewIconsRadio.Checked += (_, _) => NotifyChanged();
            CollapseDelaySlider.ValueChanged += (_, _) => { UpdateDelayLabel(); NotifyChanged(); };
            AnimationSlider.ValueChanged += (_, _) => { UpdateAnimationLabel(); NotifyChanged(); };
            ThemeSlateRadio.Checked += (_, _) => NotifyChanged();
            ThemeOceanRadio.Checked += (_, _) => NotifyChanged();
            ThemeForestRadio.Checked += (_, _) => NotifyChanged();
            ThemeEmberRadio.Checked += (_, _) => NotifyChanged();
            ThemeVioletRadio.Checked += (_, _) => NotifyChanged();
            ThemeLightRadio.Checked += (_, _) => NotifyChanged();
            OpacitySlider.ValueChanged += (_, _) => { UpdateOpacityLabel(); NotifyChanged(); };
        }

        private void UpdateOpacityLabel()
            => OpacityLabel.Text = ((int)OpacitySlider.Value).ToString(CultureInfo.InvariantCulture) + " %";

        private AppTheme SelectedTheme()
            => ThemeOceanRadio.IsChecked == true ? AppTheme.Ocean
             : ThemeForestRadio.IsChecked == true ? AppTheme.Forest
             : ThemeEmberRadio.IsChecked == true ? AppTheme.Ember
             : ThemeVioletRadio.IsChecked == true ? AppTheme.Violet
             : ThemeLightRadio.IsChecked == true ? AppTheme.Light
             : AppTheme.Slate;

        private void UpdateDelayLabel()
            => DelayLabel.Text = ((int)CollapseDelaySlider.Value).ToString(CultureInfo.InvariantCulture) + " ms";

        private void UpdateAnimationLabel()
            => AnimationLabel.Text = ((int)AnimationSlider.Value).ToString(CultureInfo.InvariantCulture) + " ms";

        private void UpdateHoldLabels()
        {
            HoldDelayLabel.Text = ((int)HoldDelaySlider.Value).ToString(CultureInfo.InvariantCulture) + " ms";
            HoldFillLabel.Text = ((int)HoldFillSlider.Value).ToString(CultureInfo.InvariantCulture) + " ms";
        }

        private void NotifyChanged()
        {
            var snapshot = new AppSettings
            {
                DockEdge = DockTopRadio.IsChecked == true ? DockEdge.Top
                         : DockLeftRadio.IsChecked == true ? DockEdge.Left
                         : DockFreeRadio.IsChecked == true ? DockEdge.Free
                         : DockEdge.Right,
                AlwaysOnTop = AlwaysOnTopCheck.IsChecked == true,
                AlwaysOpen = AlwaysOpenCheck.IsChecked == true,
                CollapseDelayMs = CollapseDelaySlider.Value,
                AnimationMs = AnimationSlider.Value,
                Animate = AnimateCheck.IsChecked == true,
                StartWithWindows = StartWithWindowsCheck.IsChecked == true,
                AutoOpenCapsule = ExpandOnHoverCheck.IsChecked == true,
                HoldToDetach = HoldToDetachCheck.IsChecked == true,
                HoldToDock = HoldToDockCheck.IsChecked == true,
                HoldDelayMs = HoldDelaySlider.Value,
                HoldFillMs = HoldFillSlider.Value,
                SortMode = SortDateRadio.IsChecked == true ? SortMode.DateAdded
                        : SortTypeRadio.IsChecked == true ? SortMode.Type
                        : SortMode.Name,
                ViewMode = ViewIconsRadio.IsChecked == true ? ViewMode.Icons : ViewMode.List,
                Theme = SelectedTheme(),
                Opacity = OpacitySlider.Value / 100.0,
                FreeLeft = _current.FreeLeft,
                FreeTop = _current.FreeTop,
                FreeWidth = _current.FreeWidth,
                FreeHeight = _current.FreeHeight,
                FreeCapsuleLeft = _current.FreeCapsuleLeft,
                FreeCapsuleTop = _current.FreeCapsuleTop
            };
            SettingsChanged?.Invoke(snapshot);
        }

        private void Done_Click(object sender, RoutedEventArgs e) => Close();

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        /// <summary>Drag the borderless window by its header.</summary>
        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is System.Windows.Controls.Button) return; // let the close button work
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }
    }
}