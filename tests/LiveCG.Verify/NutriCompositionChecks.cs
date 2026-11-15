using System.Text.Json;
using LiveCG.Core;
using SkiaSharp;

internal static class NutriCompositionChecks
{
    public static async Task Run(string root, Action<bool, string> check)
    {
        var sourceFolder = Path.Combine(root, "artifacts", "nutri-alpha", "scoped");
        var target = Directory.CreateDirectory(Path.Combine(root, "artifacts", "nutri-composition")).FullName;
        var data = File.ReadAllText(Path.Combine(sourceFolder, "data.js")).Trim();
        using var document = JsonDocument.Parse(data[(data.IndexOf('=')+1)..].TrimEnd(';'));
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var regions = document.RootElement.GetProperty("regions").Deserialize<List<EffectRegion>>(options)!;
        var focus = document.RootElement.GetProperty("blurFocus").Deserialize<BlurFocus>(options)!;
        var prices = regions.Where(region => region.Clips.Count > 0).ToList();
        var cards = focus.Steps.Select(step => regions.Single(region => region.Id == step.RegionId)).ToList();
        var background = regions.Single(region => region.Id == focus.BackgroundId);
        foreach (var region in regions)
        {
            region.ParentId = null;
            region.Role = RegionRole.Group;
        }
        var source = Path.Combine(sourceFolder, "original.png");
        var job = new ImageJob
        {
            SourcePath = source, Width = 1080, Height = 1920,
            Regions = [..prices, ..cards, background],
            Playback = new() { Enabled = true, IntroEnd = 1, LoopEnd = 2, ExitEnabled = false, LoopRest = 1 },
            BlurFocus = focus
        };
        using var bitmap = SourceImage.Read(job);
        using var renderer = new EffectScene(bitmap);
        var video = new VideoOptions { Fps = 30 };
        foreach (var time in new[] { .4, 1.4 })
        {
            using var baseline = renderer.Render(prices, video, time);
            using var withCards = renderer.Render(job.Regions, video, time);
            check(baseline.Bytes.SequenceEqual(withCards.Bytes), $"Nutri 6 at {time:F1}s: idle cards do not change the rendered frame");
            check(!withCards.Bytes.SequenceEqual(bitmap.Bytes), $"Nutri 6 at {time:F1}s: price effect renders");
            check(withCards.GetPixel(500, 1099) == bitmap.GetPixel(500, 1099) &&
                withCards.GetPixel(500, 1100).Alpha == 0, "Nutri 6: pink edge and transparent area remain intact");
        }
        var originalClips = prices[0].Clips;
        prices[0].Clips = [new() { Duration = 1, Options = new() { Kind = EffectKind.Zoom } }];
        using (var baseline = renderer.Render(prices, video, .4))
        using (var withCards = renderer.Render(job.Regions, video, .4))
            check(baseline.Bytes.SequenceEqual(withCards.Bytes), "Nutri 6: idle cards stay out of the Zoom composition too");
        prices[0].Clips = originalClips;
        await new BrowserExporter().ExportAsync(job, video, Path.Combine(target, "scoped"), true, null, default, compact: true);
        job.BlurFocus = focus with { WholeFrame = true, BackgroundId = Guid.Empty };
        job.Regions.Remove(background);
        await new BrowserExporter().ExportAsync(job, video, Path.Combine(target, "whole"), true, null, default, compact: true);
    }
}
