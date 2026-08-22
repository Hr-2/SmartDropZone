using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace SmartDropZone
{
    /// <summary>
    /// Shows the result of an update check. If an update exists it offers
    /// "Update now", which downloads, extracts and restarts the app. If the
    /// app is already current it just confirms that.
    /// </summary>
    public partial class UpdateWindow : Window
    {
        private UpdateInfo? _info;
        private bool _busy;

        public UpdateWindow(UpdateInfo? info)
        {
            InitializeComponent();
            SetInfo(info);
        }

        private void SetInfo(UpdateInfo? info)
        {
            _info = info;

            if (info == null)
            {
                // Couldn't reach GitHub — treat as "nothing to check".
                HeaderText.Text = "Check for updates";
                PromptPanel.Visibility = Visibility.Collapsed;
                ProgressPanel.Visibility = Visibility.Collapsed;
                UpToDatePanel.Visibility = Visibility.Visible;
                UpToDateSubtitle.Text = "Couldn't reach GitHub right now. Check your connection and try again.";
                return;
            }

            if (info.HasUpdate)
            {
                HeaderText.Text = "Update available";
                UpToDatePanel.Visibility = Visibility.Collapsed;
                ProgressPanel.Visibility = Visibility.Collapsed;
                PromptPanel.Visibility = Visibility.Visible;

                PromptSubtitle.Text = $"You're running {info.CurrentVersion}. The latest release is \"{info.LatestVersion}\".";
                ReleaseNotes.Text = string.IsNullOrWhiteSpace(info.ReleaseNotes)
                    ? "A new build of Smart Drop Zone is ready to install."
                    : info.ReleaseNotes.Trim();
            }
            else
            {
                HeaderText.Text = "Check for updates";
                PromptPanel.Visibility = Visibility.Collapsed;
                ProgressPanel.Visibility = Visibility.Collapsed;
                UpToDatePanel.Visibility = Visibility.Visible;
                UpToDateSubtitle.Text = $"Smart Drop Zone {info.CurrentVersion} is the newest build.";
            }
        }

        private async void Update_Click(object sender, RoutedEventArgs e)
        {
            if (_info is not { HasUpdate: true }) return;
            if (_busy) return;
            _busy = true;

            PromptPanel.Visibility = Visibility.Collapsed;
            ProgressPanel.Visibility = Visibility.Visible;
            HeaderText.Text = "Updating";

            try
            {
                var progress = new Progress<(int percent, string status)>(p =>
                {
                    ProgressBar.Value = p.percent;
                    StatusText.Text = p.status;
                });

                await UpdateService.DownloadAndApplyAsync(_info, progress);

                // The batch helper restarts the app after this process exits.
                StatusText.Text = "Restarting...";
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                _busy = false;
                HeaderText.Text = "Update failed";
                StatusText.Text = ex.Message;
                PromptPanel.Visibility = Visibility.Visible;
                ProgressPanel.Visibility = Visibility.Collapsed;
                ReleaseNotes.Text = "The update could not be applied. You can try again or download it manually from the GitHub Releases page.";
            }
        }

        private void Later_Click(object sender, RoutedEventArgs e) => Close();

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        /// <summary>Drag the borderless window by its header.</summary>
        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is System.Windows.Controls.Button) return;
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }
    }
}
