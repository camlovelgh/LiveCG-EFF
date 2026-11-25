#if LIVE_CG_STARTUP_DIAGNOSTICS
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using LiveCG.Core;

namespace LiveCG.App;

internal static class StartupDiagnostics
{
    internal static bool Enabled => true;
    private static readonly object Gate = new();
    private static string? _logPath;

    internal static void Begin(Application app)
    {
        Write($"LiveCG-EFF v{ProductInfo.Version} · SunjooAn · 시작 진단\n"
            + $"OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})\n"
            + $"Process: {RuntimeInformation.ProcessArchitecture}; {RuntimeInformation.FrameworkDescription}\n"
            + $"Application folder: {AppContext.BaseDirectory}");
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Write($"Unhandled exception (terminating: {args.IsTerminating}):\n{args.ExceptionObject}");
        app.DispatcherUnhandledException += (_, args) =>
        {
            Write($"WPF exception:\n{args.Exception}");
            var path = _logPath ?? "로그를 기록할 수 없습니다.";
            MessageBox.Show($"{args.Exception.GetBaseException().Message}\n\n진단 기록\n{path}",
                $"LiveCG-EFF 시작 진단 · {ProductInfo.Credit}", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        EventManager.RegisterClassHandler(typeof(MainWindow), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((_, _) => Write("MainWindow loaded.")));
    }

    private static void Write(string message)
    {
        lock (Gate)
        {
            var record = $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}";
            if (_logPath is not null && TryAppend(_logPath, record)) return;

            var name = $"startup-{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}.log";
            foreach (var directory in new[] { Path.Combine(DocumentStore.DataDirectory, "Diagnostics"),
                Path.Combine(Path.GetTempPath(), "LiveCG-EFF", "Diagnostics") })
            {
                var path = Path.Combine(directory, name);
                if (!TryAppend(path, record)) continue;
                _logPath = path;
                return;
            }
            _logPath = null;
        }
    }

    private static bool TryAppend(string path, string text)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, text);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
#endif
