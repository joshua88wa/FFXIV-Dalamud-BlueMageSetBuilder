# Saves whatever image is on the clipboard as images/screenshot.png in this repo.
# Paths are resolved from this file, so it can be run from anywhere.

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$outDir = Join-Path $root 'images'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$target = Join-Path $outDir 'screenshot.png'

$img = [System.Windows.Forms.Clipboard]::GetImage()

if ($null -eq $img) {
    'no image on clipboard'
    return
}

$img.Save($target, [System.Drawing.Imaging.ImageFormat]::Png)
"saved {0} ({1}x{2})" -f $target, $img.Width, $img.Height
$img.Dispose()
