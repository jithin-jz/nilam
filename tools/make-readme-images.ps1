<#
    Generates the images README.md uses, into docs\images\.

        hero.png             every card and the banner on a generated backdrop
        settings.png         the settings window
        layout-light/dark    where the cards go on a tall and on a short screen
        perf-light/dark      the measured idle cost, before and after 2.0

    Nothing here is drawn by hand. The cards and the settings window are built
    from the app's own markup - loaded out of bin\Nilam.exe - and filled with
    sample values through the app's own formatting code. The layouts come from
    LayoutPlanner. So when the app changes, running this again brings the
    pictures up to date:

        .\build.cmd
        powershell -ExecutionPolicy Bypass -File tools\make-readme-images.ps1

    bin\Nilam.exe is a 32-bit assembly, so this needs 32-bit Windows PowerShell;
    started from anything else it relaunches itself there.
#>
[CmdletBinding()]
param([string]$Repo)

$ErrorActionPreference = 'Stop'
if (-not $Repo) { $Repo = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path) }

if ([IntPtr]::Size -ne 4 -or $PSVersionTable.PSVersion.Major -gt 5) {
    $x86 = Join-Path $env:WINDIR 'SysWOW64\WindowsPowerShell\v1.0\powershell.exe'
    & $x86 -NoProfile -ExecutionPolicy Bypass -File $MyInvocation.MyCommand.Path -Repo $Repo
    exit $LASTEXITCODE
}

Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, System.Xaml

$exe = Join-Path $Repo 'bin\Nilam.exe'
if (-not (Test-Path $exe)) { throw "bin\Nilam.exe was not found. Run build.cmd first." }
$out = Join-Path $Repo 'docs\images'
New-Item -ItemType Directory -Force $out | Out-Null

# ---------------------------------------------------------------- the app's own code
$asm = [Reflection.Assembly]::LoadFile($exe)
$all = [Reflection.BindingFlags]'Static, Instance, Public, NonPublic'
function Get-AppType([string]$name) { $asm.GetType("Nilam.$name", $true) }
function Get-Static([string]$type, [string]$field) { (Get-AppType $type).GetField($field, $all).GetValue($null) }
function Get-Field($object, [string]$field) { $object.GetType().GetField($field, $all).GetValue($object) }
function Set-Field($object, [string]$field, $value) { $object.GetType().GetField($field, $all).SetValue($object, $value) }
function Invoke-App([string]$type, [string]$method, [object[]]$arguments) {
    # PowerShell hands some objects over inside a wrapper, which reflection
    # will not accept in place of the real thing; unwrap each argument.
    $raw = New-Object object[] $arguments.Length
    for ($i = 0; $i -lt $arguments.Length; $i++) {
        if ($null -ne $arguments[$i]) { $raw[$i] = $arguments[$i].PSObject.BaseObject }
    }
    (Get-AppType $type).GetMethod($method, $all).Invoke($null, $raw)
}
function ConvertTo-Brush([string]$colour) { (New-Object Windows.Media.BrushConverter).ConvertFromString($colour) }

# ---------------------------------------------------------------- sample values
# Everything shown on the cards. Sample data, not anybody's desktop.
$culture = New-Object Globalization.CultureInfo('en-US', $false)
$now = [datetime]'2026-10-17T09:41:00'              # a Saturday
$place = 'Kozhikode'
$forecast = '{"current":{"temperature_2m":28.3,"weather_code":2,"is_day":1},' +
            '"daily":{"temperature_2m_max":[33.1],"temperature_2m_min":[24.8]}}'
$track = @{ Title = 'Night Drive'; Artist = 'Sample Artist'; Progress = 0.42 }
$system = @{ Cpu = 12; Ram = 61; DiskFreeGb = 268 }
$batteryPercent = 82
$notes = "Tag v2.0.0`r`nBuy milk`r`nBook train tickets"

# Measured on one machine (1920x1200 at 150%, nothing playing), both versions
# running side by side over the same eight seconds. See docs\ARCHITECTURE.md.
$measured = @(
    @{ Label = 'Idle CPU'; Unit = '% of one core'; Old = 2.34; New = 0.20; Format = '{0:0.00}%' },
    @{ Label = 'Memory'; Unit = 'MB private'; Old = 64; New = 51; Format = '{0:0} MB' }
)

