param(
    [string]$Atlas = (Join-Path $PSScriptRoot '../assets/art/bear-v1/expression-atlas.png'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../assets/pets/bear')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$names = @('idle_0','happy_0','curious_0','startled_0','grievance_0','annoyed_0','sleepy_0','content_0','bashful_0')
$atlasImage = [Drawing.Bitmap]::FromFile([IO.Path]::GetFullPath($Atlas))
try {
    if ($atlasImage.Width % 3 -ne 0 -or $atlasImage.Height % 3 -ne 0) { throw 'Atlas must have exactly three equal rows/columns.' }
    $cellWidth = [int]($atlasImage.Width / 3)
    $cellHeight = [int]($atlasImage.Height / 3)
    for ($index = 0; $index -lt 9; $index++) {
        $cellX = ($index % 3) * $cellWidth
        $cellY = [int][Math]::Floor($index / 3) * $cellHeight
        $minX = $cellWidth; $minY = $cellHeight; $maxX = -1; $maxY = -1
        for ($y = 0; $y -lt $cellHeight; $y++) {
            for ($x = 0; $x -lt $cellWidth; $x++) {
                if ($atlasImage.GetPixel($cellX+$x,$cellY+$y).A -ge 16) {
                    $minX = [Math]::Min($minX,$x); $maxX = [Math]::Max($maxX,$x)
                    $minY = [Math]::Min($minY,$y); $maxY = [Math]::Max($maxY,$y)
                }
            }
        }
        if ($maxX -lt $minX) { throw "Empty atlas cell $index" }
        $sourceWidth = $maxX-$minX+1; $sourceHeight = $maxY-$minY+1
        $scale = [Math]::Min(96.0/$sourceWidth,102.0/$sourceHeight)
        $width = [int][Math]::Round($sourceWidth*$scale)
        $height = [int][Math]::Round($sourceHeight*$scale)
        $bitmap = [Drawing.Bitmap]::new(120,120,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $bitmap.SetResolution(96,96)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear([Drawing.Color]::Transparent)
            $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $destination = [Drawing.Rectangle]::new([int][Math]::Round((120-$width)/2),111-$height,$width,$height)
            $source = [Drawing.Rectangle]::new($cellX+$minX,$cellY+$minY,$sourceWidth,$sourceHeight)
            $graphics.DrawImage($atlasImage,$destination,$source,[Drawing.GraphicsUnit]::Pixel)
        } finally { $graphics.Dispose() }
        # Technical alpha cleanup/anchoring only; no redrawing or recoloring art.
        $bottom = -1
        for ($y = 0; $y -lt 120; $y++) {
            for ($x = 0; $x -lt 120; $x++) {
                $pixel = $bitmap.GetPixel($x,$y)
                if ($pixel.A -lt 8) { $bitmap.SetPixel($x,$y,[Drawing.Color]::Transparent) }
                else { $bottom = [Math]::Max($bottom,$y) }
            }
        }
        # Keep the existing nonzero curious compensation regression meaningful:
        # PNG bottom101 + JSON offset9 = world foot110, all others bottom110.
        $targetBottom = if ($index -eq 2) { 101 } else { 110 }
        $anchored = [Drawing.Bitmap]::new(120,120,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $anchored.SetResolution(96,96)
        $anchorGraphics = [Drawing.Graphics]::FromImage($anchored)
        try {
            $anchorGraphics.Clear([Drawing.Color]::Transparent)
            $anchorGraphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
            $anchorGraphics.DrawImageUnscaled($bitmap,0,$targetBottom-$bottom)
        } finally { $anchorGraphics.Dispose(); $bitmap.Dispose() }
        try {
            $path = Join-Path $OutputDirectory ($names[$index]+'.png')
            $anchored.Save([IO.Path]::GetFullPath($path),[Drawing.Imaging.ImageFormat]::Png)
            Write-Output "$($names[$index]): 120x120, 96DPI, bottom=$targetBottom"
        } finally { $anchored.Dispose() }
    }
} finally { $atlasImage.Dispose() }
