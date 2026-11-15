using LiveCG.Core;
using SkiaSharp;
using System.Text.Json;

internal static class BlurFocusChecks
{
    public static async Task Create(string folder)
    {
        var fixture = Directory.CreateDirectory(Path.Combine(folder, "blur-focus-" + Guid.NewGuid().ToString("N"))).FullName;
        var source = Path.Combine(fixture, "순차 강조.png");
        using var image = new SKBitmap(160, 100);
        for (var y = 0; y < 100; y++)
            for (var x = 0; x < 160; x++)
                image.SetPixel(x, y, (x / 2 + y / 2) % 2 == 0 ? SKColors.White : SKColors.Black);
        using (var canvas = new SKCanvas(image))
        using (var paint = new SKPaint { Color = SKColors.Red })
        {
            canvas.DrawRect(20, 35, 28, 30, paint);
            paint.Color = SKColors.Lime;
            canvas.DrawRect(66, 35, 28, 30, paint);
            paint.Color = SKColors.Blue;
            canvas.DrawRect(112, 35, 28, 30, paint);
        }
        SourceImage.SavePng(image, source);
        var background = new EffectRegion { Bounds = new(.05, .1, .9, .8) };
        EffectRegion Area(int x) => new() { Bounds = new(x / 160d, .35, 28 / 160d, .30) };
        var first = Area(20);
        var second = Area(66);
        var third = Area(112);
        var job = new ImageJob
        {
            SourcePath = source, Width = 160, Height = 100,
            Playback = PlaybackSections.NewDefault(), Regions = [background, first, second, third],
            BlurFocus = new()
            {
                BackgroundId = background.Id, BlurPixels = 8, Seconds = 1,
                Steps = [new(first.Id, StrengthLevel.VeryStrong), new(second.Id, StrengthLevel.VeryStrong), new(third.Id)]
            }
        };
        var project = Path.Combine(fixture, "블러 검사.livecg.json");
        DocumentStore.Save(project, new ProjectDocument { Jobs = [job] });
        var restored = DocumentStore.Load<ProjectDocument>(project).Jobs.Single();
        if (restored.Snapshot().BlurFocus?.Steps.Count != 3) throw new InvalidDataException("Blur focus did not round-trip.");
        var entry = LinkedOutput.SourceTarget(restored, "");
        await LinkedOutput.ExportAsync(restored, new(), entry, default);
        var timelineFolder = Directory.CreateDirectory(Path.Combine(fixture, "timeline")).FullName;
        var timelineSource = Path.Combine(timelineFolder, "타임라인 강조.png");
        File.Copy(source, timelineSource);
        job.SourcePath = timelineSource;
        job.Playback = new() { Enabled = true, IntroEnd = 1.5, LoopEnd = 3.5, ExitEnabled = false };
        job.Regions[1].Clips.Add(new() { Duration = 1.5, Options = new() { Kind = EffectKind.ZoomIn } });
        job.Regions[2].Clips.Add(new() { Start = 1.5, Duration = 2, Options = new() { Kind = EffectKind.Glow } });
        job.BlurFocus = job.BlurFocus! with { PlayTimelineBeforeFocus = true };
        DocumentStore.Save(project, new ProjectDocument { Jobs = [job] });
        var timeline = DocumentStore.Load<ProjectDocument>(project).Jobs.Single();
        if (timeline.Snapshot().BlurFocus?.PlayTimelineBeforeFocus != true)
            throw new InvalidDataException("Timeline mode did not round-trip.");
        var timelineEntry = LinkedOutput.SourceTarget(timeline, "");
        await LinkedOutput.ExportAsync(timeline, new(), timelineEntry, default);
        File.WriteAllText(Path.Combine(folder, "blur-focus-test.json"), JsonSerializer.Serialize(new { entry, timelineEntry }));
    }
}
