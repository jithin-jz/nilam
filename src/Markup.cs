using System;
using System.Globalization;

namespace Nilam
{
    /// <summary>
    /// The colours that depend on a setting or on Windows itself, substituted
    /// into the markup before it is parsed.
    /// </summary>
    internal class Theme
    {
        internal string CardBackground;
        internal string CardBorder;
        internal string Caption;
        internal bool HighContrast;

        internal static Theme From(Settings settings, bool highContrast)
        {
            Theme t = new Theme();
            t.HighContrast = highContrast;
            if (highContrast)
            {
                // Windows contrast themes ask for exactly this: nothing
                // translucent, nothing dim.
                t.CardBackground = "#FF000000";
                t.CardBorder = "#FFFFFFFF";
                t.Caption = "#FFFFFFFF";
                return t;
            }

            int alpha = (int)Math.Round(settings.CardOpacity * 255 / 100.0);
            if (alpha < 0) alpha = 0;
            if (alpha > 255) alpha = 255;
            t.CardBackground = "#" + alpha.ToString("X2", CultureInfo.InvariantCulture) + "121215";
            t.CardBorder = "#2AFFFFFF";
            t.Caption = "#8CFFFFFF";
            return t;
        }
    }

    /// <summary>
    /// The widget markup, kept as strings and parsed at runtime by XamlReader so
    /// the build needs no XAML compiler. The __NAME__ tokens are substituted by
    /// Window() before parsing. Only values this file controls are ever
    /// substituted - never a file path or anything else from the machine, which
    /// is how an install folder containing "&amp;" used to break the parse.
    /// </summary>
    internal static class Markup
    {
        // Progress wave on the media card. 300 is the card's inner width: a
        // medium card is 336 wide with 18px padding each side.
        internal const double WaveWidth = 300;
        internal const double WaveHeight = 18;

        /// <summary>Wraps a card body in its window and fills in the tokens.</summary>
        internal static string Window(string body, Theme theme)
        {
            string xaml = WindowHead + Styles + body + "</Window>";
            return xaml.Replace("__SMALL__", Number(LayoutPlanner.Unit))
                       .Replace("__MEDIUM__", Number(LayoutPlanner.BlockWidth))
                       .Replace("__BANNERW__", Number(LayoutPlanner.BannerWidth))
                       .Replace("__BANNERH__", Number(LayoutPlanner.BannerHeight))
                       .Replace("__WAVEW__", Number(WaveWidth))
                       .Replace("__WAVEH__", Number(WaveHeight))
                       .Replace("__CARDBG__", theme.CardBackground)
                       .Replace("__CARDBORDER__", theme.CardBorder)
                       .Replace("__CAPTION__", theme.Caption);
        }

