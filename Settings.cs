using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace SmartDropZone
{
    /// <summary>How shelf cards are ordered.</summary>
    public enum SortMode { Name, Type, DateAdded }

    /// <summary>How shelf cards are shown (Explorer-like list / icon grid).</summary>
    public enum ViewMode { List, Icons }

    /// <summary>
    /// User preferences, persisted to %AppData%\SmartDropZone\settings.json.
    /// </summary>
    public sealed class AppSettings
    {
        public DockEdge DockEdge { get; set; } = DockEdge.Right;
        public bool AlwaysOnTop { get; set; } = true;
        public bool AlwaysOpen { get; set; }
        public double CollapseDelayMs { get; set; } = 350;
        public double AnimationMs { get; set; } = 170; // docked slide-out duration
        public bool Animate { get; set; } = true;      // enable/disable animations
        public bool StartWithWindows { get; set; }
        public bool CheckForUpdates { get; set; } = true;

        // Floating-shelf behaviour
        public bool AutoOpenCapsule { get; set; } = true;
        public bool HoldToDetach { get; set; } = true;   // docked shelf -> hold out -> free
        public bool HoldToDock { get; set; } = true;     // free shelf -> hold near edge -> dock
        public double HoldDelayMs { get; set; } = 1000;  // hold still before the ring starts filling
        public double HoldFillMs { get; set; } = 1500;   // how long the ring takes to fill
        public SortMode SortMode { get; set; } = SortMode.Name;
        public ViewMode ViewMode { get; set; } = ViewMode.List;
        public double? FreeCapsuleLeft { get; set; }
        public double? FreeCapsuleTop { get; set; }

        // Position along the dock edge for docked shelves (Top for side docks,
        // Left for the top dock). null = use the default spot.
        public double? DockOffset { get; set; }

        // Docked shelf size (null = default footprint).
        public double? DockWidth { get; set; }
        public double? DockHeight { get; set; }

        // Appearance
        public AppTheme Theme { get; set; } = AppTheme.Slate;
        public double Opacity { get; set; } = 1.0; // 0.3 - 1.0

        // Free-floating shelf geometry (null = unset, keep current position).
        public double? FreeLeft { get; set; }
        public double? FreeTop { get; set; }
        public double? FreeWidth { get; set; }
        public double? FreeHeight { get; set; }

        private static string SettingsFile =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "SmartDropZone", "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFile),
                        new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } });
                    if (s != null) return s;
                }
            }
            catch
            {
                // Fall back to defaults on a corrupt settings file.
            }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                string dir = Path.GetDirectoryName(SettingsFile)!;
                Directory.CreateDirectory(dir);
                File.WriteAllText(SettingsFile,
                    JsonSerializer.Serialize(this, new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Converters = { new JsonStringEnumConverter() }
                    }));
            }
            catch
            {
                // Best-effort persistence.
            }
        }

        /// <summary>Add/remove the HKCU Run entry for "Start with Windows".</summary>
        public void ApplyStartWithWindows()
        {
            const string runKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
            const string valueName = "SmartDropZone";
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(runKeyPath, writable: true);
                if (key is null) return;

                if (StartWithWindows) key.SetValue(valueName, GetRunCommand());
                else key.DeleteValue(valueName, throwOnMissingValue: false);
            }
            catch
            {
                // Registry access can be blocked; ignore.
            }
        }

        private static string GetRunCommand()
        {
            string asm = Assembly.GetExecutingAssembly().Location;
            if (string.Equals(Path.GetExtension(asm), ".exe", StringComparison.OrdinalIgnoreCase))
                return $"\"{asm}\"";
            // Framework-dependent run: dotnet.exe "<dll>"
            return $"\"{Environment.ProcessPath}\" \"{asm}\"";
        }
    }
}