using System.Windows;
using System.Windows.Controls;
using LiveCG.Core;

namespace LiveCG.App;

internal sealed class UpdateProgressWindow : Window
{
    private readonly TextBlock _status = new() { Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar _bar = new() { Height = 16, Maximum = 100, IsIndeterminate = true };

    internal UpdateProgressWindow(Window owner, Version version)
    {
        Owner = owner;
        Title = $"LiveCG-EFF 업데이트 · {ProductInfo.Credit}";
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Width = 420;

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = $"v{version} 업데이트 준비", Margin = new Thickness(0, 0, 0, 12), FontWeight = FontWeights.SemiBold });
        panel.Children.Add(_status);
        panel.Children.Add(_bar);
        Content = panel;
        ShowPhase("다운로드 연결 중…");
    }

    internal void ShowPhase(string text, double? percentage = null)
    {
        _status.Text = text;
        _bar.IsIndeterminate = percentage is null;
        if (percentage is double value) _bar.Value = value;
    }
}
