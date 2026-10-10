# Draws images/icon.png for the Blue Mage Set Builder plugin.
# Kept in the repo so the icon can be adjusted and regenerated rather than
# re-drawn by hand. Run it from anywhere: paths are resolved from this file.
#
# The picture: an open spellbook whose two pages each hold a 3x4 grid of spell
# slots, 24 in all, the size of a Blue Mage active set. Most slots are filled,
# which is what the plugin does: fills the set from the spells you know.

Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$outDir = Join-Path $root 'images'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$out = Join-Path $outDir 'icon.png'

$size = 512

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

# One page of an open book. $outer is the x of the page's outside edge, $spine the x
# of the spine. Top and bottom edges sag toward the spine the way real pages do.
function New-PagePath([float]$outer, [float]$spine, [float]$top, [float]$bottom, [float]$sag) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $mid = ($outer + $spine) / 2
    $p.AddBezier($outer, $top, $mid, ($top - $sag), $mid, ($top - $sag), $spine, ($top + $sag))
    $p.AddLine($spine, ($top + $sag), $spine, $bottom)
    $p.AddBezier($spine, $bottom, $mid, ($bottom - $sag), $mid, ($bottom - $sag), $outer, ($bottom - 2 * $sag))
    $p.CloseFigure()
    return $p
}

$bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$g.Clear([System.Drawing.Color]::Transparent)

# Background: the same rounded tile as the other plugins, tinted blue.
$bg = New-RoundedPath 0 0 $size $size 112
$rect = New-Object System.Drawing.RectangleF(0, 0, $size, $size)
$bgBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    $rect,
    [System.Drawing.Color]::FromArgb(255, 36, 62, 122),
    [System.Drawing.Color]::FromArgb(255, 14, 22, 46),
    90)
$g.FillPath($bgBrush, $bg)
$edge = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(60, 255, 255, 255), 5)
$g.DrawPath($edge, $bg)

# Book geometry
$spine = 256.0
$top = 112.0
$bottom = 404.0
$sag = 14.0
$pageWidth = 186.0

# The cover, a little larger than the pages and showing below them.
$coverBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 24, 92, 196))
$coverL = New-PagePath ($spine - $pageWidth - 14) $spine ($top + 14) ($bottom + 22) $sag
$coverR = New-PagePath ($spine + $pageWidth + 14) $spine ($top + 14) ($bottom + 22) $sag
$g.FillPath($coverBrush, $coverL)
$g.FillPath($coverBrush, $coverR)

# The pages
$pageBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 232, 238, 250))
$pageL = New-PagePath ($spine - $pageWidth) ($spine - 4) $top $bottom $sag
$pageR = New-PagePath ($spine + $pageWidth) ($spine + 4) $top $bottom $sag
$g.FillPath($pageBrush, $pageL)
$g.FillPath($pageBrush, $pageR)

# A soft shadow down the spine so the two pages read as a book, not two cards.
$shadowRect = New-Object System.Drawing.RectangleF(($spine - 30), $top, 60, ($bottom - $top + 20))
$shadowBlend = New-Object System.Drawing.Drawing2D.ColorBlend(3)
$shadowBlend.Colors = @(
    [System.Drawing.Color]::FromArgb(0, 20, 40, 90),
    [System.Drawing.Color]::FromArgb(90, 20, 40, 90),
    [System.Drawing.Color]::FromArgb(0, 20, 40, 90))
$shadowBlend.Positions = @(0.0, 0.5, 1.0)
$shadowBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    $shadowRect, [System.Drawing.Color]::Black, [System.Drawing.Color]::Black, 0.0)
$shadowBrush.InterpolationColors = $shadowBlend
$g.SetClip($pageL)
$g.FillRectangle($shadowBrush, $shadowRect)
$g.SetClip($pageR)
$g.FillRectangle($shadowBrush, $shadowRect)
$g.ResetClip()

# The 24 slots: 3 columns x 4 rows per page. Empty ones are listed by page, row, column.
$cell = 44.0
$gap = 11.0
$gridW = 3 * $cell + 2 * $gap
$gridH = 4 * $cell + 3 * $gap
$gridTop = $top + 16 + (($bottom - $top - 16) - $gridH) / 2
$empty = @('0-3-2', '1-2-0', '1-3-1', '1-3-2')

$fillTop = [System.Drawing.Color]::FromArgb(255, 110, 210, 255)
$fillBottom = [System.Drawing.Color]::FromArgb(255, 34, 110, 228)
$emptyPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 160, 178, 214), 5)
$emptyPen.DashStyle = [System.Drawing.Drawing2D.DashStyle]::Dash

for ($page = 0; $page -lt 2; $page++) {
    $centre = if ($page -eq 0) { $spine - $pageWidth / 2 - 2 } else { $spine + $pageWidth / 2 + 2 }
    $left = $centre - $gridW / 2

    for ($row = 0; $row -lt 4; $row++) {
        for ($col = 0; $col -lt 3; $col++) {
            $x = $left + $col * ($cell + $gap)
            $y = $gridTop + $row * ($cell + $gap)
            $slot = New-RoundedPath $x $y $cell $cell 10

            if ($empty -contains "$page-$row-$col") {
                $g.DrawPath($emptyPen, $slot)
            }
            else {
                $r = New-Object System.Drawing.RectangleF($x, $y, $cell, $cell)
                $b = New-Object System.Drawing.Drawing2D.LinearGradientBrush($r, $fillTop, $fillBottom, 90)
                $g.FillPath($b, $slot)
                $b.Dispose()
            }

            $slot.Dispose()
        }
    }
}

$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)

foreach ($o in @($g, $bmp, $bgBrush, $edge, $coverBrush, $coverL, $coverR, $pageBrush,
                 $pageL, $pageR, $shadowBrush, $emptyPen, $bg)) { $o.Dispose() }

"wrote $out"
