using System.Diagnostics;
using System.Text.Json;
using LiveCG.Core;
using SkiaSharp;

internal static class NewEffectsChecks
{
    private const int CellWidth = 120, CellHeight = 92, Columns = 6;
    private static readonly int[] Frames = [2, 7, 15, 23, 32, 42];

    private static List<(string Name, List<EffectClip> Clips)> Cases(string folder)
    {
        var effects=ProductInfo.SelectableEffects.Where(ProductInfo.IsNewEffect)
            .Select(kind=>(kind.ToString(),new List<EffectClip>{new(){Options=new(){Kind=kind}}})).ToList();
        var presetFolder=Path.GetFullPath(Path.Combine(folder,"..","..","effects"));
        foreach(var path in Directory.EnumerateFiles(presetFolder,"*.lcgeffect.json").Order())
        {
            var asset=DocumentStore.Load<EffectAsset>(path);
            effects.Add((asset.Name,AssetStore.Instantiate(asset,0,30,bundled:true)));
        }
        return effects;
    }

    internal static async Task Create(string folder, Action<bool,string> check)
    {
        Directory.CreateDirectory(folder);
        var cases = Cases(folder);
        var width = CellWidth * Columns;
        var height = CellHeight * ((cases.Count + Columns - 1) / Columns);
        using var source = new SKBitmap(width, height);
        source.Erase(SKColors.Transparent);
        using var canvas = new SKCanvas(source);
        using var paint = new SKPaint { IsAntialias = true, Color = new(60, 171, 221) };
        var regions = new List<EffectRegion>();
        for (var index = 0; index < cases.Count; index++)
        {
            var (left, top) = Cell(index);
            var bounds = SKRect.Create(left + 19, top + 26, 82, 40);
            canvas.DrawRoundRect(SKRect.Create(bounds.Left + 4, bounds.Top + 5, 74, 30), 6, 6, paint);
            using var ink = new SKPaint { IsAntialias = true, Color = SKColors.White };
            for (var bar = 0; bar < 6; bar++)
                canvas.DrawRect(SKRect.Create(bounds.Left + 12 + bar * 10, bounds.Top + 11, 4, 17), ink);
            regions.Add(new()
            {
                Bounds = NormalizedRect.FromPixels(bounds,width,height), Background = BackgroundMode.Transparent,
                Clips = cases[index].Clips
            });
        }
        var path = Path.Combine(folder, "sample.png");
        SourceImage.SavePng(source,path);
        var job = new ImageJob { SourcePath = path, Width = width, Height = height, Regions = regions };
        var video = new VideoOptions { Fps = 30, EndHold = 1, Crf = 10 };
        using var scene = new EffectScene(source);
        foreach (var frame in Frames)
        {
            using var bitmap = scene.Render(regions,video,frame / 30d);
            SourceImage.SavePng(bitmap,Path.Combine(folder,$"native-{frame}.png"));
        }
        using(var first = SKBitmap.Decode(Path.Combine(folder,"native-2.png")))
        using(var second = SKBitmap.Decode(Path.Combine(folder,"native-15.png")))
        foreach (var (name,index) in cases.Select((item,index)=>(item.Name,index)))
        {
            check(Difference(first,second,index,false) > .1,$"{name} changes between native frames");
        }
        await new BrowserExporter().ExportAsync(job,video,Path.Combine(folder,"browser"),true,null,CancellationToken.None,true);
        await new WebmExporter(RuntimePaths.Ffmpeg).ExportAsync(job,video,Path.Combine(folder,"sample.webm"),true,null,CancellationToken.None);
        using var ffmpeg = new Process { StartInfo = new(RuntimePaths.Ffmpeg) { UseShellExecute = false, CreateNoWindow = true } };
        foreach(var arg in new[]{"-y","-v","error","-c:v","libvpx-vp9","-i",Path.Combine(folder,"sample.webm"),"-frames:v",(Frames[^1]+1).ToString(),Path.Combine(folder,"webm-%d.png")})
            ffmpeg.StartInfo.ArgumentList.Add(arg);
        ffmpeg.Start();
        await ffmpeg.WaitForExitAsync();
        check(ffmpeg.ExitCode==0,"transparent WebM decodes");
        foreach(var frame in Frames)
            Compare(folder,Path.Combine(folder,$"webm-{frame+1}.png"),frame,false,check,6);
        await File.WriteAllTextAsync(Path.Combine(folder,"fixture.json"),JsonSerializer.Serialize(new {width,height,frames=Frames,cases=cases.Count}));
        VerifyCompatibility(folder,check);
    }