        private static string Number(double value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        internal const string WindowHead =
@"<Window xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
        xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml""
        Title=""Nilam""
        WindowStyle=""None"" AllowsTransparency=""True"" Background=""Transparent""
        ShowInTaskbar=""False"" ResizeMode=""NoResize"" SizeToContent=""WidthAndHeight""
        ShowActivated=""False"" Topmost=""False"">";

        // There is deliberately no DropShadowEffect on the card.
        //
        // The original style had one (28px blur, 6px drop). Each card's window
        // is exactly the size of the card, so the shadow was clipped at the
        // window's edge and never appeared around it - but WPF still re-ran the
        // blur over the whole card, on the CPU, every time anything inside
        // changed: once a second while a track played. What the shadow did do
        // was show through the translucent card from underneath, making it read
        // as 80% opaque instead of the 71% written here. The background below
        // is that measured result as a flat colour, so the cards look the same
        // and cost nothing to repaint.
        internal const string Styles =
@"  <Window.Resources>
    <Style x:Key=""Card"" TargetType=""Border"">
      <Setter Property=""CornerRadius"" Value=""20""/>
      <Setter Property=""Padding"" Value=""22,18,22,18""/>
      <Setter Property=""BorderThickness"" Value=""1""/>
      <Setter Property=""BorderBrush"" Value=""__CARDBORDER__""/>
      <Setter Property=""Background"" Value=""__CARDBG__""/>
    </Style>
    <Style x:Key=""Cap"" TargetType=""TextBlock"">
      <Setter Property=""FontFamily"" Value=""Segoe UI""/>
      <Setter Property=""FontSize"" Value=""11""/>
      <Setter Property=""Foreground"" Value=""__CAPTION__""/>
      <Setter Property=""FontWeight"" Value=""SemiBold""/>
    </Style>
    <Style x:Key=""Val"" TargetType=""TextBlock"">
      <Setter Property=""FontFamily"" Value=""Segoe UI""/>
      <Setter Property=""FontSize"" Value=""15""/>
      <Setter Property=""Foreground"" Value=""#F2FFFFFF""/>
    </Style>
    <Style x:Key=""GlyphBtn"" TargetType=""Button"">
      <Setter Property=""FontFamily"" Value=""Segoe Fluent Icons, Segoe MDL2 Assets""/>
      <Setter Property=""FontSize"" Value=""13""/>
      <Setter Property=""Foreground"" Value=""#F2FFFFFF""/>
      <Setter Property=""Background"" Value=""Transparent""/>
      <Setter Property=""BorderThickness"" Value=""0""/>
      <Setter Property=""Width"" Value=""44""/>
      <Setter Property=""Height"" Value=""30""/>
      <Setter Property=""Cursor"" Value=""Hand""/>
      <Setter Property=""Template"">
        <Setter.Value>
          <ControlTemplate TargetType=""Button"">
            <Border x:Name=""bd"" CornerRadius=""10"" Background=""{TemplateBinding Background}"">
              <ContentPresenter HorizontalAlignment=""Center"" VerticalAlignment=""Center""/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property=""IsMouseOver"" Value=""True"">
                <Setter TargetName=""bd"" Property=""Background"" Value=""#26FFFFFF""/>
              </Trigger>
              <Trigger Property=""IsPressed"" Value=""True"">
                <Setter TargetName=""bd"" Property=""Background"" Value=""#40FFFFFF""/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
  </Window.Resources>";

        /// <summary>
        /// The day banner: a bare, card-less line of text parked at the top centre
        /// of the screen. Unlike the cards it has no Card background, so only the
        /// letters sit on the wallpaper. The size is fixed because the Window uses
        /// SizeToContent, and a known size is what makes the top-centre position
        /// computable before the window is shown. The letters, and their font,
        /// are added in code (DayBanner.cs).
        /// The drop shadow is not decoration - it keeps the thin strokes legible
        /// over a light wallpaper. It is static: the word changes once a day.
        /// </summary>
        internal const string DayBanner =
@"  <Border Width=""__BANNERW__"" Height=""__BANNERH__"" Background=""Transparent"">
    <StackPanel x:Name=""BannerRow"" Orientation=""Horizontal""
                HorizontalAlignment=""Center"" VerticalAlignment=""Center"">
      <!-- One shadow for the whole word rather than one per letter: it keeps the
           thin strokes legible over a light wallpaper without the overlapping
           haloes that per-letter effects produce. -->
      <StackPanel.Effect>
        <DropShadowEffect BlurRadius=""18"" ShadowDepth=""0"" Opacity=""0.55"" Color=""#FF000000""/>
      </StackPanel.Effect>
    </StackPanel>
  </Border>";

        internal const string Clock =
@"  <Border Style=""{StaticResource Card}"" Width=""__SMALL__"" Height=""__SMALL__"" Padding=""18,16,18,16"">
    <StackPanel VerticalAlignment=""Center"">
      <TextBlock x:Name=""TimeText"" Text=""00:00"" FontFamily=""Segoe UI"" FontSize=""42""
                 FontWeight=""Thin"" Foreground=""White""/>
      <TextBlock x:Name=""AmPmText"" Text="""" Style=""{StaticResource Cap}"" Margin=""2,-2,0,0""/>
      <TextBlock x:Name=""DayText"" Text=""Day"" FontFamily=""Segoe UI"" FontSize=""14""
                 FontWeight=""Light"" Foreground=""#E8FFFFFF"" Margin=""2,14,0,0""/>
      <TextBlock x:Name=""DateText"" Text=""Date"" Style=""{StaticResource Cap}"" Margin=""2,2,0,0""/>
    </StackPanel>
  </Border>";

        internal const string Weather =
@"  <Border Style=""{StaticResource Card}"" Width=""__SMALL__"" Height=""__SMALL__"" Padding=""18,16,18,16"">
    <Grid>
      <StackPanel>
        <TextBlock x:Name=""WxPlace"" Text=""WEATHER"" Style=""{StaticResource Cap}""
                   TextTrimming=""CharacterEllipsis""/>
        <StackPanel Orientation=""Horizontal"" Margin=""0,8,0,0"">
          <TextBlock x:Name=""WxTemp"" Text=""--"" FontFamily=""Segoe UI"" FontSize=""38""
                     FontWeight=""Thin"" Foreground=""White""/>
          <TextBlock x:Name=""WxIcon"" Text=""&#xE753;"" FontFamily=""Segoe Fluent Icons, Segoe MDL2 Assets""
                     FontSize=""22"" Foreground=""#E0FFFFFF"" VerticalAlignment=""Center"" Margin=""8,4,0,0""/>
        </StackPanel>
        <TextBlock x:Name=""WxDesc"" Text="""" Style=""{StaticResource Val}"" FontSize=""12""
                   Foreground=""#E0FFFFFF"" TextTrimming=""CharacterEllipsis"" Margin=""1,2,0,0""/>
        <TextBlock x:Name=""WxRange"" Text="""" Style=""{StaticResource Cap}"" Margin=""1,10,0,0""/>
      </StackPanel>
      <!-- Open-Meteo's data is CC BY 4.0 and its terms ask for a link next to
           wherever the data is shown. This sits in the card's bottom padding
           via a negative margin, so it claims no layout height. -->
      <TextBlock x:Name=""WxCredit"" Text=""Open-Meteo"" FontFamily=""Segoe UI"" FontSize=""9""
                 Foreground=""__CAPTION__"" HorizontalAlignment=""Right"" VerticalAlignment=""Bottom""
                 Margin=""0,0,-4,-10"" Cursor=""Hand"" Background=""Transparent""
                 ToolTip=""Weather data by Open-Meteo.com""
                 AutomationProperties.Name=""Weather data by Open-Meteo.com""/>
    </Grid>
  </Border>";

        internal const string Media =
@"  <Border Style=""{StaticResource Card}"" Width=""__MEDIUM__"" Height=""__SMALL__"" Padding=""18,16,18,16"">
    <Grid>
      <!-- The original two-column layout is nested here untouched. Because it is
           a single child of this overlay root it measures exactly as it did
           before, so adding the wave cannot reflow the art, text or buttons. -->
      <Grid>
        <Grid.ColumnDefinitions>
          <ColumnDefinition Width=""Auto""/><ColumnDefinition Width=""*""/>
        </Grid.ColumnDefinitions>
        <!-- BitmapScalingMode=HighQuality switches WPF from its default bilinear
             resample to Fant. Cover art arrives at whatever size the player felt
             like sending - often 300px or more for a 96px slot - and bilinear
             only reads a 2x2 neighbourhood, so a >2x downscale throws away most
             of the detail and leaves the art looking soft. Fant area-averages
             the whole footprint instead. -->
        <Border x:Name=""ArtBorder"" Width=""96"" Height=""96"" CornerRadius=""14""
                Background=""#26FFFFFF"" VerticalAlignment=""Center"" Margin=""0,0,16,0""
                RenderOptions.BitmapScalingMode=""HighQuality"">
          <!-- Shown when a track has no cover art. The four bars carry a hue
               ramp - aqua, sky, lavender, pink - held in the same lightness band
               as the CPU, RAM and battery bars, so the whole app reads as one
               palette rather than four unrelated accents.
               Each bar is laid out at full height once and squashed by a
               ScaleTransform, rather than having its Height animated: a
               transform is applied on the render thread, while four animated
               Heights would re-run a layout pass on this panel every frame for
               the whole time a track plays. -->
          <StackPanel x:Name=""ArtBeat"" Orientation=""Horizontal"" Height=""36""
                      HorizontalAlignment=""Center"" VerticalAlignment=""Center"">
            <Border x:Name=""Beat1"" Width=""6"" Height=""36"" CornerRadius=""3"" Background=""#FF8FE8D8""
                    Margin=""3,0,3,0"" VerticalAlignment=""Bottom"" RenderTransformOrigin=""0.5,1"">
              <Border.RenderTransform><ScaleTransform ScaleY=""0.28""/></Border.RenderTransform>
            </Border>
            <Border x:Name=""Beat2"" Width=""6"" Height=""36"" CornerRadius=""3"" Background=""#FF87CEFA""
                    Margin=""3,0,3,0"" VerticalAlignment=""Bottom"" RenderTransformOrigin=""0.5,1"">
              <Border.RenderTransform><ScaleTransform ScaleY=""0.28""/></Border.RenderTransform>
            </Border>
            <Border x:Name=""Beat3"" Width=""6"" Height=""36"" CornerRadius=""3"" Background=""#FFC4B5FD""
                    Margin=""3,0,3,0"" VerticalAlignment=""Bottom"" RenderTransformOrigin=""0.5,1"">
              <Border.RenderTransform><ScaleTransform ScaleY=""0.28""/></Border.RenderTransform>
            </Border>
            <Border x:Name=""Beat4"" Width=""6"" Height=""36"" CornerRadius=""3"" Background=""#FFF9B8D8""
                    Margin=""3,0,3,0"" VerticalAlignment=""Bottom"" RenderTransformOrigin=""0.5,1"">
              <Border.RenderTransform><ScaleTransform ScaleY=""0.28""/></Border.RenderTransform>
            </Border>
          </StackPanel>
        </Border>
        <StackPanel Grid.Column=""1"" VerticalAlignment=""Center"">
          <TextBlock Text=""NOW PLAYING"" Style=""{StaticResource Cap}"" Margin=""0,0,0,6""/>
          <TextBlock x:Name=""MediaTitle"" Text="""" FontFamily=""Segoe UI"" FontSize=""14""
                     Foreground=""#F2FFFFFF"" MaxWidth=""176"" TextTrimming=""CharacterEllipsis""/>
          <TextBlock x:Name=""MediaArtist"" Text="""" Style=""{StaticResource Val}"" FontSize=""11""
                     Foreground=""#8CFFFFFF"" MaxWidth=""176"" TextTrimming=""CharacterEllipsis"" Margin=""0,3,0,0""/>
          <!-- The buttons' content is an icon-font glyph, which a screen reader
               cannot read; AutomationProperties.Name is what it announces. -->
          <StackPanel Orientation=""Horizontal"" HorizontalAlignment=""Left"" Margin=""-10,8,0,0"">
            <Button x:Name=""BtnPrev"" Style=""{StaticResource GlyphBtn}"" Content=""&#xE892;""
                    AutomationProperties.Name=""Previous track""/>
            <Button x:Name=""BtnPlay"" Style=""{StaticResource GlyphBtn}"" Content=""&#xE768;"" FontSize=""17""
                    AutomationProperties.Name=""Play""/>
            <Button x:Name=""BtnNext"" Style=""{StaticResource GlyphBtn}"" Content=""&#xE893;""
                    AutomationProperties.Name=""Next track""/>
          </StackPanel>
        </StackPanel>
      </Grid>

      <!-- Progress wave. It sits in the card's bottom padding via a negative
           margin, so it claims no layout height and the existing content keeps
           its exact position. Two copies of one geometry: a dim full-width wave
           and a lit copy revealed left-to-right by a clip. -->
      <!-- Background is Transparent on purpose: a Path is hit-tested only on its
           1.6px stroke, which is far too thin to click. The transparent panel
           gives the whole strip a hit area for seeking. -->
      <Grid x:Name=""WaveHost"" Width=""__WAVEW__"" Height=""__WAVEH__""
            HorizontalAlignment=""Center"" VerticalAlignment=""Bottom""
            Margin=""0,0,0,-6"" Background=""Transparent""
            AutomationProperties.Name=""Track position"">
        <Path x:Name=""WaveDim"" Stroke=""#33FFFFFF"" StrokeThickness=""1.6""
              StrokeStartLineCap=""Round"" StrokeEndLineCap=""Round""/>
        <!-- The lit wave carries the whole white-to-sky-blue ramp across its
             full width, and the clip reveals it left to right. So the colour
             at the leading edge is the colour for that point in the track:
             white near the start, deepening to sky blue towards the end. -->
        <Path x:Name=""WaveLit"" StrokeThickness=""1.6""
              StrokeStartLineCap=""Round"" StrokeEndLineCap=""Round"">
          <Path.Stroke>
            <LinearGradientBrush StartPoint=""0,0"" EndPoint=""1,0"">
              <GradientStop Offset=""0"" Color=""#FFFFFF""/>
              <GradientStop Offset=""1"" Color=""#38BDF8""/>
            </LinearGradientBrush>
          </Path.Stroke>
        </Path>
      </Grid>
    </Grid>
  </Border>";

        internal const string SysStats =
@"  <Border Style=""{StaticResource Card}"" Width=""__SMALL__"" Height=""__SMALL__"" Padding=""18,16,18,16"">
    <StackPanel>
      <TextBlock Text=""SYSTEM"" Style=""{StaticResource Cap}"" Margin=""0,0,0,12""/>
      <Grid Margin=""0,0,0,5"">
        <Grid.ColumnDefinitions><ColumnDefinition Width=""*""/><ColumnDefinition Width=""Auto""/></Grid.ColumnDefinitions>
        <TextBlock Text=""CPU"" Style=""{StaticResource Val}"" FontSize=""11"" Foreground=""#B8FFFFFF""/>
        <TextBlock x:Name=""CpuText"" Grid.Column=""1"" Text=""--"" Style=""{StaticResource Val}""/>
      </Grid>
      <Border Height=""4"" CornerRadius=""2"" Background=""#1FFFFFFF"" Margin=""0,0,0,12"">
        <Border x:Name=""CpuBar"" HorizontalAlignment=""Left"" Width=""0"" Height=""4"" CornerRadius=""2"" Background=""#FF87CEFA""/>
      </Border>
      <Grid Margin=""0,0,0,5"">
        <Grid.ColumnDefinitions><ColumnDefinition Width=""*""/><ColumnDefinition Width=""Auto""/></Grid.ColumnDefinitions>
        <TextBlock Text=""RAM"" Style=""{StaticResource Val}"" FontSize=""11"" Foreground=""#B8FFFFFF""/>
        <TextBlock x:Name=""RamText"" Grid.Column=""1"" Text=""--"" Style=""{StaticResource Val}""/>
      </Grid>
      <Border Height=""4"" CornerRadius=""2"" Background=""#1FFFFFFF"" Margin=""0,0,0,12"">
        <Border x:Name=""RamBar"" HorizontalAlignment=""Left"" Width=""0"" Height=""4"" CornerRadius=""2"" Background=""#FFC4B5FD""/>
      </Border>
      <TextBlock x:Name=""DiskText"" Text="""" Style=""{StaticResource Cap}""/>
    </StackPanel>
  </Border>";

        internal const string Notes =
@"  <Border Style=""{StaticResource Card}"" Width=""__MEDIUM__"" Height=""__SMALL__"" Padding=""20,16,20,16"">
    <StackPanel>
      <TextBlock Text=""NOTES"" Style=""{StaticResource Cap}"" Margin=""0,0,0,8""/>
      <TextBox x:Name=""NotesBox"" Height=""96"" Background=""Transparent"" BorderThickness=""0""
               Foreground=""#F2FFFFFF"" FontFamily=""Segoe UI"" FontSize=""12""
               TextWrapping=""Wrap"" AcceptsReturn=""True"" VerticalScrollBarVisibility=""Auto""
               CaretBrush=""White"" SelectionOpacity=""0.3"" AutomationProperties.Name=""Notes""/>
    </StackPanel>
  </Border>";

        internal const string Battery =
@"  <Border Style=""{StaticResource Card}"" Width=""__SMALL__"" Height=""__SMALL__"" Padding=""18,16,18,16"">
    <StackPanel>
      <TextBlock Text=""BATTERY"" Style=""{StaticResource Cap}""/>
      <TextBlock x:Name=""BattText"" Text=""--"" FontFamily=""Segoe UI"" FontSize=""40""
                 FontWeight=""Thin"" Foreground=""White"" Margin=""0,10,0,0""/>
      <TextBlock x:Name=""BattStatus"" Text=""--"" Style=""{StaticResource Val}"" FontSize=""12""
                 Foreground=""#E0FFFFFF"" Margin=""1,2,0,0""/>
      <Border Height=""5"" CornerRadius=""3"" Background=""#1FFFFFFF"" Margin=""0,14,0,0"">
        <Border x:Name=""BattBar"" HorizontalAlignment=""Left"" Width=""0"" Height=""5"" CornerRadius=""3"" Background=""#FF8FE8D8""/>
      </Border>
    </StackPanel>
  </Border>";
    }
}
