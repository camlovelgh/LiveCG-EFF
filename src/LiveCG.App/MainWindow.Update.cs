using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using LiveCG.Core;

namespace LiveCG.App;

public partial class MainWindow
{
    private const string ReleaseRepository = "sunjoo1968-design/LiveCG-EFF";
    private readonly CancellationTokenSource _updateCancellation = new();

    private async void CheckForUpdateOnStartup(object? sender, EventArgs e)
    {
        ContentRendered -= CheckForUpdateOnStartup;
#if LIVE_CG_STARTUP_DIAGNOSTICS
        // Keep the diagnostic executable in place until the operator finishes collecting logs.
        if (StartupDiagnostics.Enabled) return;
#endif
        Closed += (_, _) => _updateCancellation.Cancel();
        if (Environment.GetEnvironmentVariable("LIVECG_DISABLE_WORKSPACE_RESTORE") == "1") return;

        var appDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var install = appDirectory.Parent?.FullName;
        var installer = Path.Combine(appDirectory.FullName, "InstallUpdate.ps1");
        if (install is null || appDirectory.Name != "app" || !File.Exists(installer) ||
            !File.Exists(Path.Combine(install, "LiveCG-EFF.exe"))) return;

        string? staging = null;
        UpdateProgressWindow? progress = null;
        var agreed = false;
        using var preparation = CancellationTokenSource.CreateLinkedTokenSource(_updateCancellation.Token);
        var token = preparation.Token;
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"LiveCG-EFF/{ProductInfo.Version}");
            using var response = await client.GetAsync($"https://api.github.com/repos/{ReleaseRepository}/releases/latest", token);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(token));
            var release = json.RootElement;
            var tag = release.GetProperty("tag_name").GetString();
            if (tag is null || !tag.StartsWith('v') || !Version.TryParse(tag[1..], out var version) ||
                version.Build < 0 || version.Revision >= 0 || version <= Version.Parse(ProductInfo.Version) ||
                release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) return;

            var assetName = $"LiveCG-EFF-{version}-win-x64.zip";
            var asset = release.GetProperty("assets").EnumerateArray().FirstOrDefault(item =>
                item.GetProperty("name").GetString() == assetName && item.GetProperty("state").GetString() == "uploaded");
            if (asset.ValueKind == JsonValueKind.Undefined ||
                !asset.TryGetProperty("digest", out var digestValue) || digestValue.GetString() is not { Length: 71 } digest ||
                !digest.StartsWith("sha256:", StringComparison.Ordinal) || !digest[7..].All(Uri.IsHexDigit)) return;
            var url = $"https://github.com/{ReleaseRepository}/releases/download/{tag}/{assetName}";
            if (asset.GetProperty("browser_download_url").GetString() != url || _busy || !IsLoaded || token.IsCancellationRequested) return;
            var expectedSize = asset.GetProperty("size").GetInt64();
            if (expectedSize <= 0) return;
            agreed = MessageBox.Show(this,
                $"v{version} 업데이트가 있습니다. 다운로드하고 설치를 준비할까요?",
                "LiveCG-EFF 업데이트", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
            if (!agreed || token.IsCancellationRequested) return;

            staging = Path.Combine(install, ".livecg-update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            var archive = Path.Combine(staging, "release.zip");
            var extracted = Path.Combine(staging, "release");
            progress = new UpdateProgressWindow(this, version);
            EventHandler cancelPreparation = (_, _) => preparation.Cancel();
            progress.Closed += cancelPreparation;
            progress.Show();
            using (var download = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token))
            {
                download.EnsureSuccessStatusCode();
                await using var output = File.Create(archive);
                using var downloadTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                downloadTimeout.CancelAfter(TimeSpan.FromMinutes(15));
                await using var input = await download.Content.ReadAsStreamAsync(downloadTimeout.Token);
                var buffer = new byte[128 * 1024];
                long received = 0;
                var refresh = Stopwatch.StartNew();
                int count;
                while ((count = await input.ReadAsync(buffer, downloadTimeout.Token)) != 0)
                {
                    received += count;
                    if (received > expectedSize) throw new InvalidDataException("다운로드한 ZIP의 크기가 일치하지 않습니다.");
                    await output.WriteAsync(buffer.AsMemory(0, count), downloadTimeout.Token);
                    if (refresh.ElapsedMilliseconds < 100 && received != expectedSize) continue;
                    var percentage = 100d * received / expectedSize;
                    progress.ShowPhase($"다운로드 중 · {percentage:0}% ({received / 1048576d:0.0} / {expectedSize / 1048576d:0.0} MB)", percentage);
                    refresh.Restart();
                }
            }
            if (new FileInfo(archive).Length != expectedSize)
                throw new InvalidDataException("다운로드한 ZIP의 크기가 일치하지 않습니다.");
            progress.ShowPhase("다운로드 확인 중…");
            await using (var stream = File.OpenRead(archive))
            {
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
                if (!actual.Equals(digest[7..], StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("다운로드한 ZIP의 SHA-256이 일치하지 않습니다.");
            }
            progress.ShowPhase("설치 파일 압축 해제 중…");
            await Task.Run(() => ZipFile.ExtractToDirectory(archive, extracted), token);
            File.Delete(archive);
            var bundle = Path.Combine(extracted, $"LiveCG-EFF-{version}");
            if (FileVersionInfo.GetVersionInfo(Path.Combine(bundle, "app", "LiveCG-EFF.exe")).ProductVersion?.Split('+')[0] != version.ToString() ||
                !File.Exists(Path.Combine(bundle, "LiveCG-EFF.exe")))
                throw new InvalidDataException("업데이트 ZIP의 프로그램 버전이나 구조를 확인할 수 없습니다.");
            var helper = Path.Combine(staging, "InstallUpdate.ps1");
            await File.WriteAllTextAsync(helper, await File.ReadAllTextAsync(installer, token), new UTF8Encoding(true), token);
            if (token.IsCancellationRequested) return;
            progress.Closed -= cancelPreparation;
            progress.Close();
            progress = null;
            if (_busy) { Notify("작업을 마친 뒤 앱을 다시 열면 업데이트할 수 있습니다."); return; }
            if (MessageBox.Show(this,
                $"v{version} 설치 준비가 끝났습니다. 지금 LiveCG-EFF를 다시 시작해 설치할까요?\n\n아니요를 선택하면 다음에 앱을 열 때 다시 확인합니다.",
                "업데이트 설치 · 다시 시작", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            {
                Notify("다음 앱 시작 시 업데이트를 다시 확인합니다.");
                return;
            }

            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = install
            };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", helper,
                "-ParentId", Environment.ProcessId.ToString(), "-Bundle", bundle, "-ShowProgress" })
                start.ArgumentList.Add(argument);

            // Start the helper only after the normal save and close prompts have been accepted.
            var closed = false;
            void LaunchInstaller(object? window, EventArgs args)
            {
                closed = true;
                try
                {
                    using var process = Process.Start(start) ?? throw new InvalidOperationException("설치를 시작할 수 없습니다.");
                    staging = null;
                }
                catch (Exception error)
                {
                    MessageBox.Show(error.Message, "LiveCG-EFF 업데이트", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            Closed += LaunchInstaller;
            try { Close(); }
            finally { Closed -= LaunchInstaller; }
            if (!closed) Notify("업데이트 재시작을 취소했습니다.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (agreed && IsLoaded)
                MessageBox.Show(this, error.Message, "LiveCG-EFF 업데이트", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            if (progress?.IsVisible == true) progress.Close();
            if (staging is not null)
            {
                try { Directory.Delete(staging, recursive: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