    private static void VerifyCompatibility(string folder,Action<bool,string> check)
    {
        var oldData=Environment.GetEnvironmentVariable("LIVECG_DATA_DIR");
        Environment.SetEnvironmentVariable("LIVECG_DATA_DIR",Path.Combine(folder,"legacy-user"));
        try
        {
            Directory.CreateDirectory(AssetStore.DirectoryPath);
            var invalid=Path.Combine(AssetStore.DirectoryPath,"unreadable.lcgeffect.json");
            File.WriteAllText(invalid,"""{"Clips":[{"Options":{"Kind":"UnknownForeignEffect"}}]}""");
            var valid=EffectAsset.NewMotion();
            AssetStore.Save(valid);
            var assets=AssetStore.List(out var errors);
            check(assets.Any(asset=>asset.Id==valid.Id)&&errors.Any(error=>error.StartsWith("unreadable.lcgeffect.json")), "invalid user asset is skipped and named while valid assets remain");
            check(File.Exists(invalid),"unreadable asset is preserved");
            using var source=SKBitmap.Decode(Path.Combine(folder,"sample.png"));
            using var scene=new EffectScene(source);
            foreach(var kind in Enum.GetValues<EffectKind>().Where(ProductInfo.IsRetiredEffect))
            {
                var clip=new EffectClip { Options=new(){Kind=kind} };
                check(AssetStore.Instantiate(new(){Clips=[clip]},0,30).Count==0,$"{kind} is excluded when inserting an old preset");
                var path=Path.Combine(folder,"legacy-template.json");
                DocumentStore.Save(path,new TemplateDocument { Regions=[new(){Clips=[clip]}] });
                var loaded=DocumentStore.Load<TemplateDocument>(path);
                using var frame=scene.Render(loaded.Regions,new(),.35);
                check(loaded.Regions[0].Clips[0].Options.Kind==kind&&frame.Bytes.SequenceEqual(source.Bytes),
                    $"{kind} legacy clip loads without rendering");
            }
        }
        finally { Environment.SetEnvironmentVariable("LIVECG_DATA_DIR",oldData); }
    }

    internal static void Compare(string folder,string image,int frame,bool premultiplied,Action<bool,string> check,double tolerance=5)
    {
        using var expected=SKBitmap.Decode(Path.Combine(folder,$"native-{frame}.png"));
        using var actual=SKBitmap.Decode(image);
        check(actual is not null&&actual.Width==expected.Width&&actual.Height==expected.Height,"output matches source dimensions "+frame);
        if(actual is null||actual.Width!=expected.Width||actual.Height!=expected.Height)return;
        foreach(var (name,index) in Cases(folder).Select((item,index)=>(item.Name,index)))
        {
            var delta=Difference(expected,actual,index,premultiplied);
            check(delta<tolerance,$"{name} frame {frame}: premultiplied mean error {delta:F3}");
        }
        check(actual.GetPixel(0,0).Alpha==0,"untouched corner is transparent "+frame);
    }

    private static (int X,int Y) Cell(int index)=>(index%Columns*CellWidth,index/Columns*CellHeight);

    private static double Difference(SKBitmap expected,SKBitmap actual,int index,bool premultiplied)
    {
        var (left,top)=Cell(index);
        double total=0;
        for(var y=top;y<top+CellHeight;y++)
        for(var x=left;x<left+CellWidth;x++)
        {
            var a=expected.GetPixel(x,y);
            var b=actual.GetPixel(x,y);
            var aa=a.Alpha/255d;
            var ba=premultiplied?1:b.Alpha/255d;
            total+=Math.Abs(a.Alpha-b.Alpha)+Math.Abs(a.Red*aa-b.Red*ba)
                 +Math.Abs(a.Green*aa-b.Green*ba)+Math.Abs(a.Blue*aa-b.Blue*ba);
        }
        return total/(CellWidth*CellHeight*4);
    }
}
