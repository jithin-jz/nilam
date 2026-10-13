<#
    Draws assets\app.ico: a dark rounded tile holding two small cards and one
    wide card, in the same aqua / sky / lavender the widgets use for their bars.

    The icon is generated rather than hand-drawn so it can be regenerated at any
    size and so the repository carries its source. Run it under Windows
    PowerShell (it uses System.Drawing):

        powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
#>
[CmdletBinding()]
param([string]$Out)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if (-not $Out) {
    $here = Split-Path -Parent $MyInvocation.MyCommand.Path
    $Out = Join-Path (Split-Path -Parent $here) 'assets\app.ico'
}

function New-RoundedPath([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    # Everything is laid out on a 16-unit grid and scaled, so the 16px icon
    # lands on whole pixels and the larger sizes keep the same proportions.
    $u = $size / 16.0
    $tile = New-RoundedPath 0 0 $size $size (3.5 * $u)
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 20, 20, 24))), $tile)

    $cards = @(
        @{ x = 3; y = 3; w = 4.5; h = 4.5; c = [System.Drawing.Color]::FromArgb(255, 135, 206, 250) },   # sky
        @{ x = 8.5; y = 3; w = 4.5; h = 4.5; c = [System.Drawing.Color]::FromArgb(255, 196, 181, 253) }, # lavender
        @{ x = 3; y = 8.5; w = 10; h = 4.5; c = [System.Drawing.Color]::FromArgb(255, 143, 232, 216) }   # aqua
    )
    foreach ($c in $cards) {
        $path = New-RoundedPath ($c.x * $u) ($c.y * $u) ($c.w * $u) ($c.h * $u) (1.2 * $u)
        $g.FillPath((New-Object System.Drawing.SolidBrush $c.c), $path)
        $path.Dispose()
    }
    $tile.Dispose()
    $g.Dispose()
    return $bmp
}

# Classic 32-bit DIB entry: BITMAPINFOHEADER, bottom-up BGRA rows, then a 1bpp
# AND mask. Small sizes are stored this way because every icon consumer reads
# it; only the 256px image is PNG-compressed, as the format expects.
function Get-DibData([System.Drawing.Bitmap]$bmp) {
    $s = $bmp.Width
    $rect = New-Object System.Drawing.Rectangle(0, 0, $s, $s)
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $pixels = New-Object byte[] ($s * $s * 4)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length)
    $bmp.UnlockBits($data)

    $maskRow = [int]([Math]::Floor(($s + 31) / 32) * 4)
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    $bw.Write([int]40); $bw.Write([int]$s); $bw.Write([int]($s * 2))
    $bw.Write([int16]1); $bw.Write([int16]32); $bw.Write([int]0)
    $bw.Write([int]($s * $s * 4 + $maskRow * $s)); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0)
    for ($y = $s - 1; $y -ge 0; $y--) { $bw.Write($pixels, $y * $s * 4, $s * 4) }
    $bw.Write((New-Object byte[] ($maskRow * $s)))
    $bw.Flush()
    return ,$ms.ToArray()
}

function Get-PngData([System.Drawing.Bitmap]$bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return ,$ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$images = foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    $bytes = if ($s -ge 256) { Get-PngData $bmp } else { Get-DibData $bmp }
    $bmp.Dispose()
    @{ Size = $s; Bytes = $bytes }
}

$dir = Split-Path -Parent $Out
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force $dir | Out-Null }
$fs = [System.IO.File]::Create($Out)
$w = New-Object System.IO.BinaryWriter($fs)
$w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($i in $images) {
    $dim = if ($i.Size -ge 256) { 0 } else { $i.Size }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([int16]1); $w.Write([int16]32)
    $w.Write([int]$i.Bytes.Length); $w.Write([int]$offset)
    $offset += $i.Bytes.Length
}
foreach ($i in $images) { $w.Write($i.Bytes) }
$w.Flush(); $fs.Close()

Write-Host ("wrote {0} ({1:N0} bytes, sizes: {2})" -f $Out, (Get-Item $Out).Length, ($sizes -join ', '))
