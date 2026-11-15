using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using LiveCG.Core;

namespace LiveCG.App;

internal sealed class BlurFocusWindow : Window
{
    private sealed record RegionChoice(EffectRegion Region, string Label);
    private sealed record StrengthChoice(StrengthLevel Value, string Label);
    private readonly RegionChoice[] _regions;
    private readonly List<BlurFocusStep> _steps;
    private readonly CheckBox _wholeFrame = new() { Content = "전체 화면 블러" };
    private readonly ComboBox _background = new() { DisplayMemberPath = "Label" };
    private readonly ComboBox _candidate = new() { DisplayMemberPath = "Label" };
    private readonly ComboBox _strength = new() { DisplayMemberPath = "Label" };
    private readonly ListBox _order = new() { MinHeight = 130, MaxHeight = 220 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly BlurFocusPreview _preview;
    private readonly TextBlock _previewStatus = new() { TextWrapping = TextWrapping.Wrap };
    private ReadNumber? _readBlur, _readSeconds;
    private bool _refreshing;
    private bool _loading;
    public BlurFocus? Result { get; private set; }

    public BlurFocusWindow(ImageJob job, Guid? selected)
    {
        Title = "순차 블러 강조 · " + ProductInfo.Credit;
        Width = 980;
        MinWidth = 740;
        Height = 650;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var settings = job.BlurFocus;
        _regions = job.Regions.Select((r, i) => new RegionChoice(r, $"{i + 1}. {r.Name}"))
            .Where(choice => LayerGraph.Visible(choice.Region, job.Regions)).ToArray();
        _steps = settings is null ? [] : [.. settings.Steps];
        var panel = new StackPanel { Margin = new Thickness(16) };
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(350) });
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        layout.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var previewPanel = new DockPanel { Margin = new Thickness(8, 16, 16, 16) };
        Grid.SetColumn(previewPanel, 1);
        layout.Children.Add(previewPanel);
        var controls = new StackPanel();
        DockPanel.SetDock(controls, Dock.Bottom);
        previewPanel.Children.Add(controls);
        _preview = new BlurFocusPreview(job);
        Closed += (_, _) => _preview.Dispose();
        previewPanel.Children.Add(_preview);
        controls.Children.Add(_previewStatus);
        var previewActions = Row(controls);
        Add(previewActions, "← 이전", () => Navigate(-1));
        Add(previewActions, "다음 →", () => Navigate(1));
        Add(previewActions, "전체 블러", () => ShowFocus(null, true));
        Add(previewActions, "처음부터", () => ShowFocus(null, true, clean: true));
        Content = layout;
        panel.Children.Add(Label("블러 범위를 선택한 뒤 강조 영역을 원하는 순서로 추가하세요."));
        var playBeforeFocus = new ComboBox { ItemsSource = new[] { "원본 대기", "일반 타임라인 재생" },
            SelectedIndex = settings?.PlayTimelineBeforeFocus == true ? 1 : 0 };
        panel.Children.Add(Label("강조 전 재생"));
        panel.Children.Add(playBeforeFocus);
        panel.Children.Add(Label("일반 재생에서는 강조를 마치면 반복 구간부터 재생합니다. 반복 구간이 없으면 마지막 장면을 표시합니다."));
        panel.Children.Add(Label("블러 영역"));
        panel.Children.Add(_wholeFrame);
        panel.Children.Add(_background);
        _wholeFrame.IsChecked = settings?.WholeFrame ?? _regions.Length < 2;
        _background.IsEnabled = _wholeFrame.IsChecked != true;
        _background.ItemsSource = _regions;
        _background.SelectedItem = _regions.FirstOrDefault(r => r.Region.Id == (settings?.BackgroundId ?? selected))
            ?? _regions.FirstOrDefault();
        var blur = NumberRow(panel, "블러량 (원본 px)", 0, 64, settings?.BlurPixels ?? 12, 1);
        var seconds = NumberRow(panel, "전환 길이 (초)", .1, 5, settings?.Seconds ?? .45, .05);
        _readBlur = blur.Read;
        _readSeconds = seconds.Read;
        blur.Input.TextChanged += (_, _) => UpdatePreview();
        panel.Children.Add(Label("안쪽 강조 영역"));
        panel.Children.Add(_candidate);
        var add = Row(panel);
        Add(add, "순서에 추가", () =>
        {
            if (_candidate.SelectedItem is RegionChoice choice && !_steps.Any(s => s.RegionId == choice.Region.Id))
            {
                _steps.Add(new(choice.Region.Id));
                Refresh(_steps.Count - 1);
                ShowFocus(choice.Region.Id, false);
            }
        });
        panel.Children.Add(_order);
        var order = Row(panel);
        Add(order, "위로", () => Move(-1));
        Add(order, "아래로", () => Move(1));
        Add(order, "목록에서 제거", () =>
        {
            var i = _order.SelectedIndex;
            if (i < 0) return;
            _steps.RemoveAt(i);
            Refresh(Math.Min(i, _steps.Count - 1));
        });
        panel.Children.Add(Label("선택한 강조 영역의 확대 강도"));
        panel.Children.Add(_strength);
        _strength.ItemsSource = new[] { StrengthLevel.VeryWeak, StrengthLevel.Weak, StrengthLevel.Medium, StrengthLevel.Strong, StrengthLevel.VeryStrong }
            .Select(value => new StrengthChoice(value, ProductInfo.StrengthName(value))).ToArray();
        _order.SelectionChanged += (_, _) =>
        {
            SyncStrength();
            if (!_refreshing && _order.SelectedIndex >= 0) ShowFocus(_steps[_order.SelectedIndex].RegionId, false);
        };
        _strength.SelectionChanged += (_, _) =>
        {
            var i = _order.SelectedIndex;
            if (_loading || i < 0 || _strength.SelectedItem is not StrengthChoice choice) return;
            _steps[i] = _steps[i] with { Strength = choice.Value };
            Refresh(i);
            ShowFocus(_steps[i].RegionId, false);
        };
        _wholeFrame.Checked += (_, _) => ChangeScope();
        _wholeFrame.Unchecked += (_, _) => ChangeScope();
        _background.SelectionChanged += (_, _) => { RefreshCandidates(); UpdatePreview(); };
        panel.Children.Add(Label("OBS 도구 → 스크립트에서 LiveCG 스크립트를 추가한 뒤 설정 → 단축키에서 키를 지정하세요."));
        panel.Children.Add(_status);
        var actions = Row(panel);
        Add(actions, "적용", () =>
        {
            var wholeFrame = _wholeFrame.IsChecked == true;
            var bg = _background.SelectedItem as RegionChoice;
            if ((!wholeFrame && bg is null) ||
                !blur.Read(out var amount) || !seconds.Read(out var time))
            {
                _status.Text = "블러 영역과 숫자 입력 범위를 확인하세요.";
                return;
            }
            if (_steps.Count == 0 || _steps.Any(step => !Candidates().Any(r => r.Region.Id == step.RegionId)))
            {
                _status.Text = "블러 범위에 포함된 강조 영역을 한 개 이상 추가하세요.";
                return;
            }
            Result = new() { WholeFrame = wholeFrame, BackgroundId = wholeFrame ? Guid.Empty : bg!.Region.Id, BlurPixels = amount, Seconds = time, PlayTimelineBeforeFocus = playBeforeFocus.SelectedIndex == 1, Steps = [.. _steps] };
            DialogResult = true;
        });
        Add(actions, "효과 해제", () => { Result = null; DialogResult = true; });
        Add(actions, "취소", () => DialogResult = false);
        Refresh(0);
        ShowFocus(null, false, clean: true);
    }

    private delegate bool ReadNumber(out double value);
    private static (ReadNumber Read, TextBox Input) NumberRow(Panel panel, string title, double min, double max, double value, double step)
    {
        panel.Children.Add(Label(title));
        var row = new DockPanel();
        var number = new TextBox { Width = 75, Text = value.ToString("0.##", CultureInfo.CurrentCulture) };
        DockPanel.SetDock(number, Dock.Right);
        row.Children.Add(number);
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, TickFrequency = step, IsSnapToTickEnabled = true };
        row.Children.Add(slider);
        panel.Children.Add(row);
        bool syncing = false;
        bool Read(out double parsed) => double.TryParse(number.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed)
            && double.IsFinite(parsed) && parsed >= min && parsed <= max;
        slider.ValueChanged += (_, _) => { if (!syncing) number.Text = slider.Value.ToString("0.##", CultureInfo.CurrentCulture); };
        number.TextChanged += (_, _) =>
        {
            if (!Read(out var parsed)) return;
            syncing = true;
            slider.Value = parsed;
            syncing = false;
        };
        return (Read, number);
    }

    private static TextBlock Label(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 4) };
    private static WrapPanel Row(Panel parent)
    {
        var row = new WrapPanel();
        parent.Children.Add(row);
        return row;
    }
    private static void Add(Panel panel, string label, Action click)
    {
        var button = new Button { Content = label, Margin = new Thickness(2) };
        button.Click += (_, _) => click();
        panel.Children.Add(button);
    }
    private void ChangeScope()
    {
        _background.IsEnabled = _wholeFrame.IsChecked != true;
        RefreshCandidates();
        UpdatePreview();
    }
    private IEnumerable<RegionChoice> Candidates() => _wholeFrame.IsChecked == true ? _regions : _background.SelectedItem is RegionChoice bg && bg.Region.Enabled
        ? _regions.Where(r => r != bg && r.Region.Enabled && BlurFocus.Inside(r.Region.Bounds, bg.Region.Bounds))
        : [];
    private void RefreshCandidates()
    {
        _candidate.ItemsSource = Candidates().Where(r => _steps.All(s => s.RegionId != r.Region.Id)).ToArray();
        _candidate.SelectedIndex = 0;
    }
    private void Refresh(int index)
    {
        _refreshing = true;
        _order.ItemsSource = _steps.Select((s, i) =>
            $"{i + 1}. {_regions.FirstOrDefault(r => r.Region.Id == s.RegionId)?.Label ?? "삭제된 영역"} · {ProductInfo.StrengthName(s.Strength)}").ToArray();
        _order.SelectedIndex = index >= 0 && index < _steps.Count ? index : -1;
        RefreshCandidates();
        SyncStrength();
        _refreshing = false;
        UpdatePreview();
    }
    private (EffectRegion Region, BlurFocusStep Step)[] PreviewSteps()
    {
        var candidates = Candidates().ToDictionary(choice => choice.Region.Id, choice => choice.Region);
        return _steps.Where(step => candidates.ContainsKey(step.RegionId))
            .Select(step => (candidates[step.RegionId], step)).ToArray();
    }
    private void UpdatePreview()
    {
        _preview.WholeFrame = _wholeFrame.IsChecked == true;
        _preview.BackgroundRegion = (_background.SelectedItem as RegionChoice)?.Region;
        _preview.Steps = PreviewSteps();
        if (_readBlur is not null && _readBlur(out var amount)) _preview.BlurAmount = amount;
        if (_preview.Active is { } active && !_preview.Steps.Any(item => item.Step.RegionId == active))
            _preview.Select(null, .45, false, clean: true);
        _preview.InvalidateVisual();
        var position = Array.FindIndex(_preview.Steps.ToArray(), item => item.Step.RegionId == _preview.Active);
        _previewStatus.Text = _preview.IsClean ? "미리보기 · 원본" :
            position < 0 ? "미리보기 · 전체 블러" : $"미리보기 · {position + 1} / {_preview.Steps.Count}";
    }
    private void ShowFocus(Guid? id, bool animate, bool clean = false)
    {
        UpdatePreview();
        if (id.HasValue && !_preview.Steps.Any(item => item.Step.RegionId == id)) return;
        var duration = _readSeconds is not null && _readSeconds(out var time) ? time : .45;
        _preview.Select(id, duration, animate, clean);
        if (id is { } active)
        {
            _refreshing = true;
            _order.SelectedIndex = _steps.FindIndex(step => step.RegionId == active);
            _refreshing = false;
        }
        UpdatePreview();
    }
    private void Navigate(int direction)
    {
        var steps = PreviewSteps();
        var index = Array.FindIndex(steps, item => item.Step.RegionId == _preview.Active) + direction;
        if (index >= 0 && index < steps.Length) ShowFocus(steps[index].Step.RegionId, true);
        else if (_preview.Active.HasValue) ShowFocus(null, true, clean: true);
    }
    private void SyncStrength()
    {
        _loading = true;
        var i = _order.SelectedIndex;
        _strength.IsEnabled = i >= 0;
        _strength.SelectedItem = i >= 0 ? _strength.Items.OfType<StrengthChoice>().First(c => c.Value == _steps[i].Strength) : null;
        _loading = false;
    }
    private void Move(int offset)
    {
        var index = _order.SelectedIndex;
        var next = index + offset;
        if (index < 0 || next < 0 || next >= _steps.Count) return;
        (_steps[index], _steps[next]) = (_steps[next], _steps[index]);
        Refresh(next);
    }
}
