using System.Diagnostics;
using System.Windows.Threading;
using LiveCG.Core;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace LiveCG.App;

internal sealed class BlurFocusPreview : SKElement, IDisposable
{
    private readonly SKImage _image;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _clock = new();
    private readonly Dictionary<Guid, double> _from = new();
    private SKImage? _blurred;
    private double _cachedAmount = -1;
    private double _duration = .45;
    private bool _shown;
    private double _blurFrom;
    public bool WholeFrame { get; set; }
    public EffectRegion? BackgroundRegion { get; set; }
    public IReadOnlyList<(EffectRegion Region, BlurFocusStep Step)> Steps { get; set; } = [];
    public double BlurAmount { get; set; }
    public Guid? Active { get; private set; }
    public bool IsClean { get; private set; } = true;

    public BlurFocusPreview(ImageJob job)
    {
        using var source = SourceImage.Read(job);
        var ratio = Math.Min(1d, 1024d / Math.Max(source.Width, source.Height));
        using var scaled = new SKBitmap(Math.Max(1, (int)Math.Round(source.Width * ratio)),
            Math.Max(1, (int)Math.Round(source.Height * ratio)), SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(scaled))
        using (var image = SKImage.FromBitmap(source))
            canvas.DrawImage(image, SKRect.Create(scaled.Width, scaled.Height), new SKSamplingOptions(SKFilterMode.Linear));
        _image = SKImage.FromBitmap(scaled);
        SourceRatio = ratio;
        _timer.Tick += (_, _) =>
        {
            if (Progress >= 1) _timer.Stop();
            InvalidateVisual();
        };
    }

    private double SourceRatio { get; }
    private double Progress => _clock.IsRunning ? Math.Clamp(_clock.Elapsed.TotalSeconds / _duration, 0, 1) : 1;
    private double Weight(Guid id, double eased) =>
        _from.GetValueOrDefault(id) * (1 - eased) + (Active == id ? eased : 0);
    private static double Ease(double p) => p * p * (3 - 2 * p);
    private double BlurWeight(double eased) => _blurFrom * (1 - eased) + (IsClean ? 0 : eased);

    public void Select(Guid? next, double duration, bool animate, bool clean = false)
    {
        if (_shown && next == Active && clean == IsClean && animate) return;
        var progress = Ease(Progress);
        _blurFrom = BlurWeight(progress);
        var previous = Steps.ToDictionary(step => step.Step.RegionId, step => Weight(step.Step.RegionId, progress));
        _from.Clear();
        foreach (var (key, weight) in previous)
            if (animate && weight > 0) _from.Add(key, weight);
        Active = next;
        IsClean = clean;
        _shown = true;
        _duration = duration;
        if (animate) { _clock.Restart(); _timer.Start(); }
        else { _clock.Reset(); _timer.Stop(); }
        InvalidateVisual();
    }

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        base.OnPaintSurface(e);
        var canvas = e.Surface.Canvas;
        canvas.Clear(new SKColor(22, 27, 34));
        var fit = Math.Min((float)e.Info.Width / _image.Width, (float)e.Info.Height / _image.Height);
        canvas.Translate((e.Info.Width - _image.Width * fit) / 2, (e.Info.Height - _image.Height * fit) / 2);
        canvas.Scale(fit);
        canvas.SaveLayer();
        canvas.DrawImage(_image, 0, 0);
        if ((!WholeFrame && BackgroundRegion is null) || !_shown || (IsClean && Progress >= 1))
        {
            canvas.Restore();
            return;
        }
        var eased = Ease(Progress);
        var amount = BlurAmount * BlurWeight(eased);
        if (amount > 0)
        {
            canvas.Save();
            if (!WholeFrame && BackgroundRegion is { } background)
            {
                using var path = PathFor(background);
                canvas.ClipPath(path, antialias: true);
            }
            using var blend = new SKPaint { BlendMode = SKBlendMode.SrcATop };
            canvas.DrawImage(BlurredImage(amount), 0, 0, blend);
            canvas.Restore();
        }
        foreach (var (region, step) in Steps.OrderBy(step => step.Step.RegionId == Active))
        {
            var weight = Weight(step.RegionId, eased);
            if (weight <= 0) continue;
            var scale = (float)(1 + (step.Scale - 1) * weight);
            var bounds = Bounds(region);
            canvas.Save();
            canvas.Scale(scale, scale, bounds.MidX, bounds.MidY);
            using var path = PathFor(region);
            canvas.ClipPath(path, antialias: true);
            using var paint = new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Round(255 * weight)) };
            canvas.DrawImage(_image, 0, 0, paint);
            canvas.Restore();
        }
        canvas.Restore();
    }

    private SKRect Bounds(EffectRegion region) => region.Bounds.Pixels(_image.Width, _image.Height);
    private SKPath PathFor(EffectRegion region) => RegionMask.Create(region.Shape, Bounds(region), region.Points);

    private SKImage BlurredImage(double amount)
    {
        if (_blurred is not null && _cachedAmount == amount) return _blurred;
        using var surface = SKSurface.Create(new SKImageInfo(_image.Width, _image.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.Transparent);
        var sigma = (float)(amount * SourceRatio);
        using var blur = SKImageFilter.CreateBlur(sigma, sigma, SKShaderTileMode.Decal);
        using var paint = new SKPaint { ImageFilter = blur };
        surface.Canvas.DrawImage(_image, 0, 0, paint);
        var replacement = surface.Snapshot();
        _blurred?.Dispose();
        _blurred = replacement;
        _cachedAmount = amount;
        return replacement;
    }

    public void Dispose()
    {
        _timer.Stop();
        _blurred?.Dispose();
        _image.Dispose();
    }
}
