using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SmartDropZone
{
    /// <summary>Result of an update check.</summary>
    public sealed class UpdateInfo
    {
        public bool HasUpdate { get; set; }
        public string CurrentVersion { get; set; } = "";
        public string LatestVersion { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public string ReleaseNotes { get; set; } = "";
    }

    /// <summary>
    /// Checks GitHub for a newer release and applies it. The newest release
    /// (highest semver tag, e.g. "1.0.3") is compared against the running
    /// version; if the remote is newer, an update is offered.
    /// </summary>
    public static class UpdateService
    {
        private const string RepoOwner = "Hr-2";
        private const string RepoName = "SmartDropZone";
        private const string ReleasesApiUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases?per_page=20";

        /// <summary>Queries GitHub for the newest release and compares it to the running version.</summary>
        public static async Task<UpdateInfo> CheckForUpdateAsync()
        {
            var info = new UpdateInfo { CurrentVersion = AppInfo.Version };

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("SmartDropZone");
                client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
                client.Timeout = TimeSpan.FromSeconds(20);

                var json = await client.GetStringAsync(ReleasesApiUrl);
                using var doc = JsonDocument.Parse(json);
                var releases = doc.RootElement;

                // Walk releases newest-first and pick the highest semver tag.
                Version? best = null;
                JsonElement bestRelease = default;
                foreach (var release in releases.EnumerateArray())
                {
                    if (!release.TryGetProperty("tag_name", out var tagEl)) continue;
                    string tag = tagEl.GetString() ?? "";
                    if (!Version.TryParse(tag.TrimStart('v'), out var v)) continue;
                    if (best is null || v > best)
                    {
                        best = v;
                        bestRelease = release;
                    }
                }

                if (best is null) return info;

                info.LatestVersion = best.ToString();

                if (bestRelease.TryGetProperty("body", out var body))
                    info.ReleaseNotes = body.GetString() ?? "";

                if (bestRelease.TryGetProperty("assets", out var assets))
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        var assetName = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                        if (assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            info.DownloadUrl = asset.TryGetProperty("browser_download_url", out var url) ? url.GetString() ?? "" : "";
                            break;
                        }
                    }
                }

                if (Version.TryParse(info.CurrentVersion, out var current))
                    info.HasUpdate = best > current;
            }
            catch
            {
                // Offline, rate-limited, or GitHub changed — treat as "no update found".
            }

            return info;
        }

        /// <summary>
        /// Downloads the release zip, extracts it, then hands off to a hidden batch
        /// helper that waits for this process to exit, swaps the files, and relaunches.
        /// </summary>
        public static async Task DownloadAndApplyAsync(UpdateInfo info, IProgress<(int percent, string status)>? progress = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(info.DownloadUrl))
                throw new InvalidOperationException("No update asset found on GitHub.");

            var appDir = Path.GetDirectoryName(typeof(UpdateService).Assembly.Location) ?? ".";
            var tempDir = Path.Combine(Path.GetTempPath(), "SmartDropZone_update");
            var zipPath = Path.Combine(tempDir, "update.zip");
            var extractDir = Path.Combine(tempDir, "extracted");

            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
                Directory.CreateDirectory(extractDir);

                progress?.Report((0, "Downloading update..."));

                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("SmartDropZone");
                client.Timeout = TimeSpan.FromMinutes(15);

                using (var response = await client.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    var totalBytes = response.Content.Headers.ContentLength ?? -1;

                    await using (var content = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
                    await using (var fs = File.Create(zipPath))
                    {
                        var buffer = new byte[81920];
                        long readBytes = 0;
                        int lastPct = -1;
                        int bytesRead;
                        while ((bytesRead = await content.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
                        {
                            await fs.WriteAsync(buffer, 0, bytesRead, ct).ConfigureAwait(false);
                            readBytes += bytesRead;
                            if (totalBytes > 0)
                            {
                                var pct = (int)(readBytes * 100 / totalBytes);
                                if (pct != lastPct)
                                {
                                    lastPct = pct;
                                    progress?.Report((pct, $"Downloading update... {pct}%"));
                                }
                            }
                        }
                    }
                }

                progress?.Report((95, "Extracting update..."));

                ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);

                progress?.Report((99, "Applying update..."));

                var exeName = "SmartDropZone.exe";
                var pid = Environment.ProcessId;
                var batchPath = Path.Combine(tempDir, "update.bat");

                var batch = $@"@echo off
setlocal
set ""APP_DIR={appDir}""
set ""STAGE_DIR={extractDir}""
set ""PID={pid}""
set ""EXE={exeName}""

:wait
tasklist /FI ""PID eq %PID%"" 2>NUL | find ""%PID%"" >NUL
if not errorlevel 1 (
    timeout /t 1 /nobreak >NUL
    goto wait
)

robocopy ""%STAGE_DIR%"" ""%APP_DIR%"" /E /IS /R:3 /W:2 >NUL
if errorlevel 8 (
    echo ERROR: Failed to copy update files.
    exit /b 1
)

rmdir /S /Q ""%STAGE_DIR%"" 2>NUL
del ""%~dp0update.zip"" 2>NUL
del ""%~f0"" 2>NUL

start """" ""%APP_DIR%\%EXE%""
";

                File.WriteAllText(batchPath, batch);

                var psi = new ProcessStartInfo
                {
                    FileName = batchPath,
                    WorkingDirectory = Path.GetDirectoryName(batchPath),
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi);

                progress?.Report((100, "Update ready. Restarting..."));
            }
            catch (Exception ex)
            {
                try
                {
                    if (Directory.Exists(tempDir))
                        Directory.Delete(tempDir, true);
                }
                catch { }
                throw new InvalidOperationException("Update failed: " + ex.Message, ex);
            }
        }
    }
}
