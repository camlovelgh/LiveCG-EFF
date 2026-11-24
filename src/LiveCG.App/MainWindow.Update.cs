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
        Closed += (_, _) => _updateCancellation.Cancel();
        if (Environment.GetEnvironmentVariable("LIVECG_DISABLE_WORKSPACE_RESTORE") == "1") return;

        var appDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var install = appDirectory.Parent?.FullName;
        var installer = Path.Combine(appDirectory.FullName, "InstallUpdate.ps1");
        if (install is null || appDirectory.Name != "app" || !File.Exists(installer) ||
            !File.Exists(Path.Combine(install, "LiveCG-EFF.exe"))) return;

        string? staging = null;
        var agreed = false;
        var token = _updateCancellation.Token;
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
            agreed = MessageBox.Show(this,
                $"v{version} 업데이트가 있습니다. 현재 설치 폴더에 설치하고 LiveCG-EFF를 다시 시작할까요?",
                "LiveCG-EFF 업데이트", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
            if (!agreed || token.IsCancellationRequested) return;

            staging = Path.Combine(install, ".livecg-update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            var archive = Path.Combine(staging, "release.zip");
            var extracted = Path.Combine(staging, "release");
            Notify($"v{version} 업데이트 다운로드 중…");
            using (var download = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token))
            {
                download.EnsureSuccessStatusCode();
                await using var output = File.Create(archive);
                using var downloadTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                downloadTimeout.CancelAfter(TimeSpan.FromMinutes(15));
                await download.Content.CopyToAsync(output, downloadTimeout.Token);
            }
            if (new FileInfo(archive).Length != asset.GetProperty("size").GetInt64())
                throw new InvalidDataException("다운로드한 ZIP의 크기가 일치하지 않습니다.");
            await using (var stream = File.OpenRead(archive))
            {
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
                if (!actual.Equals(digest[7..], StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("다운로드한 ZIP의 SHA-256이 일치하지 않습니다.");
            }
            await Task.Run(() => ZipFile.ExtractToDirectory(archive, extracted), token);
            File.Delete(archive);
            var bundle = Path.Combine(extracted, $"LiveCG-EFF-{version}");
            if (FileVersionInfo.GetVersionInfo(Path.Combine(bundle, "app", "LiveCG-EFF.exe")).ProductVersion?.Split('+')[0] != version.ToString() ||
                !File.Exists(Path.Combine(bundle, "LiveCG-EFF.exe")))
                throw new InvalidDataException("업데이트 ZIP의 프로그램 버전이나 구조를 확인할 수 없습니다.");
            var helper = Path.Combine(staging, "InstallUpdate.ps1");
            await File.WriteAllTextAsync(helper, await File.ReadAllTextAsync(installer, token), new UTF8Encoding(true), token);
            if (token.IsCancellationRequested) return;
            if (_busy) { Notify("작업을 마친 뒤 앱을 다시 열면 업데이트할 수 있습니다."); return; }

            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = install
            };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", helper,
                "-ParentId", Environment.ProcessId.ToString(), "-Bundle", bundle })
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
            if (staging is not null)
            {
                try { Directory.Delete(staging, recursive: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
