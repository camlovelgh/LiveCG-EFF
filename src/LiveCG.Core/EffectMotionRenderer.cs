using SkiaSharp;

namespace LiveCG.Core;

internal static class EffectMotionRenderer
{
    private static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear);

    public static bool DrawEntrance(SKCanvas canvas,SKImage image,int width,int height,EffectClip clip,float progress,float power,SKPaint paint,float x=0,float y=0)
    {
        var p=Math.Clamp(progress,0,1);var w=(float)width;var h=(float)height;var bounds=SKRect.Create(x,y,w,h);
        switch(clip.Options.Kind)
        {
            case EffectKind.CardFlip:
            {
                var eased=1-MathF.Pow(1-p,3);var scale=Math.Max(.015f,MathF.Sin(eased*MathF.PI*.5f));
                canvas.Save();canvas.Translate(x+w*.5f,y);canvas.Scale(scale,1);canvas.Translate(-x-w*.5f,-y);
                canvas.DrawImage(image,bounds,Sampling,paint);canvas.Restore();return true;
            }
            case EffectKind.FocusPull:
            {
                var eased=1-MathF.Pow(1-p,3);var sigma=Math.Max(0,h*.055f*power*(1-eased));
                if(sigma<.15f)canvas.DrawImage(image,bounds,Sampling,paint);
                else{using var filter=SKImageFilter.CreateBlur(sigma,sigma);using var blurred=new SKPaint{Color=paint.Color,IsAntialias=true,ImageFilter=filter};canvas.DrawImage(image,bounds,Sampling,blurred);}return true;
            }
            case EffectKind.EchoTrail:
            {
                var eased=1-MathF.Pow(1-p,3);var travel=w*.13f*power*(1-eased);var original=paint.Color;
                for(var i=3;i>=1;i--){using var echo=new SKPaint{Color=SKColors.White.WithAlpha((byte)(original.Alpha*eased*(.11f+i*.055f))),IsAntialias=true};canvas.DrawImage(image,SKRect.Create(x-travel*i/3,y,w,h),Sampling,echo);}
                paint.Color=original.WithAlpha((byte)(original.Alpha*eased));canvas.DrawImage(image,bounds,Sampling,paint);paint.Color=original;return true;
            }
            case EffectKind.InkSpread:
            {
                var eased=1-MathF.Pow(1-p,2.2f);var max=MathF.Sqrt(w*w+h*h)*.55f;var radius=max*eased;
                using var ink=new SKPath();const int points=48;
                for(var i=0;i<points;i++){var angle=2*MathF.PI*i/points;var wobble=1+(.035f*MathF.Sin(i*2.17f)+.022f*MathF.Sin(i*4.91f))*power;var px=x+w*.5f+MathF.Cos(angle)*radius*wobble;var py=y+h*.5f+MathF.Sin(angle)*radius*wobble;if(i==0)ink.MoveTo(px,py);else ink.LineTo(px,py);}
                ink.Close();canvas.Save();canvas.ClipPath(ink,antialias:true);canvas.DrawImage(image,bounds,Sampling,paint);canvas.Restore();return true;
            }
            case EffectKind.CenterCurtain:
            {
                var eased=1-MathF.Pow(1-p,3);var visible=w*eased;canvas.Save();canvas.ClipRect(SKRect.Create(x+(w-visible)*.5f,y,visible,h));canvas.DrawImage(image,bounds,Sampling,paint);canvas.Restore();return true;
            }
            case EffectKind.VenetianShutter:
            {
                const int slats=7;var band=h/slats;
                for(var i=0;i<slats;i++)
                {
                    var local=Math.Clamp((p*1.35f-i*.075f)/.58f,0,1);if(local<=0)continue;
                    var open=band*local;var top=y+i*band+(band-open)*.5f;
                    canvas.Save();canvas.ClipRect(SKRect.Create(x,top,w,open));canvas.DrawImage(image,bounds,Sampling,paint);canvas.Restore();
                }return true;
            }
            case EffectKind.IrisOpen:
            {
                var eased=1-MathF.Pow(1-p,3);var radius=MathF.Sqrt(w*w+h*h)*.56f*eased;
                using var iris=new SKPath();iris.AddCircle(x+w*.5f,y+h*.5f,radius);canvas.Save();canvas.ClipPath(iris,antialias:true);canvas.DrawImage(image,bounds,Sampling,paint);canvas.Restore();return true;
            }
            case EffectKind.CharacterRotateRise:
                DrawCharacterRotateRise(canvas,image,width,height,p,paint,x,y);return true;
            case EffectKind.SplitConverge:
                DrawSplitConverge(canvas,image,width,height,p,paint,x,y);return true;
            case EffectKind.TileAssemble:
                DrawTileAssemble(canvas,image,width,height,p,power,paint,x,y);return true;
            default:return false;
        }
    }

    private static void DrawCharacterRotateRise(SKCanvas canvas,SKImage image,int width,int height,float progress,SKPaint paint,float x,float y)
    {
        var count=Math.Clamp((int)Math.Round(width/Math.Max(1,height*.62)),1,48);var visible=progress*count;var original=paint.Color;
        for(var i=0;i<count;i++)
        {
            var local=Math.Clamp(visible-i,0,1);if(local<=0)continue;var eased=1-MathF.Pow(1-local,3);
            var left=i*width/(float)count;var right=(i+1)*width/(float)count;var tileWidth=right-left;
            var offsetY=(1-eased)*height*.58f;var angle=(1-eased)*(i%2==0?-78:78);
            paint.Color=original.WithAlpha((byte)(original.Alpha*eased));canvas.Save();canvas.Translate(x+left+tileWidth*.5f,y+height*.5f+offsetY);canvas.RotateDegrees(angle);canvas.Translate(-tileWidth*.5f,-height*.5f);
            canvas.DrawImage(image,SKRect.Create(left,0,tileWidth,height),SKRect.Create(0,0,tileWidth,height),Sampling,paint);canvas.Restore();
        }
        paint.Color=original;
    }

    private static void DrawSplitConverge(SKCanvas canvas,SKImage image,int width,int height,float progress,SKPaint paint,float x,float y)
    {
        var eased=1-MathF.Pow(1-progress,3);var middle=width*.5f;var half=width-middle;var travel=width*(1-eased);var original=paint.Color;
        paint.Color=original.WithAlpha((byte)(original.Alpha*eased));
        canvas.DrawImage(image,SKRect.Create(0,0,middle,height),SKRect.Create(x-travel,y,middle,height),Sampling,paint);
        canvas.DrawImage(image,SKRect.Create(middle,0,half,height),SKRect.Create(x+middle+travel,y,half,height),Sampling,paint);
        paint.Color=original;
    }

    private static void DrawTileAssemble(SKCanvas canvas,SKImage image,int width,int height,float progress,float power,SKPaint paint,float x,float y)
    {
        const int columns=6,rows=4;var total=columns*rows;var original=paint.Color;
        for(var row=0;row<rows;row++)for(var column=0;column<columns;column++)
        {
            var index=row*columns+column;var order=(index*17+5)%total;var start=order/(float)total*.48f;var local=Math.Clamp((progress-start)/(.52f),0,1);if(local<=0)continue;
            var eased=1-MathF.Pow(1-local,3);var left=column*width/(float)columns;var right=(column+1)*width/(float)columns;var top=row*height/(float)rows;var bottom=(row+1)*height/(float)rows;
            var dx=((order*13%11)-5)*width*.045f*power*(1-eased);var dy=((order*7%9)-4)*height*.075f*power*(1-eased);var rotation=((order%2==0)?-1:1)*(24+(order%4)*5)*(1-eased);var scaleX=Math.Max(.025f,MathF.Sin((.08f+.92f*eased)*MathF.PI*.5f));
            var tileWidth=right-left;var tileHeight=bottom-top;paint.Color=original.WithAlpha((byte)(original.Alpha*eased));canvas.Save();canvas.Translate(x+left+tileWidth*.5f+dx,y+top+tileHeight*.5f+dy);canvas.RotateDegrees(rotation);canvas.Scale(scaleX,.78f+.22f*eased);canvas.Translate(-tileWidth*.5f,-tileHeight*.5f);
            canvas.DrawImage(image,SKRect.Create(left,top,tileWidth,tileHeight),SKRect.Create(0,0,tileWidth,tileHeight),Sampling,paint);canvas.Restore();
        }
        paint.Color=original;
    }

    public static void DrawTypewriter(SKCanvas canvas, SKBitmap bitmap, SKImage image, float progress, SKPaint paint, float x = 0, float y = 0)
    {
        var count = Math.Clamp((int)Math.Round(bitmap.Width / Math.Max(1, bitmap.Height * .62f)), 1, 48);
        var visible = Math.Clamp(progress, 0, 1) * count;
        for (var i = 0; i < count; i++)
        {
            var local = Math.Clamp(visible - i, 0, 1);
            if (local <= 0) continue;
            var left = i * bitmap.Width / (float)count;
            var right = (i + 1) * bitmap.Width / (float)count;
            var original = paint.Color;
            paint.Color = original.WithAlpha((byte)(original.Alpha * local));
            canvas.DrawImage(image, SKRect.Create(left, 0, right - left, bitmap.Height),
                SKRect.Create(x + left - 1, y, right - left + 2, bitmap.Height), Sampling, paint);
            paint.Color = original;
        }
    }

    public static void DrawWave(SKCanvas canvas, SKImage image, int width, int height, float progress, float power, float speed, SKPaint paint, float x = 0, float y = 0)
    {
        var strip = Math.Max(4, width / 80);
        var amplitude = Math.Clamp(height * .035f * power, 1, height * .16f);
        var phase = progress * MathF.PI * 2 * speed;
        for (var left = 0; left < width; left += strip)
        {
            var right = Math.Min(width, left + strip);
            var slice = SKRect.Create(left, 0, right - left, height);
            var offset = MathF.Sin(left / (float)Math.Max(1, width) * MathF.PI * 4 + phase) * amplitude;
            var destination = SKRect.Create(x + left - 1, y + offset, right - left + 2, height);
            canvas.DrawImage(image, slice, destination, Sampling, paint);
        }
    }

    public static void DrawFragments(SKCanvas canvas, SKImage image, int width, int height, float progress, float power, SKPaint paint, float x = 0, float y = 0)
    {
        const int columns = 8, rows = 6;
        var total = columns * rows;
        var original = paint.Color;
        for (var row = 0; row < rows; row++)
        for (var column = 0; column < columns; column++)
        {
            var index = row * columns + column;
            var order = (index * 37 + 11) % total;
            var local = Math.Clamp((progress - order / (float)total * .55f) / .45f, 0, 1);
            if (local <= 0) continue;
            var left = column * width / (float)columns;
            var top = row * height / (float)rows;
            var right = (column + 1) * width / (float)columns;
            var bottom = (row + 1) * height / (float)rows;
            var dx = ((order * 17 % 15) - 7) * width * .006f * power * (1 - local);
            var dy = ((order * 11 % 13) - 6) * height * .01f * power * (1 - local);
            paint.Color = original.WithAlpha((byte)(original.Alpha * local));
            canvas.DrawImage(image, SKRect.Create(left, top, right - left, bottom - top),
                SKRect.Create(x + left + dx - 1, y + top + dy - 1, right - left + 2, bottom - top + 2), Sampling, paint);
        }
        paint.Color = original;
    }

    public static void DrawTearReveal(SKCanvas canvas, SKImage image, int width, int height, float progress, float power, SKPaint paint, float x = 0, float y = 0)
    {
        if (progress <= 0) return;
        var edge = progress * width;
        var tooth = Math.Clamp(height * .035f * power, 2, height * .12f);
        using var tear = new SKPath();
        tear.MoveTo(x, y);
        const int segments = 18;
        for (var i = 0; i <= segments; i++)
        {
            var yy = height * i / (float)segments;
            var jitter = (i == 0 || i == segments ? 0 : MathF.Sin(i * 13.17f + 1.4f) * tooth);
            tear.LineTo(x + Math.Clamp(edge + jitter, 0, width), y + yy);
        }
        tear.LineTo(x, y + height);
        tear.Close();
        canvas.Save();
        canvas.ClipPath(tear, antialias: true);
        canvas.DrawImage(image, SKRect.Create(x, y, width, height), Sampling, paint);
        canvas.Restore();
    }
}
