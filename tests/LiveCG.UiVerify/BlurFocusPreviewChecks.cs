using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using LiveCG.App;
using LiveCG.Core;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

internal static class BlurFocusPreviewChecks
{
    public static void Run(string folder, Action<bool, string> check)
    {
        var path = Path.Combine(folder, "preview-blur.png");
        using var image = new SKBitmap(160, 100);
        for (var y = 0; y < image.Height; y++)
            for (var x = 0; x < image.Width; x++)
                image.SetPixel(x, y, (x / 2 + y / 2) % 2 == 0 ? SKColors.White : SKColors.Black);
        using (var canvas = new SKCanvas(image))
        using (var paint = new SKPaint { Color = SKColors.Red }) canvas.DrawRect(20, 35, 28, 30, paint);
        SourceImage.SavePng(image, path);
        var bg = new EffectRegion { Bounds = new(.05, .1, .9, .8) };
        var first = new EffectRegion { Bounds = new(.125, .35, .175, .3) };
        var second = new EffectRegion { Bounds = new(.4, .35, .175, .3) };
        var settings = new BlurFocus { BackgroundId = bg.Id, BlurPixels = 0, Seconds = .1, Steps = [new(first.Id), new(second.Id)] };
        var job = new ImageJob { SourcePath = path, Width = 160, Height = 100, Regions = [bg, first, second], BlurFocus = settings };
        var type = typeof(MainWindow).Assembly.GetType("LiveCG.App.BlurFocusWindow")!;
        var window = (Window)Activator.CreateInstance(type, job, bg.Id)!;
        var preview = type.GetField("_preview", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
        var previewType = preview.GetType();
        var active = previewType.GetProperty("Active")!;
        var controls = Descendants(window).ToArray();
        void Click(string label) => controls.OfType<Button>().Single(b => Equals(b.Content, label))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        SKColor Pixel(int x, int y)
        {
            var info = new SKImageInfo(160, 100);
            using var surface = SKSurface.Create(info);
            previewType.GetMethod("OnPaintSurface", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(preview, [new SKPaintSurfaceEventArgs(surface, info)]);
            using var result = surface.Snapshot();
            using var bitmap = SKBitmap.FromImage(result);
            return bitmap.GetPixel(x, y);
        }
        try
        {
            check((bool)previewType.GetProperty("IsClean")!.GetValue(preview)!, "dialog starts on the clean original");
            Click("다음 →");
            check(Equals(active.GetValue(preview), first.Id), "next starts the first emphasis");
            Thread.Sleep(130);
            controls.OfType<Slider>().First().Value = 8;
            var number = controls.OfType<TextBox>().First();
            check(number.Text == "8" && Pixel(52, 24).Red is > 50 and < 210, "blur slider immediately changes rendered pixels and numeric input");
            number.Text = "0";
            check(Pixel(52, 24).Red == image.GetPixel(52, 24).Red, "precise blur input immediately clears blur");
            var strength = (ComboBox)type.GetField("_strength", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
            strength.SelectedIndex = 4;
            check(Pixel(17, 50).Red > 240 && Pixel(17, 50).Green < 15, "strength change immediately enlarges selected pixels");
            Click("다음 →");
            check(Equals(active.GetValue(preview), second.Id), "next previews the second area");
            Click("← 이전");
            check(Equals(active.GetValue(preview), first.Id), "previous returns to first area");
            Click("전체 블러");
            check(active.GetValue(preview) is null && settings.BlurPixels == 0 && settings.Steps[0].Strength == StrengthLevel.Medium,
                "full blur preview leaves saved settings unchanged");
        }
        finally { window.Close(); }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