# ---------------------------------------------------------------- helpers
function Save-Png($visual, [double]$width, [double]$height, [double]$scale, [string]$path) {
    $visual.Measure((New-Object Windows.Size($width, $height)))
    $visual.Arrange((New-Object Windows.Rect(0, 0, $width, $height)))
    $visual.UpdateLayout()
    $bitmap = New-Object Windows.Media.Imaging.RenderTargetBitmap(
        [int][Math]::Round($width * $scale), [int][Math]::Round($height * $scale),
        (96 * $scale), (96 * $scale), [Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.File]::Create($path)
    try { $encoder.Save($stream) } finally { $stream.Close() }
    Write-Host ("  {0}  {1} x {2}  {3:N0} KB" -f (Split-Path -Leaf $path), $bitmap.PixelWidth, $bitmap.PixelHeight, ((Get-Item $path).Length / 1KB))
    return $bitmap
}

function Save-Text([string]$path, [string]$text) {
    [IO.File]::WriteAllText($path, $text, (New-Object Text.UTF8Encoding($false)))
    Write-Host ("  {0}  {1:N1} KB" -f (Split-Path -Leaf $path), ((Get-Item $path).Length / 1KB))
}

$theme = Invoke-App 'Theme' 'From' @([Activator]::CreateInstance((Get-AppType 'Settings'), $true), $false)

# Builds one card from the app's markup and hands back its window, so named
# elements can be looked up before the content is lifted out of it.
function New-CardWindow([string]$markupName) {
    $xaml = Invoke-App 'Markup' 'Window' @((Get-Static 'Markup' $markupName), $theme)
    return [Windows.Markup.XamlReader]::Parse($xaml)
}

function Remove-FromWindow($window) {
    $content = $window.Content
    $window.Content = $null
    return $content
}

# ====================================================================== hero.png
Write-Host 'docs\images:'
$workArea = New-Object Windows.Rect(0, 0, 1280, 752)       # 1920x1200 at 150%, the screen this was designed on
$names = New-Object 'System.Collections.Generic.List[string]'
foreach ($n in (Get-Static 'Settings' 'CardNames')) { $names.Add($n) }
$plan = Invoke-App 'LayoutPlanner' 'Plan' @($workArea, $names)
$spots = Get-Field $plan 'Cards'

$scene = New-Object Windows.Controls.Canvas
$scene.Width = $workArea.Width
$scene.Height = $workArea.Height
$scene.ClipToBounds = $true

# A generated backdrop, so the picture depends on nobody's wallpaper.
$sky = New-Object Windows.Media.LinearGradientBrush
$sky.StartPoint = New-Object Windows.Point(0, 0)
$sky.EndPoint = New-Object Windows.Point(1, 1)
foreach ($stop in @(@('#0D1526', 0.0), @('#1B2A52', 0.45), @('#4B2E6B', 0.8), @('#7A3B63', 1.0))) {
    $sky.GradientStops.Add((New-Object Windows.Media.GradientStop(([Windows.Media.ColorConverter]::ConvertFromString($stop[0])), $stop[1])))
}
$back = New-Object Windows.Shapes.Rectangle
$back.Width = $workArea.Width; $back.Height = $workArea.Height; $back.Fill = $sky
[void]$scene.Children.Add($back)
foreach ($glow in @(@('#662FB3A8', 260, 560, 620), @('#55FF9A6B', 1010, 110, 520), @('#404C7DFF', 660, 330, 700))) {
    $brush = New-Object Windows.Media.RadialGradientBrush
    $brush.GradientStops.Add((New-Object Windows.Media.GradientStop(([Windows.Media.ColorConverter]::ConvertFromString($glow[0])), 0)))
    $brush.GradientStops.Add((New-Object Windows.Media.GradientStop(([Windows.Media.ColorConverter]::ConvertFromString('#00000000')), 1)))
    $spot = New-Object Windows.Shapes.Ellipse
    $spot.Width = $glow[3]; $spot.Height = $glow[3]; $spot.Fill = $brush
    [Windows.Controls.Canvas]::SetLeft($spot, $glow[1] - ($glow[3] / 2))
    [Windows.Controls.Canvas]::SetTop($spot, $glow[2] - ($glow[3] / 2))
    [void]$scene.Children.Add($spot)
}

function Add-ToScene($element, [string]$cardName) {
    $rect = $spots[$cardName]
    [Windows.Controls.Canvas]::SetLeft($element, $rect.Left)
    [Windows.Controls.Canvas]::SetTop($element, $rect.Top)
    [void]$scene.Children.Add($element)
}

# ---- clock: through TimeText, exactly as the card formats it
$w = New-CardWindow 'Clock'
$reading = Invoke-App 'TimeText' 'Format' @($now, [Enum]::Parse((Get-AppType 'ClockChoice'), 'Auto'), $culture)
$w.FindName('TimeText').Text = Get-Field $reading 'Time'
$w.FindName('AmPmText').Text = Get-Field $reading 'AmPm'
$w.FindName('DayText').Text = $now.ToString('dddd', $culture)
$w.FindName('DateText').Text = $now.ToString('dd MMM yyyy', $culture).ToUpper($culture)
Add-ToScene (Remove-FromWindow $w) 'Clock'

# ---- weather: a sample response through Weather.Parse
$w = New-CardWindow 'Weather'
$weather = Invoke-App 'Weather' 'Parse' @($forecast)
$degree = [string][char]0x00B0
$w.FindName('WxPlace').Text = $place.ToUpper($culture)
$w.FindName('WxTemp').Text = "$(Get-Field $weather 'Temp')$degree"
$w.FindName('WxIcon').Text = Get-Field $weather 'Glyph'
$w.FindName('WxDesc').Text = Get-Field $weather 'Description'
$w.FindName('WxRange').Text = "H:$(Get-Field $weather 'High')$degree   L:$(Get-Field $weather 'Low')$degree"
Add-ToScene (Remove-FromWindow $w) 'Weather'

# ---- media: a track with no cover art, so the equaliser shows and no artwork is borrowed
$w = New-CardWindow 'Media'
$w.FindName('MediaTitle').Text = $track.Title
$w.FindName('MediaArtist').Text = $track.Artist
$w.FindName('BtnPlay').Content = [string][char]0xE769          # playing, so the button offers pause
$heights = 0.85, 0.52, 0.62, 0.92
for ($i = 1; $i -le 4; $i++) { $w.FindName("Beat$i").RenderTransform.ScaleY = $heights[$i - 1] }
$wave = (Get-AppType 'MediaCard').GetMethod('MakeWaveGeometry', $all).Invoke($null, $null)
$w.FindName('WaveDim').Data = $wave
$w.FindName('WaveLit').Data = $wave
$waveWidth = Get-Static 'Markup' 'WaveWidth'
$waveHeight = Get-Static 'Markup' 'WaveHeight'
$w.FindName('WaveLit').Clip = New-Object Windows.Media.RectangleGeometry((New-Object Windows.Rect(0, 0, ($waveWidth * $track.Progress), $waveHeight)))
Add-ToScene (Remove-FromWindow $w) 'Media'

# ---- system and battery
$barMax = 124.0                                                # Card.BarMax: the inner width of a small card
$w = New-CardWindow 'SysStats'
$w.FindName('CpuText').Text = "$($system.Cpu)%"
$w.FindName('CpuBar').Width = $barMax * $system.Cpu / 100
$w.FindName('RamText').Text = "$($system.Ram)%"
$w.FindName('RamBar').Width = $barMax * $system.Ram / 100
$w.FindName('DiskText').Text = "$($system.DiskFreeGb) GB FREE"
Add-ToScene (Remove-FromWindow $w) 'System'

$w = New-CardWindow 'Battery'
$snapshot = [Activator]::CreateInstance((Get-AppType 'StatsSnapshot'), $true)
Set-Field $snapshot 'BatteryPercent' $batteryPercent
$w.FindName('BattText').Text = "$batteryPercent%"
$w.FindName('BattStatus').Text = Invoke-App 'Stats' 'BatteryStatus' @($snapshot)
$w.FindName('BattBar').Width = $barMax * $batteryPercent / 100
Add-ToScene (Remove-FromWindow $w) 'Battery'

# ---- notes
$w = New-CardWindow 'Notes'
$w.FindName('NotesBox').Text = $notes
Add-ToScene (Remove-FromWindow $w) 'Notes'

# ---- banner: the bundled font, split and sized by the same rules DayBanner uses
$w = New-CardWindow 'DayBanner'
$row = $w.FindName('BannerRow')
$fontFolder = New-Object Uri((Join-Path $Repo 'fonts') + '\')
$michroma = New-Object Windows.Media.FontFamily($fontFolder, './#Michroma, Segoe UI')
$bannerFont = Get-AppType 'BannerFont'
$tracking = $bannerFont.GetField('MichromaTracking', $all).GetRawConstantValue()
$fontScale = $bannerFont.GetField('MichromaScale', $all).GetRawConstantValue()
$dayBanner = Get-AppType 'DayBanner'
$baseSize = $dayBanner.GetField('BaseSize', $all).GetRawConstantValue()
$minScale = $dayBanner.GetField('MinScale', $all).GetRawConstantValue()
$word = Invoke-App 'BannerText' 'DayName' @($now, $culture)
$splitArguments = [object[]]@($word, $false)
$pieces = (Get-AppType 'BannerText').GetMethod('Pieces', $all).Invoke($null, $splitArguments)
$isTracked = $splitArguments[1]

function Set-BannerWord([double]$size) {
    $row.Children.Clear()
    for ($i = 0; $i -lt $pieces.Count; $i++) {
        $letter = New-Object Windows.Controls.TextBlock
        $letter.Text = $pieces[$i]
        $letter.FontFamily = $michroma
        $letter.FontSize = $size
        $letter.Foreground = ConvertTo-Brush '#F2FFFFFF'
        if ($isTracked -and $i -lt $pieces.Count - 1) { $letter.Margin = New-Object Windows.Thickness(0, 0, ($tracking * $size), 0) }
        [void]$row.Children.Add($letter)
    }
}
$size = $baseSize * $fontScale
Set-BannerWord $size
$row.Measure((New-Object Windows.Size([double]::PositiveInfinity, [double]::PositiveInfinity)))
$limit = Get-Field $plan 'BannerMaxTextWidth'
if ($row.DesiredSize.Width -gt $limit) { Set-BannerWord ($size * [Math]::Max($minScale, $limit / $row.DesiredSize.Width)) }
Add-ToScene (Remove-FromWindow $w) 'DayBanner'

[void](Save-Png $scene $workArea.Width $workArea.Height 1.25 (Join-Path $out 'hero.png'))

# ================================================================== settings.png
$xaml = Get-Static 'SettingsWindow' 'Xaml'
$dialog = [Windows.Markup.XamlReader]::Parse($xaml)
$dialog.FindName('PlaceText').Text = "Showing weather for $place. Search to change it."
$dialog.FindName('CityBox').Text = $place
$list = $dialog.FindName('CityList')
[void]$list.Items.Add("$place, Kerala, India")
$list.SelectedIndex = 0
$list.Visibility = 'Visible'
foreach ($checked in 'UnitAuto', 'ClockAuto', 'IdlePhotos', 'StartupBox') { $dialog.FindName($checked).IsChecked = $true }
foreach ($n in $names) { $dialog.FindName("Card$n").IsChecked = $true }
$dialog.FindName('PhotoFolder').Text = 'C:\Users\you\Pictures\wallpaper'
$dialog.FindName('OpacitySlider').Value = 80
$dialog.FindName('OpacityText').Text = '80%'
$dialog.FindName('VersionRun').Text = "Nilam $((Get-Content (Join-Path $Repo 'VERSION') -TotalCount 1).Trim())"

# The content is moved into a holder that takes the window's resources with it,
# so the styles the window defines keep applying. That picture is then set into
# a plain frame standing in for the window's own title bar and border.
$body = $dialog.Content
$bodyWidth = 444
$body.Measure((New-Object Windows.Size($bodyWidth, [double]::PositiveInfinity)))
$bodyHeight = [Math]::Ceiling($body.DesiredSize.Height)
$tmp = Join-Path $env:TEMP ("nilam-settings-body-" + [guid]::NewGuid().ToString('N') + ".png")
$holder = New-Object Windows.Controls.Border
$holder.Background = [Windows.Media.Brushes]::White
$dialog.Content = $null
$holder.Child = $body
$holder.Resources = $dialog.Resources
[void](Save-Png $holder $bodyWidth $bodyHeight 1.5 $tmp)

$frame = New-Object Windows.Controls.Border
$frame.Background = [Windows.Media.Brushes]::White
$frame.BorderBrush = ConvertTo-Brush '#FFB9B9B9'
$frame.BorderThickness = New-Object Windows.Thickness(1)
$frame.CornerRadius = New-Object Windows.CornerRadius(8)
$frame.Margin = New-Object Windows.Thickness(16)
$frame.Effect = New-Object Windows.Media.Effects.DropShadowEffect -Property @{ BlurRadius = 18; ShadowDepth = 3; Opacity = 0.22; Direction = 270 }
$stack = New-Object Windows.Controls.StackPanel
$title = New-Object Windows.Controls.DockPanel
$title.Height = 32
$close = New-Object Windows.Controls.TextBlock -Property @{ Text = [string][char]0xE8BB; FontFamily = (New-Object Windows.Media.FontFamily('Segoe Fluent Icons, Segoe MDL2 Assets')); FontSize = 10; VerticalAlignment = 'Center'; Margin = (New-Object Windows.Thickness(0, 0, 16, 0)); Foreground = (ConvertTo-Brush '#FF404040') }
[Windows.Controls.DockPanel]::SetDock($close, 'Right')
[void]$title.Children.Add($close)
$icon = New-Object Windows.Controls.Image -Property @{ Width = 16; Height = 16; Margin = (New-Object Windows.Thickness(12, 0, 8, 0)); VerticalAlignment = 'Center' }
$decoder = New-Object Windows.Media.Imaging.IconBitmapDecoder((New-Object Uri((Join-Path $Repo 'assets\app.ico'))), 'None', 'OnLoad')
$icon.Source = ($decoder.Frames | Sort-Object PixelWidth | Where-Object { $_.PixelWidth -ge 32 } | Select-Object -First 1)
[void]$title.Children.Add($icon)
[void]$title.Children.Add((New-Object Windows.Controls.TextBlock -Property @{ Text = 'Nilam settings'; FontFamily = (New-Object Windows.Media.FontFamily('Segoe UI')); FontSize = 12; VerticalAlignment = 'Center' }))
[void]$stack.Children.Add($title)
$picture = New-Object Windows.Controls.Image -Property @{ Width = $bodyWidth; Height = $bodyHeight; Stretch = 'Fill' }
$loaded = New-Object Windows.Media.Imaging.BitmapImage
$loaded.BeginInit(); $loaded.CacheOption = 'OnLoad'; $loaded.UriSource = New-Object Uri($tmp); $loaded.EndInit()
$picture.Source = $loaded
[Windows.Media.RenderOptions]::SetBitmapScalingMode($picture, 'HighQuality')
[void]$stack.Children.Add($picture)
$frame.Child = $stack
$canvas = New-Object Windows.Controls.Grid
[void]$canvas.Children.Add($frame)
[void](Save-Png $canvas ($bodyWidth + 34) ($bodyHeight + 32 + 34) 1.5 (Join-Path $out 'settings.png'))
Remove-Item $tmp -ErrorAction SilentlyContinue

# ============================================================== layout-*.svg
# Two work areas through LayoutPlanner: the tall one fits all four rows in one
# block; on the short one (1080p at 150%) the rows split into two blocks.
$palette = @{
    light = @{ Surface = '#fcfcfb'; Ink = '#0b0b0b'; Secondary = '#52514e'; Muted = '#898781'; Line = '#c3c2b7'; Grid = '#e1e0d9'; Accent = '#2a78d6'; OnAccent = '#ffffff'; Good = '#006300'; Border = '#e1e0d9' }
    dark  = @{ Surface = '#1a1a19'; Ink = '#f0efec'; Secondary = '#c3c2b7'; Muted = '#898781'; Line = '#383835'; Grid = '#2c2c2a'; Accent = '#3987e5'; OnAccent = '#0b0b0b'; Good = '#0ca30c'; Border = '#2c2c2a' }
}
$fontStack = 'system-ui, -apple-system, &quot;Segoe UI&quot;, sans-serif'
$inv = [Globalization.CultureInfo]::InvariantCulture
function N([double]$v) { $v.ToString('0.##', $inv) }

$screens = @(
    @{ Title = '1920 x 1200 at 150%'; Note = 'one block, four rows'; Width = 1280; Height = 752 },
    @{ Title = '1920 x 1080 at 150%'; Note = 'two blocks, below the banner'; Width = 1280; Height = 672 }
)
$labels = @{ Clock = 'Clock'; Weather = 'Weather'; Media = 'Now playing'; System = 'System'; Battery = 'Battery'; Notes = 'Notes' }

foreach ($mode in 'light', 'dark') {
    $c = $palette[$mode]
    $k = 0.27                                   # screen units to picture units
    $panel = 372; $gap = 16; $top = 16
    $svgW = ($panel * 2) + $gap
    $svgH = 290
    $sb = New-Object Text.StringBuilder
    [void]$sb.AppendLine("<svg xmlns=`"http://www.w3.org/2000/svg`" width=`"$svgW`" height=`"$svgH`" viewBox=`"0 0 $svgW $svgH`" role=`"img`" aria-label=`"Default card layout on a tall screen and on a short screen`" font-family=`"$fontStack`">")
    for ($s = 0; $s -lt $screens.Count; $s++) {
        $screen = $screens[$s]
        $area = New-Object Windows.Rect(0, 0, $screen.Width, $screen.Height)
        $p = Get-Field (Invoke-App 'LayoutPlanner' 'Plan' @($area, $names)) 'Cards'
        $x0 = ($s * ($panel + $gap)) + (($panel - ($screen.Width * $k)) / 2)
        $y0 = $top
        $w0 = $screen.Width * $k
        $h0 = ($screen.Height + 48) * $k      # the work area plus a 48-unit taskbar
        [void]$sb.AppendLine("  <rect x=`"$(N ($s * ($panel + $gap)))`" y=`"0`" width=`"$panel`" height=`"$svgH`" rx=`"8`" fill=`"$($c.Surface)`" stroke=`"$($c.Border)`"/>")
        [void]$sb.AppendLine("  <rect x=`"$(N $x0)`" y=`"$(N $y0)`" width=`"$(N $w0)`" height=`"$(N $h0)`" rx=`"4`" fill=`"none`" stroke=`"$($c.Line)`"/>")
        [void]$sb.AppendLine("  <rect x=`"$(N $x0)`" y=`"$(N ($y0 + ($screen.Height * $k)))`" width=`"$(N $w0)`" height=`"$(N (48 * $k))`" fill=`"$($c.Grid)`"/>")
        foreach ($name in $names) {
            $r = $p[$name]
            $x = $x0 + ($r.Left * $k); $y = $y0 + ($r.Top * $k); $rw = $r.Width * $k; $rh = $r.Height * $k
            if ($name -eq 'DayBanner') {
                [void]$sb.AppendLine("  <text x=`"$(N ($x + ($rw / 2)))`" y=`"$(N ($y + ($rh / 2) + 4))`" text-anchor=`"middle`" font-size=`"11`" letter-spacing=`"5`" fill=`"$($c.Secondary)`">WEEKDAY</text>")
            }
            else {
                [void]$sb.AppendLine("  <rect x=`"$(N $x)`" y=`"$(N $y)`" width=`"$(N $rw)`" height=`"$(N $rh)`" rx=`"5`" fill=`"$($c.Accent)`"/>")
                [void]$sb.AppendLine("  <text x=`"$(N ($x + ($rw / 2)))`" y=`"$(N ($y + ($rh / 2) + 3))`" text-anchor=`"middle`" font-size=`"8.5`" font-weight=`"600`" fill=`"$($c.OnAccent)`">$($labels[$name])</text>")
            }
        }
        $cx = ($s * ($panel + $gap)) + ($panel / 2)
        [void]$sb.AppendLine("  <text x=`"$(N $cx)`" y=`"$(N ($svgH - 30))`" text-anchor=`"middle`" font-size=`"13`" font-weight=`"600`" fill=`"$($c.Ink)`">$($screen.Title)</text>")
        [void]$sb.AppendLine("  <text x=`"$(N $cx)`" y=`"$(N ($svgH - 12))`" text-anchor=`"middle`" font-size=`"12`" fill=`"$($c.Secondary)`">$($screen.Note)</text>")
    }
    [void]$sb.AppendLine('</svg>')
    Save-Text (Join-Path $out "layout-$mode.svg") $sb.ToString()
}

# ================================================================ perf-*.svg
# Two stat tiles, each on its own scale: the two measures have different units,
# and a shared axis would invent a relationship between them. The 2.0 bar wears
# the accent; the 1.x bar is the grey it is compared against.
foreach ($mode in 'light', 'dark') {
    $c = $palette[$mode]
    $tileW = 372; $tileH = 176; $gap = 16
    $svgW = ($tileW * 2) + $gap
    $sb = New-Object Text.StringBuilder
    [void]$sb.AppendLine("<svg xmlns=`"http://www.w3.org/2000/svg`" width=`"$svgW`" height=`"$tileH`" viewBox=`"0 0 $svgW $tileH`" role=`"img`" aria-label=`"Idle CPU and memory, version 1.4.2 against 2.0.0`" font-family=`"$fontStack`">")
    for ($i = 0; $i -lt $measured.Count; $i++) {
        $m = $measured[$i]
        $x0 = $i * ($tileW + $gap)
        $drop = [Math]::Round(100 * ($m.Old - $m.New) / $m.Old)
        [void]$sb.AppendLine("  <rect x=`"$x0`" y=`"0`" width=`"$tileW`" height=`"$tileH`" rx=`"8`" fill=`"$($c.Surface)`" stroke=`"$($c.Border)`"/>")
        [void]$sb.AppendLine("  <text x=`"$($x0 + 20)`" y=`"30`" font-size=`"13`" fill=`"$($c.Secondary)`">$($m.Label), $($m.Unit)</text>")
        [void]$sb.AppendLine("  <text x=`"$($x0 + 20)`" y=`"70`" font-size=`"32`" font-weight=`"600`" fill=`"$($c.Ink)`">$([string]::Format($inv, $m.Format, $m.New))</text>")
        [void]$sb.AppendLine("  <text x=`"$($x0 + 20)`" y=`"92`" font-size=`"13`" fill=`"$($c.Good)`">$drop% lower than 1.4.2</text>")
        $axisX = $x0 + 64
        $full = 232.0                              # the length of the larger bar
        $rows = @(@('1.4.2', $m.Old, $c.Muted, 118), @('2.0.0', $m.New, $c.Accent, 144))
        foreach ($rowSpec in $rows) {
            $len = [Math]::Max(8, $full * $rowSpec[1] / $m.Old)
            $y = $rowSpec[3]; $h = 12; $r = 4
            # Square at the baseline, rounded at the data end.
            $d = "M$(N $axisX) $y h$(N ($len - $r)) a$r $r 0 0 1 $r $r v$(N ($h - (2 * $r))) a$r $r 0 0 1 -$r $r h-$(N ($len - $r)) z"
            [void]$sb.AppendLine("  <text x=`"$($x0 + 20)`" y=`"$($y + 10)`" font-size=`"12`" fill=`"$($c.Secondary)`">$($rowSpec[0])</text>")
            [void]$sb.AppendLine("  <path d=`"$d`" fill=`"$($rowSpec[2])`"/>")
            [void]$sb.AppendLine("  <text x=`"$(N ($axisX + $len + 8))`" y=`"$($y + 10)`" font-size=`"12`" fill=`"$($c.Ink)`">$([string]::Format($inv, $m.Format, $rowSpec[1]))</text>")
        }
        [void]$sb.AppendLine("  <line x1=`"$(N $axisX)`" y1=`"110`" x2=`"$(N $axisX)`" y2=`"164`" stroke=`"$($c.Line)`" stroke-width=`"1`"/>")
    }
    [void]$sb.AppendLine('</svg>')
    Save-Text (Join-Path $out "perf-$mode.svg") $sb.ToString()
}
Write-Host 'done.'
