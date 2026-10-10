# Produces images/preview.png, the screenshot Dalamud shows in the plugin installer.
#
# Dalamud rejects plugin images larger than 730x380 (PluginImageCache.PluginImageWidth
# and PluginImageHeight) and logs an error instead of displaying them, so the full size
# screenshot cannot be used directly. This scales images/screenshot.png down to fit
# inside that box, keeping its aspect ratio.

Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$source = Join-Path $root 'images\screenshot.png'
$target = Join-Path $root 'images\preview.png'

$maxWidth = 730
$maxHeight = 380

$src = [System.Drawing.Image]::FromFile($source)

$ratio = [Math]::Min($maxWidth / $src.Width, $maxHeight / $src.Height)
$w = [int][Math]::Floor($src.Width * $ratio)
$h = [int][Math]::Floor($src.Height * $ratio)

$bmp = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality

$g.DrawImage($src, (New-Object System.Drawing.Rectangle(0, 0, $w, $h)))

$bmp.Save($target, [System.Drawing.Imaging.ImageFormat]::Png)

$g.Dispose()
$bmp.Dispose()
$src.Dispose()

"wrote {0} ({1}x{2}, limit {3}x{4})" -f $target, $w, $h, $maxWidth, $maxHeight
