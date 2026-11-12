$ErrorActionPreference = 'Stop'
$iconSource = @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
public static class AppIconBuilder {
    public static void Main(string[] args) { Write(args[0],args[1]); }
    static void Round(Graphics g, Brush brush, float x,float y,float w,float h,float r) {
        using(var p=new GraphicsPath()) {
            p.AddArc(x,y,r*2,r*2,180,90); p.AddArc(x+w-r*2,y,r*2,r*2,270,90);
            p.AddArc(x+w-r*2,y+h-r*2,r*2,r*2,0,90); p.AddArc(x,y+h-r*2,r*2,r*2,90,90); p.CloseFigure(); g.FillPath(brush,p);
        }
    }
    static Bitmap Draw(int size) {
        var bitmap=new Bitmap(size,size,PixelFormat.Format32bppArgb);
        using(var g=Graphics.FromImage(bitmap))
        using(var blue=new SolidBrush(Color.FromArgb(18,101,212)))
        using(var pins=new SolidBrush(Color.FromArgb(169,223,255)))
        using(var mint=new SolidBrush(Color.FromArgb(102,237,203))) {
            g.SmoothingMode=SmoothingMode.AntiAlias; g.ScaleTransform(size/64f,size/64f);
            Round(g,blue,2,2,60,60,14);
            for(int x=16;x<=38;x+=11) g.FillRectangle(pins,x,41,6,8);
            Round(g,Brushes.White,10,18,44,25,4);
            Round(g,blue,16,24,12,12,2); Round(g,blue,34,24,12,12,2);
            g.FillEllipse(blue,39,5,20,20); g.FillEllipse(mint,42,8,14,14);
        }
        return bitmap;
    }
    public static void Write(string path,string preview) {
        int[] sizes={16,20,24,32,40,48,64,128,256}; var images=new byte[sizes.Length][];
        for(int i=0;i<sizes.Length;i++) using(var image=Draw(sizes[i])) using(var stream=new MemoryStream()) { image.Save(stream,ImageFormat.Png); images[i]=stream.ToArray(); }
        using(var writer=new BinaryWriter(File.Create(path))) {
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
            int offset=6+16*sizes.Length;
            for(int i=0;i<sizes.Length;i++) {
                writer.Write((byte)(sizes[i]==256?0:sizes[i])); writer.Write((byte)(sizes[i]==256?0:sizes[i])); writer.Write((byte)0); writer.Write((byte)0);
                writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(images[i].Length); writer.Write(offset); offset+=images[i].Length;
            }
            foreach(var image in images) writer.Write(image);
        }
        using(var image=Draw(256)) image.Save(preview,ImageFormat.Png);
    }
}
'@
$assetDirectory = Join-Path (Split-Path $PSScriptRoot) 'assets'
$buildDirectory = Join-Path (Split-Path $PSScriptRoot) 'dist\icon-builder'
New-Item -ItemType Directory -Path $buildDirectory -Force | Out-Null
$sourcePath = Join-Path $buildDirectory 'IconBuilder.cs'
$binaryPath = Join-Path $buildDirectory 'IconBuilder.exe'
[IO.File]::WriteAllText($sourcePath,$iconSource)
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:winexe /reference:System.Drawing.dll ("/out:" + $binaryPath) $sourcePath
if ($LASTEXITCODE -ne 0) { throw 'Icon builder compilation failed.' }
& $binaryPath (Join-Path $assetDirectory 'app.ico') (Join-Path $assetDirectory 'app-icon.png')
if ($LASTEXITCODE -ne 0) { throw 'Icon generation failed.' }
