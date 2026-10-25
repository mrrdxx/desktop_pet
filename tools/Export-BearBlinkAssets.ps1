param(
    [string]$BaseImage = (Join-Path $PSScriptRoot '../assets/pets/bear/idle_0.png'),
    [string]$BlinkSheet = (Join-Path $PSScriptRoot '../assets/art/bear-v1/blink-source.png'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../assets/pets/bear')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.IO;

// AI supplies the eyelids; deterministic eye-only compositing locks the original
// mouth, outline, feet and every other pixel. Never redraw the whole sprite.
public static class BearBlinkExport {
    public static readonly Rectangle[] EyeRegions = {
        new Rectangle(37,39,15,18), new Rectangle(68,39,15,18)
    };
    static Rectangle Bounds(Bitmap image, Rectangle cell) {
        int left=cell.Right, top=cell.Bottom, right=-1, bottom=-1;
        for(int y=cell.Top;y<cell.Bottom;y++)
        for(int x=cell.Left;x<cell.Right;x++) if(image.GetPixel(x,y).A>=16) {
            left=Math.Min(left,x); top=Math.Min(top,y);
            right=Math.Max(right,x); bottom=Math.Max(bottom,y);
        }
        if(right<left) throw new InvalidDataException("Empty source cell");
        return Rectangle.FromLTRB(left,top,right+1,bottom+1);
    }
    public static void Export(string basePath,string sheetPath,string outputDir) {
        using(var original=new Bitmap(basePath))
        using(var sheet=new Bitmap(sheetPath)) {
            if(original.Width!=120 || original.Height!=120 || sheet.Width%2!=0)
                throw new InvalidDataException("Expected 120px base and two equal sheet cells");
            var target=Bounds(original,new Rectangle(0,0,120,120));
            for(int state=0;state<2;state++) {
                var cell=new Rectangle(state*sheet.Width/2,0,sheet.Width/2,sheet.Height);
                var source=Bounds(sheet,cell);
                using(var normalized=new Bitmap(120,120,PixelFormat.Format32bppArgb))
                using(var result=new Bitmap(120,120,PixelFormat.Format32bppArgb)) {
                    normalized.SetResolution(96,96); result.SetResolution(96,96);
                    // Bitmap(Image) draws through premultiplied alpha and can
                    // round translucent outline RGB. Copy actual RGBA instead.
                    for(int y=0;y<120;y++)
                    for(int x=0;x<120;x++) result.SetPixel(x,y,original.GetPixel(x,y));
                    using(var g=Graphics.FromImage(normalized)) {
                        g.Clear(Color.Transparent);
                        g.CompositingMode=CompositingMode.SourceCopy;
                        g.InterpolationMode=InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode=PixelOffsetMode.HighQuality;
                        g.DrawImage(sheet,target,source,GraphicsUnit.Pixel);
                    }
                    foreach(var region in EyeRegions)
                    for(int y=region.Top;y<region.Bottom;y++)
                    for(int x=region.Left;x<region.Right;x++) {
                        var a=original.GetPixel(x,y); var b=normalized.GetPixel(x,y);
                        double edge=Math.Min(Math.Min(x-region.Left,region.Right-1-x),
                            Math.Min(y-region.Top,region.Bottom-1-y));
                        double weight=Math.Min(1,edge/2.0);
                        if(b.A<250 && weight>0) throw new InvalidDataException("Eye patch must be opaque");
                        result.SetPixel(x,y,Color.FromArgb(a.A,
                            (int)Math.Round(a.R+(b.R-a.R)*weight),
                            (int)Math.Round(a.G+(b.G-a.G)*weight),
                            (int)Math.Round(a.B+(b.B-a.B)*weight)));
                    }
                    string name=state==0 ? "blink_half.png" : "blink_closed.png";
                    result.Save(Path.Combine(outputDir,name),ImageFormat.Png);
                    Console.WriteLine(name+": 120x120, 96 DPI, original alpha and non-eye pixels preserved");
                }
            }
        }
    }
}
'@
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
[BearBlinkExport]::Export([IO.Path]::GetFullPath($BaseImage), [IO.Path]::GetFullPath($BlinkSheet), [IO.Path]::GetFullPath($OutputDirectory))
