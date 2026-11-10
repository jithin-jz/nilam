using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Xml;
using Microsoft.Win32;

namespace Nilam
{
    /// <summary>
    /// The colours of the settings window. It follows the Windows app mode
    /// (light or dark) and accent colour the way Windows' own Settings app does.
    /// Under a contrast theme every colour becomes a reference to the matching
    /// system colour instead, so the window keeps the person's chosen palette
    /// while still using the same layout and controls.
    /// </summary>
    internal class DialogTheme
    {
        internal bool Dark;
        internal bool HighContrast;
        internal Color Background;
        internal readonly Dictionary<string, string> Tokens = new Dictionary<string, string>();

        // The blue Windows ships with, in the two shades Fluent uses for it.
        private static readonly Color DefaultAccentDark = Color.FromRgb(0x4C, 0xC2, 0xFF);
        private static readonly Color DefaultAccentLight = Color.FromRgb(0x00, 0x5F, 0xB8);

        internal static DialogTheme Current()
        {
            if (SystemParameters.HighContrast) return Make(false, true, null);
            bool dark = AppsUseDarkTheme();
            return Make(dark, false, ReadAccent(dark));
        }

        /// <summary>Builds a theme from explicit choices; the tests and the README pictures use this.</summary>
        internal static DialogTheme Make(bool dark, bool highContrast, Color? accent)
        {
            DialogTheme t = new DialogTheme();
            t.Dark = dark && !highContrast;
            t.HighContrast = highContrast;
            if (highContrast)
            {
                t.Background = SystemColors.WindowColor;
                t.Set("BG", Sys("Window"));
                t.Set("CARD", Sys("Window"));
                t.Set("CARDHOVER", Sys("Window"));
                t.Set("CARDPRESSED", Sys("Window"));
                t.Set("STROKE", Sys("WindowText"));
                t.Set("HOVERSTROKE", Sys("Highlight"));
                t.Set("TEXT", Sys("WindowText"));
                t.Set("TEXT2", Sys("WindowText"));
                t.Set("TEXT3", Sys("GrayText"));
                t.Set("ACCENT", Sys("Highlight"));
                t.Set("ACCENTHOVER", Sys("Highlight"));
                t.Set("ONACCENT", Sys("HighlightText"));
                t.Set("ACCENTSOFT", Sys("Window"));
                t.Set("CTRL", Sys("Control"));
                t.Set("CTRLHOVER", Sys("Control"));
                t.Set("CTRLPRESSED", Sys("Control"));
                t.Set("CTRLSTROKE", Sys("ControlText"));
                t.Set("CTRLTEXT", Sys("ControlText"));
                t.Set("INPUT", Sys("Window"));
                t.Set("INPUTFOCUS", Sys("Window"));
                t.Set("TOGGLEOFF", Sys("WindowText"));
                t.Set("TRACK", Sys("WindowText"));
                t.Set("THUMB", Sys("Window"));
                t.Set("FOOTER", Sys("Window"));
                t.Set("DIVIDER", Sys("WindowText"));
                return t;
            }

            Color a = accent.HasValue ? accent.Value : (dark ? DefaultAccentDark : DefaultAccentLight);
            t.Set("ACCENT", Hex(0xFF, a));
            t.Set("ACCENTHOVER", Hex(0xE6, a));
            t.Set("ACCENTSOFT", Hex(dark ? (byte)0x26 : (byte)0x1A, a));

            // Windows 11's Fluent colour values, flattened onto solid surfaces.
            if (dark)
            {
                t.Background = Color.FromRgb(0x20, 0x20, 0x20);
                t.Set("BG", "#FF202020");
                t.Set("CARD", "#FF2B2B2B");
                t.Set("CARDHOVER", "#FF323232");
                t.Set("CARDPRESSED", "#FF292929");
                t.Set("STROKE", "#FF1D1D1D");
                t.Set("HOVERSTROKE", "#FF3A3A3A");
                t.Set("TEXT", "#FFFFFFFF");
                t.Set("TEXT2", "#FFC5C5C5");
                t.Set("TEXT3", "#FF8B8B8B");
                t.Set("ONACCENT", "#FF000000");
                t.Set("CTRL", "#FF373737");
                t.Set("CTRLHOVER", "#FF3D3D3D");
                t.Set("CTRLPRESSED", "#FF323232");
                t.Set("CTRLSTROKE", "#FF444444");
                t.Set("CTRLTEXT", "#FFFFFFFF");
                t.Set("INPUT", "#FF333333");
                t.Set("INPUTFOCUS", "#FF1F1F1F");
                t.Set("TOGGLEOFF", "#FFA0A0A0");
                t.Set("TRACK", "#FF6E6E6E");
                t.Set("THUMB", "#FF454545");
                t.Set("FOOTER", "#FF1C1C1C");
                t.Set("DIVIDER", "#FF2E2E2E");
            }
            else
            {
                t.Background = Color.FromRgb(0xF3, 0xF3, 0xF3);
                t.Set("BG", "#FFF3F3F3");
                t.Set("CARD", "#FFFBFBFB");
                t.Set("CARDHOVER", "#FFF6F6F6");
                t.Set("CARDPRESSED", "#FFF2F2F2");
                t.Set("STROKE", "#FFE5E5E5");
                t.Set("HOVERSTROKE", "#FFD6D6D6");
                t.Set("TEXT", "#FF1B1B1B");
                t.Set("TEXT2", "#FF5C5C5C");
                t.Set("TEXT3", "#FF8A8A8A");
                t.Set("ONACCENT", "#FFFFFFFF");
                t.Set("CTRL", "#FFFEFEFE");
                t.Set("CTRLHOVER", "#FFF5F5F5");
                t.Set("CTRLPRESSED", "#FFEFEFEF");
                t.Set("CTRLSTROKE", "#FFD9D9D9");
                t.Set("CTRLTEXT", "#FF1B1B1B");
                t.Set("INPUT", "#FFFFFFFF");
                t.Set("INPUTFOCUS", "#FFFFFFFF");
                t.Set("TOGGLEOFF", "#FF868686");
                t.Set("TRACK", "#FF8A8A8A");
                t.Set("THUMB", "#FFFFFFFF");
                t.Set("FOOTER", "#FFEBEBEB");
                t.Set("DIVIDER", "#FFE0E0E0");
            }
            return t;
        }

        /// <summary>Fills the __NAME__ tokens. Every value comes from this class, never from the machine's files.</summary>
        internal string Apply(string xaml)
        {
            foreach (KeyValuePair<string, string> kv in Tokens)
            {
                xaml = xaml.Replace("__" + kv.Key + "__", kv.Value);
            }
            return xaml;
        }

        private void Set(string name, string value)
        {
            Tokens[name] = value;
        }

        private static string Sys(string name)
        {
            return "{DynamicResource {x:Static SystemColors." + name + "BrushKey}}";
        }

        private static string Hex(byte alpha, Color c)
        {
            return "#" + alpha.ToString("X2", CultureInfo.InvariantCulture) + c.R.ToString("X2", CultureInfo.InvariantCulture)
                 + c.G.ToString("X2", CultureInfo.InvariantCulture) + c.B.ToString("X2", CultureInfo.InvariantCulture);
        }

        private static bool AppsUseDarkTheme()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object value = key == null ? null : key.GetValue("AppsUseLightTheme");
                    return value is int && (int)value == 0;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("settings", "could not read the app theme: " + Log.Describe(ex));
                return false;
            }
        }

        /// <summary>
        /// The shade of the accent colour Fluent uses for filled controls: a
        /// lighter one on dark surfaces and a darker one on light ones, so it
        /// always has enough contrast. Windows keeps all its shades in
        /// AccentPalette: three lighter, the accent itself, then three darker.
        /// </summary>
        private static Color? ReadAccent(bool dark)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent"))
                {
                    byte[] palette = key == null ? null : key.GetValue("AccentPalette") as byte[];
                    if (palette == null || palette.Length < 32) return null;
                    int i = (dark ? 1 : 4) * 4;
                    return Color.FromRgb(palette[i], palette[i + 1], palette[i + 2]);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("settings", "could not read the accent colour: " + Log.Describe(ex));
                return null;
            }
        }
    }

    /// <summary>
    /// The settings window, laid out like Windows 11's own Settings app: a
    /// column of cards, each with an icon, a name, a line of explanation and
    /// its control on the right. The controls are restyled standard WPF
    /// controls - CheckBox, RadioButton, Slider - so keyboard use and screen
    /// readers behave exactly as they do for unstyled ones.
    /// </summary>
    internal class SettingsWindow
    {
        // internal so the tests can check it parses and defines every control.
        // The __NAME__ tokens are filled by DialogTheme.Apply.
        internal const string Xaml =
@"<Window xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
        xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml""
        Title=""Nilam settings"" Width=""680"" Height=""820"" MinWidth=""600"" MinHeight=""440""
        WindowStartupLocation=""CenterScreen"" Background=""__BG__"" Foreground=""__TEXT__""
        FontFamily=""Segoe UI"" FontSize=""14"" UseLayoutRounding=""True""
        TextOptions.TextFormattingMode=""Display"">
  <Window.Resources>

    <!-- Keyboard focus: a rounded ring just outside the control. Shown only
         when focus arrived from the keyboard, never after a click. -->
    <Style x:Key=""Focus"">
      <Setter Property=""Control.Template"">
        <Setter.Value>
          <ControlTemplate>
            <Border Margin=""-3"" CornerRadius=""7"" BorderThickness=""2"" BorderBrush=""__TEXT__""/>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>

    <!-- Text -->
    <Style x:Key=""Section"" TargetType=""TextBlock"">
      <Setter Property=""FontWeight"" Value=""SemiBold""/>
      <Setter Property=""Margin"" Value=""1,28,0,8""/>
    </Style>
    <Style x:Key=""RowTitle"" TargetType=""TextBlock"">
      <Setter Property=""Foreground"" Value=""__TEXT__""/>
      <Setter Property=""TextWrapping"" Value=""Wrap""/>
    </Style>
    <Style x:Key=""RowDesc"" TargetType=""TextBlock"">
      <Setter Property=""FontSize"" Value=""12""/>
      <Setter Property=""Foreground"" Value=""__TEXT2__""/>
      <Setter Property=""TextWrapping"" Value=""Wrap""/>
      <Setter Property=""Margin"" Value=""0,1,0,0""/>
    </Style>
    <Style x:Key=""RowGlyph"" TargetType=""TextBlock"">
      <Setter Property=""FontFamily"" Value=""Segoe Fluent Icons, Segoe MDL2 Assets""/>
      <Setter Property=""FontSize"" Value=""18""/>
      <Setter Property=""Width"" Value=""20""/>
      <Setter Property=""TextAlignment"" Value=""Center""/>
      <Setter Property=""VerticalAlignment"" Value=""Center""/>
      <Setter Property=""Margin"" Value=""2,0,18,0""/>
      <Setter Property=""Foreground"" Value=""__TEXT__""/>
    </Style>
    <Style x:Key=""RowValue"" TargetType=""TextBlock"">
      <Setter Property=""Width"" Value=""40""/>
      <Setter Property=""TextAlignment"" Value=""Right""/>
      <Setter Property=""Margin"" Value=""0,0,12,0""/>
      <Setter Property=""VerticalAlignment"" Value=""Center""/>
      <Setter Property=""Foreground"" Value=""__TEXT2__""/>
    </Style>
    <Style x:Key=""TileIcon"" TargetType=""Border"">
      <Setter Property=""Width"" Value=""36""/>
      <Setter Property=""Height"" Value=""36""/>
      <Setter Property=""CornerRadius"" Value=""6""/>
      <Setter Property=""Background"" Value=""__ACCENTSOFT__""/>
      <Setter Property=""Margin"" Value=""0,0,12,0""/>
      <Setter Property=""VerticalAlignment"" Value=""Center""/>
    </Style>
    <Style x:Key=""TileGlyph"" TargetType=""TextBlock"">
      <Setter Property=""FontFamily"" Value=""Segoe Fluent Icons, Segoe MDL2 Assets""/>
      <Setter Property=""FontSize"" Value=""16""/>
      <Setter Property=""Foreground"" Value=""__ACCENT__""/>
      <Setter Property=""HorizontalAlignment"" Value=""Center""/>
      <Setter Property=""VerticalAlignment"" Value=""Center""/>
    </Style>
    <Style TargetType=""Hyperlink"">
      <Setter Property=""Foreground"" Value=""__ACCENT__""/>
      <Setter Property=""TextDecorations"" Value=""{x:Null}""/>
      <Style.Triggers>
        <Trigger Property=""IsMouseOver"" Value=""True"">
          <Setter Property=""TextDecorations"" Value=""Underline""/>
        </Trigger>
      </Style.Triggers>
    </Style>

    <!-- A settings card. -->
    <Style x:Key=""Row"" TargetType=""Border"">
      <Setter Property=""Background"" Value=""__CARD__""/>
      <Setter Property=""BorderBrush"" Value=""__STROKE__""/>
      <Setter Property=""BorderThickness"" Value=""1""/>
      <Setter Property=""CornerRadius"" Value=""6""/>
      <Setter Property=""Padding"" Value=""16,14,16,14""/>
      <Setter Property=""Margin"" Value=""0,0,0,4""/>
      <Setter Property=""MinHeight"" Value=""68""/>
      <Style.Triggers>
        <Trigger Property=""IsEnabled"" Value=""False"">
          <Setter Property=""Opacity"" Value=""0.45""/>
        </Trigger>
      </Style.Triggers>
    </Style>

    <!-- A whole card that is one on/off switch: clicking anywhere on it
         toggles, the way Windows Settings' own switch cards work. The card's
         content is whatever the CheckBox holds; Tag=""Tile"" hides the On/Off
         word for the narrower widget tiles. -->
    <Style x:Key=""Switch"" TargetType=""CheckBox"">
      <Setter Property=""Foreground"" Value=""__TEXT__""/>
      <Setter Property=""Padding"" Value=""16,14,16,14""/>
      <Setter Property=""Margin"" Value=""0,0,0,4""/>
      <Setter Property=""FocusVisualStyle"" Value=""{StaticResource Focus}""/>
      <Setter Property=""Template"">
        <Setter.Value>
          <ControlTemplate TargetType=""CheckBox"">
            <Border x:Name=""bd"" Background=""__CARD__"" BorderBrush=""__STROKE__"" BorderThickness=""1""
                    CornerRadius=""6"" Padding=""{TemplateBinding Padding}"" MinHeight=""68"">
              <DockPanel>
                <StackPanel DockPanel.Dock=""Right"" Orientation=""Horizontal"" VerticalAlignment=""Center"" Margin=""16,0,0,0"">
                  <TextBlock x:Name=""state"" Text=""Off"" MinWidth=""24"" Margin=""0,0,12,0"" VerticalAlignment=""Center""/>
                  <Grid Width=""40"" Height=""20"">
                    <Border x:Name=""track"" CornerRadius=""10"" BorderThickness=""1""
                            BorderBrush=""__TOGGLEOFF__"" Background=""__CTRL__""/>
                    <Ellipse x:Name=""knob"" Width=""12"" Height=""12"" Margin=""4,0,4,0""
                             HorizontalAlignment=""Left"" Fill=""__TOGGLEOFF__""/>
                  </Grid>
                </StackPanel>
                <ContentPresenter VerticalAlignment=""Center""/>
              </DockPanel>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property=""Tag"" Value=""Tile"">
                <Setter TargetName=""state"" Property=""Visibility"" Value=""Collapsed""/>
              </Trigger>
              <Trigger Property=""IsMouseOver"" Value=""True"">
                <Setter TargetName=""bd"" Property=""Background"" Value=""__CARDHOVER__""/>
                <Setter TargetName=""bd"" Property=""BorderBrush"" Value=""__HOVERSTROKE__""/>
                <Setter TargetName=""knob"" Property=""Width"" Value=""14""/>
                <Setter TargetName=""knob"" Property=""Height"" Value=""14""/>
                <Setter TargetName=""knob"" Property=""Margin"" Value=""3,0,3,0""/>
              </Trigger>
              <Trigger Property=""IsPressed"" Value=""True"">
                <Setter TargetName=""bd"" Property=""Background"" Value=""__CARDPRESSED__""/>
              </Trigger>
              <Trigger Property=""IsChecked"" Value=""True"">
                <Setter TargetName=""state"" Property=""Text"" Value=""On""/>
                <Setter TargetName=""track"" Property=""Background"" Value=""__ACCENT__""/>
                <Setter TargetName=""track"" Property=""BorderBrush"" Value=""__ACCENT__""/>
                <Setter TargetName=""knob"" Property=""Fill"" Value=""__ONACCENT__""/>
                <Setter TargetName=""knob"" Property=""HorizontalAlignment"" Value=""Right""/>
              </Trigger>
              <Trigger Property=""IsEnabled"" Value=""False"">
                <Setter TargetName=""bd"" Property=""Opacity"" Value=""0.45""/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>

    <!-- One option of a segmented choice. The group sits in a SegmentBox. -->
    <Style x:Key=""SegmentBox"" TargetType=""Border"">
      <Setter Property=""Background"" Value=""__CTRL__""/>
      <Setter Property=""BorderBrush"" Value=""__CTRLSTROKE__""/>
      <Setter Property=""BorderThickness"" Value=""1""/>
      <Setter Property=""CornerRadius"" Value=""6""/>
      <Setter Property=""Padding"" Value=""2""/>
      <Setter Property=""VerticalAlignment"" Value=""Center""/>
      <Setter Property=""Margin"" Value=""16,0,0,0""/>
    </Style>
    <Style x:Key=""Segment"" TargetType=""RadioButton"">
      <Setter Property=""Foreground"" Value=""__CTRLTEXT__""/>
      <Setter Property=""FocusVisualStyle"" Value=""{StaticResource Focus}""/>
      <Setter Property=""Template"">
        <Setter.Value>
          <ControlTemplate TargetType=""RadioButton"">
            <Border x:Name=""bd"" Background=""Transparent"" CornerRadius=""4"" Padding=""14,4,14,5"" MinWidth=""56"">
              <ContentPresenter HorizontalAlignment=""Center"" VerticalAlignment=""Center""/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property=""IsMouseOver"" Value=""True"">
                <Setter TargetName=""bd"" Property=""Background"" Value=""__CTRLHOVER__""/>
              </Trigger>
              <Trigger Property=""IsChecked"" Value=""True"">
                <Setter TargetName=""bd"" Property=""Background"" Value=""__ACCENT__""/>
                <Setter Property=""Foreground"" Value=""__ONACCENT__""/>
              </Trigger>
              <MultiTrigger>
                <MultiTrigger.Conditions>
                  <Condition Property=""IsChecked"" Value=""True""/>
                  <Condition Property=""IsMouseOver"" Value=""True""/>
                </MultiTrigger.Conditions>
                <Setter TargetName=""bd"" Property=""Background"" Value=""__ACCENTHOVER__""/>
              </MultiTrigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>

    <!-- Buttons -->
    <Style TargetType=""Button"">
      <Setter Property=""Foreground"" Value=""__CTRLTEXT__""/>
      <Setter Property=""Background"" Value=""__CTRL__""/>
      <Setter Property=""BorderBrush"" Value=""__CTRLSTROKE__""/>
      <Setter Property=""Padding"" Value=""16,4,16,6""/>
      <Setter Property=""MinWidth"" Value=""96""/>
      <Setter Property=""MinHeight"" Value=""32""/>
      <Setter Property=""FocusVisualStyle"" Value=""{StaticResource Focus}""/>
      <Setter Property=""Template"">
        <Setter.Value>
          <ControlTemplate TargetType=""Button"">
            <Border x:Name=""bd"" Background=""{TemplateBinding Background}"" BorderBrush=""{TemplateBinding BorderBrush}""
                    BorderThickness=""1"" CornerRadius=""4"" Padding=""{TemplateBinding Padding}"">
              <ContentPresenter HorizontalAlignment=""Center"" VerticalAlignment=""Center""/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property=""IsMouseOver"" Value=""True"">
                <Setter Property=""Background"" Value=""__CTRLHOVER__""/>
              </Trigger>
              <Trigger Property=""IsPressed"" Value=""True"">
                <Setter Property=""Background"" Value=""__CTRLPRESSED__""/>
                <Setter Property=""Foreground"" Value=""__TEXT2__""/>
              </Trigger>
              <Trigger Property=""IsEnabled"" Value=""False"">
                <Setter TargetName=""bd"" Property=""Opacity"" Value=""0.45""/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key=""Accent"" TargetType=""Button"" BasedOn=""{StaticResource {x:Type Button}}"">
      <Setter Property=""Background"" Value=""__ACCENT__""/>
      <Setter Property=""BorderBrush"" Value=""__ACCENT__""/>
      <Setter Property=""Foreground"" Value=""__ONACCENT__""/>
      <Style.Triggers>
        <Trigger Property=""IsMouseOver"" Value=""True"">
          <Setter Property=""Background"" Value=""__ACCENTHOVER__""/>
          <Setter Property=""BorderBrush"" Value=""__ACCENTHOVER__""/>
        </Trigger>
        <Trigger Property=""IsPressed"" Value=""True"">
          <Setter Property=""Background"" Value=""__ACCENTHOVER__""/>
          <Setter Property=""Foreground"" Value=""__ONACCENT__""/>
        </Trigger>
      </Style.Triggers>
    </Style>
    <Style x:Key=""Ghost"" TargetType=""Button"" BasedOn=""{StaticResource {x:Type Button}}"">
      <Setter Property=""Background"" Value=""Transparent""/>
      <Setter Property=""BorderBrush"" Value=""Transparent""/>
      <Setter Property=""FontFamily"" Value=""Segoe Fluent Icons, Segoe MDL2 Assets""/>
      <Setter Property=""FontSize"" Value=""14""/>
      <Setter Property=""MinWidth"" Value=""0""/>
      <Setter Property=""MinHeight"" Value=""0""/>
      <Setter Property=""Width"" Value=""32""/>
      <Setter Property=""Height"" Value=""26""/>
      <Setter Property=""Padding"" Value=""0""/>
    </Style>

    <!-- Text box: Fluent's accent underline marks the focused field. Tag holds
         the placeholder shown while it is empty. -->
    <Style TargetType=""TextBox"">
      <Setter Property=""Foreground"" Value=""__TEXT__""/>
      <Setter Property=""Background"" Value=""__INPUT__""/>
      <Setter Property=""BorderBrush"" Value=""__CTRLSTROKE__""/>
      <Setter Property=""CaretBrush"" Value=""__TEXT__""/>
      <Setter Property=""SelectionBrush"" Value=""__ACCENT__""/>
      <Setter Property=""Padding"" Value=""10,0,10,0""/>
      <Setter Property=""MinHeight"" Value=""32""/>
      <Setter Property=""VerticalContentAlignment"" Value=""Center""/>
      <Setter Property=""FocusVisualStyle"" Value=""{x:Null}""/>
      <Setter Property=""Template"">
        <Setter.Value>
          <ControlTemplate TargetType=""TextBox"">
            <Border x:Name=""bd"" Background=""{TemplateBinding Background}"" BorderBrush=""{TemplateBinding BorderBrush}""
                    BorderThickness=""1"" CornerRadius=""4"">
              <Grid>
                <!-- No margin here: the text box applies its own Padding inside
                     the host, so the hint below adds the same amount. -->
                <ScrollViewer x:Name=""PART_ContentHost"" Focusable=""False""
                              VerticalAlignment=""{TemplateBinding VerticalContentAlignment}""
                              HorizontalScrollBarVisibility=""Hidden"" VerticalScrollBarVisibility=""Hidden""/>
                <TextBlock x:Name=""hint"" Margin=""{TemplateBinding Padding}"" Padding=""3,0,0,0""
                           Text=""{Binding Tag, RelativeSource={RelativeSource TemplatedParent}}""
                           Foreground=""__TEXT3__"" VerticalAlignment=""Center"" IsHitTestVisible=""False""
                           Visibility=""Collapsed""/>
                <Border x:Name=""line"" Height=""2"" VerticalAlignment=""Bottom"" Background=""__ACCENT__""
                        CornerRadius=""0,0,3,3"" Visibility=""Collapsed""/>
              </Grid>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property=""Text"" Value="""">
                <Setter TargetName=""hint"" Property=""Visibility"" Value=""Visible""/>
              </Trigger>
              <Trigger Property=""IsMouseOver"" Value=""True"">
                <Setter TargetName=""bd"" Property=""Background"" Value=""__CTRLHOVER__""/>
              </Trigger>
              <Trigger Property=""IsKeyboardFocused"" Value=""True"">
                <Setter TargetName=""bd"" Property=""Background"" Value=""__INPUTFOCUS__""/>
                <Setter TargetName=""line"" Property=""Visibility"" Value=""Visible""/>
              </Trigger>
              <Trigger Property=""IsEnabled"" Value=""False"">
                <Setter TargetName=""bd"" Property=""Opacity"" Value=""0.45""/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>

    <!-- Slider and scroll bar parts -->
    <Style x:Key=""Blank"" TargetType=""RepeatButton"">
      <Setter Property=""Focusable"" Value=""False""/>
      <Setter Property=""IsTabStop"" Value=""False""/>
      <Setter Property=""Template"">
        <Setter.Value>
          <ControlTemplate TargetType=""RepeatButton"">
            <Border Background=""Transparent""/>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key=""SliderFill"" TargetType=""RepeatButton"">
      <Setter Property=""Focusable"" Value=""False""/>
      <Setter Property=""IsTabStop"" Value=""False""/>
      <Setter Property=""Template"">
        <Setter.Value>
          <ControlTemplate TargetType=""RepeatButton"">
            <Grid Background=""Transparent"">
              <Border Height=""4"" CornerRadius=""2"" Background=""__ACCENT__"" VerticalAlignment=""Center""/>
            </Grid>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key=""SliderThumb"" TargetType=""Thumb"">
      <Setter Property=""Template"">
        <Setter.Value>
          <ControlTemplate TargetType=""Thumb"">
            <Grid Width=""20"" Height=""20"" Background=""Transparent"">
              <Ellipse Fill=""__THUMB__"" Stroke=""__CTRLSTROKE__"" StrokeThickness=""1""/>
              <Ellipse x:Name=""dot"" Width=""12"" Height=""12"" Fill=""__ACCENT__""/>
            </Grid>
            <ControlTemplate.Triggers>
              <Trigger Property=""IsMouseOver"" Value=""True"">
                <Setter TargetName=""dot"" Property=""Width"" Value=""14""/>
                <Setter TargetName=""dot"" Property=""Height"" Value=""14""/>
              </Trigger>
              <Trigger Property=""IsDragging"" Value=""True"">
                <Setter TargetName=""dot"" Property=""Width"" Value=""10""/>
                <Setter TargetName=""dot"" Property=""Height"" Value=""10""/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style TargetType=""Slider"">
      <Setter Property=""FocusVisualStyle"" Value=""{StaticResource Focus}""/>
      <Setter Property=""Template"">
        <Setter.Value>
          <ControlTemplate TargetType=""Slider"">
            <Grid Height=""32"" Background=""Transparent"">
              <Border Height=""4"" CornerRadius=""2"" Background=""__TRACK__"" Margin=""2,0,2,0"" VerticalAlignment=""Center""/>
              <Track x:Name=""PART_Track"">
                <Track.DecreaseRepeatButton>
                  <RepeatButton Style=""{StaticResource SliderFill}"" Command=""{x:Static Slider.DecreaseLarge}""/>
                </Track.DecreaseRepeatButton>
                <Track.IncreaseRepeatButton>
                  <RepeatButton Style=""{StaticResource Blank}"" Command=""{x:Static Slider.IncreaseLarge}""/>
                </Track.IncreaseRepeatButton>
                <Track.Thumb>
                  <Thumb Style=""{StaticResource SliderThumb}""/>
                </Track.Thumb>
              </Track>
            </Grid>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key=""ScrollThumb"" TargetType=""Thumb"">
      <Setter Property=""Template"">
        <Setter.Value>
          <ControlTemplate TargetType=""Thumb"">
            <Grid Background=""Transparent"">
              <Border x:Name=""bar"" Width=""3"" CornerRadius=""1.5"" Background=""__TEXT3__"" HorizontalAlignment=""Center""/>
            </Grid>
            <ControlTemplate.Triggers>
              <Trigger Property=""IsMouseOver"" Value=""True"">
                <Setter TargetName=""bar"" Property=""Width"" Value=""6""/>
                <Setter TargetName=""bar"" Property=""CornerRadius"" Value=""3""/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <!-- Vertical only: every scroll area in this window has horizontal
         scrolling switched off. -->
    <Style TargetType=""ScrollBar"">
      <Setter Property=""Width"" Value=""14""/>
      <Setter Property=""MinWidth"" Value=""14""/>
      <Setter Property=""Template"">
        <Setter.Value>
          <ControlTemplate TargetType=""ScrollBar"">
            <Grid Background=""Transparent"" Margin=""0,4,0,4"">
              <Track x:Name=""PART_Track"" IsDirectionReversed=""True"">
                <Track.DecreaseRepeatButton>
                  <RepeatButton Style=""{StaticResource Blank}"" Command=""{x:Static ScrollBar.PageUpCommand}""/>
                </Track.DecreaseRepeatButton>
                <Track.IncreaseRepeatButton>
                  <RepeatButton Style=""{StaticResource Blank}"" Command=""{x:Static ScrollBar.PageDownCommand}""/>
                </Track.IncreaseRepeatButton>
                <Track.Thumb>
                  <Thumb Style=""{StaticResource ScrollThumb}""/>
                </Track.Thumb>
              </Track>
            </Grid>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>

    <!-- City search results -->
    <Style TargetType=""ListBoxItem"">
      <Setter Property=""Foreground"" Value=""__TEXT__""/>
      <Setter Property=""FocusVisualStyle"" Value=""{StaticResource Focus}""/>
      <Setter Property=""Template"">
        <Setter.Value>
          <ControlTemplate TargetType=""ListBoxItem"">
            <Border x:Name=""bd"" Background=""Transparent"" CornerRadius=""4"" Padding=""12,7,12,8"" Margin=""0,1,0,1"">
              <Grid>
                <Border x:Name=""pill"" Width=""3"" Height=""16"" CornerRadius=""1.5"" Background=""__ACCENT__""
                        HorizontalAlignment=""Left"" Margin=""-10,0,0,0"" Visibility=""Collapsed""/>
                <ContentPresenter/>
              </Grid>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property=""IsMouseOver"" Value=""True"">
                <Setter TargetName=""bd"" Property=""Background"" Value=""__CTRLHOVER__""/>
              </Trigger>
              <Trigger Property=""IsSelected"" Value=""True"">
                <Setter TargetName=""bd"" Property=""Background"" Value=""__CTRLPRESSED__""/>
                <Setter TargetName=""pill"" Property=""Visibility"" Value=""Visible""/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
  </Window.Resources>

  <Grid>
    <Grid.RowDefinitions>
      <RowDefinition Height=""*""/>
      <RowDefinition Height=""Auto""/>
    </Grid.RowDefinitions>

    <ScrollViewer VerticalScrollBarVisibility=""Auto"" HorizontalScrollBarVisibility=""Disabled"" Focusable=""False"">
      <StackPanel Margin=""32,20,32,32"">

        <TextBlock Text=""Settings"" FontSize=""28"" FontWeight=""SemiBold"" TextOptions.TextFormattingMode=""Ideal""/>
        <TextBlock Text=""Choose what your desktop widgets show and how they look.""
                   Foreground=""__TEXT2__"" Margin=""1,4,0,0"" TextWrapping=""Wrap""/>

        <!-- ============================================================ widgets -->
        <TextBlock Text=""Widgets"" Style=""{StaticResource Section}""/>
        <UniformGrid Columns=""2"" Margin=""-2,-2,-2,0"">
          <CheckBox x:Name=""CardClock"" Style=""{StaticResource Switch}"" Tag=""Tile"" Padding=""12"" Margin=""2""
                    AutomationProperties.Name=""Clock card"">
            <StackPanel Orientation=""Horizontal"">
              <Border Style=""{StaticResource TileIcon}""><TextBlock Style=""{StaticResource TileGlyph}"" Text=""&#xE823;""/></Border>
              <StackPanel VerticalAlignment=""Center"">
                <TextBlock Text=""Clock"" Style=""{StaticResource RowTitle}""/>
                <TextBlock Text=""Time and date"" Style=""{StaticResource RowDesc}""/>
              </StackPanel>
            </StackPanel>
          </CheckBox>
          <CheckBox x:Name=""CardWeather"" Style=""{StaticResource Switch}"" Tag=""Tile"" Padding=""12"" Margin=""2""
                    AutomationProperties.Name=""Weather card"">
            <StackPanel Orientation=""Horizontal"">
              <Border Style=""{StaticResource TileIcon}""><TextBlock Style=""{StaticResource TileGlyph}"" Text=""&#xE706;""/></Border>
              <StackPanel VerticalAlignment=""Center"">
                <TextBlock Text=""Weather"" Style=""{StaticResource RowTitle}""/>
                <TextBlock Text=""Conditions for your city"" Style=""{StaticResource RowDesc}""/>
              </StackPanel>
            </StackPanel>
          </CheckBox>
          <CheckBox x:Name=""CardMedia"" Style=""{StaticResource Switch}"" Tag=""Tile"" Padding=""12"" Margin=""2""
                    AutomationProperties.Name=""Media card"">
            <StackPanel Orientation=""Horizontal"">
              <Border Style=""{StaticResource TileIcon}""><TextBlock Style=""{StaticResource TileGlyph}"" Text=""&#xE8D6;""/></Border>
              <StackPanel VerticalAlignment=""Center"">
                <TextBlock Text=""Media"" Style=""{StaticResource RowTitle}""/>
                <TextBlock Text=""Now playing and controls"" Style=""{StaticResource RowDesc}""/>
              </StackPanel>
            </StackPanel>
          </CheckBox>
          <CheckBox x:Name=""CardSystem"" Style=""{StaticResource Switch}"" Tag=""Tile"" Padding=""12"" Margin=""2""
                    AutomationProperties.Name=""System card"">
            <StackPanel Orientation=""Horizontal"">
              <Border Style=""{StaticResource TileIcon}""><TextBlock Style=""{StaticResource TileGlyph}"" Text=""&#xE9D9;""/></Border>
              <StackPanel VerticalAlignment=""Center"">
                <TextBlock Text=""System"" Style=""{StaticResource RowTitle}""/>
                <TextBlock Text=""Processor, memory and disk"" Style=""{StaticResource RowDesc}""/>
              </StackPanel>
            </StackPanel>
          </CheckBox>
          <CheckBox x:Name=""CardBattery"" Style=""{StaticResource Switch}"" Tag=""Tile"" Padding=""12"" Margin=""2""
                    AutomationProperties.Name=""Battery card"">
            <StackPanel Orientation=""Horizontal"">
              <Border Style=""{StaticResource TileIcon}""><TextBlock Style=""{StaticResource TileGlyph}"" Text=""&#xE83F;""/></Border>
              <StackPanel VerticalAlignment=""Center"">
                <TextBlock Text=""Battery"" Style=""{StaticResource RowTitle}""/>
                <TextBlock x:Name=""CardBatteryNote"" Text=""Charge and power state"" Style=""{StaticResource RowDesc}""/>
              </StackPanel>
            </StackPanel>
          </CheckBox>
          <CheckBox x:Name=""CardNotes"" Style=""{StaticResource Switch}"" Tag=""Tile"" Padding=""12"" Margin=""2""
                    AutomationProperties.Name=""Notes card"">
            <StackPanel Orientation=""Horizontal"">
              <Border Style=""{StaticResource TileIcon}""><TextBlock Style=""{StaticResource TileGlyph}"" Text=""&#xE70B;""/></Border>
              <StackPanel VerticalAlignment=""Center"">
                <TextBlock Text=""Notes"" Style=""{StaticResource RowTitle}""/>
                <TextBlock Text=""A scratchpad on the desktop"" Style=""{StaticResource RowDesc}""/>
              </StackPanel>
            </StackPanel>
          </CheckBox>
          <CheckBox x:Name=""CardDayBanner"" Style=""{StaticResource Switch}"" Tag=""Tile"" Padding=""12"" Margin=""2""
                    AutomationProperties.Name=""Day banner"">
            <StackPanel Orientation=""Horizontal"">
              <Border Style=""{StaticResource TileIcon}""><TextBlock Style=""{StaticResource TileGlyph}"" Text=""&#xE787;""/></Border>
              <StackPanel VerticalAlignment=""Center"">
                <TextBlock Text=""Day banner"" Style=""{StaticResource RowTitle}""/>
                <TextBlock Text=""The weekday, large, up top"" Style=""{StaticResource RowDesc}""/>
              </StackPanel>
            </StackPanel>
          </CheckBox>
        </UniformGrid>

        <!-- ==================================================== weather and clock -->
        <TextBlock Text=""Weather and clock"" Style=""{StaticResource Section}""/>
        <Border Style=""{StaticResource Row}"">
          <StackPanel>
            <DockPanel>
              <TextBlock Style=""{StaticResource RowGlyph}"" Text=""&#xE774;""/>
              <StackPanel VerticalAlignment=""Center"">
                <TextBlock Text=""Location"" Style=""{StaticResource RowTitle}""/>
                <TextBlock x:Name=""PlaceText"" Style=""{StaticResource RowDesc}""/>
              </StackPanel>
            </DockPanel>
            <Grid Margin=""40,12,0,0"">
              <TextBox x:Name=""CityBox"" Tag=""Search for a city"" Padding=""10,0,40,0""
                       AutomationProperties.Name=""City to search for""/>
              <Button x:Name=""SearchButton"" Style=""{StaticResource Ghost}"" Content=""&#xE721;""
                      HorizontalAlignment=""Right"" Margin=""0,0,3,0"" ToolTip=""Search""
                      AutomationProperties.Name=""Search""/>
            </Grid>
            <TextBlock x:Name=""SearchStatus"" Style=""{StaticResource RowDesc}"" Margin=""40,8,0,0"" Visibility=""Collapsed""/>
            <ListBox x:Name=""CityList"" Margin=""40,6,0,0"" MaxHeight=""170"" Visibility=""Collapsed""
                     Background=""Transparent"" BorderThickness=""0"" Padding=""0""
                     ScrollViewer.HorizontalScrollBarVisibility=""Disabled""
                     AutomationProperties.Name=""Matching places""/>
          </StackPanel>
        </Border>
        <Border Style=""{StaticResource Row}"">
          <DockPanel>
            <Border DockPanel.Dock=""Right"" Style=""{StaticResource SegmentBox}"">
              <StackPanel Orientation=""Horizontal"" KeyboardNavigation.TabNavigation=""Once""
                          KeyboardNavigation.DirectionalNavigation=""Cycle"">
                <RadioButton x:Name=""UnitAuto"" GroupName=""Units"" Style=""{StaticResource Segment}"" Content=""Automatic""/>
                <RadioButton x:Name=""UnitC"" GroupName=""Units"" Style=""{StaticResource Segment}"" Content=""&#176;C""
                             AutomationProperties.Name=""Celsius""/>
                <RadioButton x:Name=""UnitF"" GroupName=""Units"" Style=""{StaticResource Segment}"" Content=""&#176;F""
                             AutomationProperties.Name=""Fahrenheit""/>
              </StackPanel>
            </Border>
            <TextBlock Style=""{StaticResource RowGlyph}"" Text=""&#xE9CA;""/>
            <StackPanel VerticalAlignment=""Center"">
              <TextBlock Text=""Temperature"" Style=""{StaticResource RowTitle}""/>
              <TextBlock Text=""Automatic follows your region"" Style=""{StaticResource RowDesc}""/>
            </StackPanel>
          </DockPanel>
        </Border>
        <Border Style=""{StaticResource Row}"">
          <DockPanel>
            <Border DockPanel.Dock=""Right"" Style=""{StaticResource SegmentBox}"">
              <StackPanel Orientation=""Horizontal"" KeyboardNavigation.TabNavigation=""Once""
                          KeyboardNavigation.DirectionalNavigation=""Cycle"">
                <RadioButton x:Name=""ClockAuto"" GroupName=""Clock"" Style=""{StaticResource Segment}"" Content=""Automatic""/>
                <RadioButton x:Name=""Clock12"" GroupName=""Clock"" Style=""{StaticResource Segment}"" Content=""12-hour""/>
                <RadioButton x:Name=""Clock24"" GroupName=""Clock"" Style=""{StaticResource Segment}"" Content=""24-hour""/>
              </StackPanel>
            </Border>
            <TextBlock Style=""{StaticResource RowGlyph}"" Text=""&#xE823;""/>
            <StackPanel VerticalAlignment=""Center"">
              <TextBlock Text=""Time format"" Style=""{StaticResource RowTitle}""/>
              <TextBlock Text=""Automatic follows your region"" Style=""{StaticResource RowDesc}""/>
            </StackPanel>
          </DockPanel>
        </Border>

        <!-- ============================================================== media -->
        <TextBlock Text=""Media card"" Style=""{StaticResource Section}""/>
        <CheckBox x:Name=""IdlePhotos"" Style=""{StaticResource Switch}""
                  AutomationProperties.Name=""Show pictures while nothing is playing"">
          <StackPanel Orientation=""Horizontal"">
            <TextBlock Style=""{StaticResource RowGlyph}"" Text=""&#xE8B9;""/>
            <StackPanel VerticalAlignment=""Center"">
              <TextBlock Text=""Show pictures while nothing is playing"" Style=""{StaticResource RowTitle}""/>
              <TextBlock Text=""The card cycles through photos from a folder when it is idle"" Style=""{StaticResource RowDesc}""/>
            </StackPanel>
          </StackPanel>
        </CheckBox>
        <Border x:Name=""PhotoRow"" Style=""{StaticResource Row}"">
          <DockPanel>
            <TextBlock Style=""{StaticResource RowGlyph}"" Text=""&#xE8B7;"" VerticalAlignment=""Top"" Margin=""2,1,18,0""/>
            <StackPanel>
              <TextBlock Text=""Picture folder"" Style=""{StaticResource RowTitle}""/>
              <DockPanel Margin=""0,10,0,0"">
                <Button x:Name=""BrowseButton"" DockPanel.Dock=""Right"" Content=""Browse"" Margin=""8,0,0,0""/>
                <TextBox x:Name=""PhotoFolder"" Tag=""Choose a folder of pictures"" AutomationProperties.Name=""Picture folder""/>
              </DockPanel>
            </StackPanel>
          </DockPanel>
        </Border>

        <!-- ========================================================= brightness -->
        <TextBlock Text=""Brightness"" Style=""{StaticResource Section}""/>
        <TextBlock x:Name=""BrightnessStatus"" Style=""{StaticResource RowDesc}"" Margin=""1,-2,0,10""
                   Text=""Looking for your displays...""/>
        <!-- One row per connected display, added once they have been found. -->
        <StackPanel x:Name=""BrightnessRows""/>

        <!-- ============================================================ taskbar -->
        <TextBlock Text=""Taskbar"" Style=""{StaticResource Section}""/>
        <!-- A DockPanel rather than a horizontal StackPanel, which would give
             the note unlimited width: it is replaced by a longer reason when
             the switch is unavailable, and must wrap rather than be cut off. -->
        <CheckBox x:Name=""ClearTaskbar"" Style=""{StaticResource Switch}""
                  AutomationProperties.Name=""Clear taskbar on the desktop"">
          <DockPanel>
            <TextBlock Style=""{StaticResource RowGlyph}"" Text=""&#xE771;""/>
            <StackPanel VerticalAlignment=""Center"">
              <TextBlock Text=""Clear taskbar on the desktop"" Style=""{StaticResource RowTitle}""/>
              <TextBlock x:Name=""ClearTaskbarNote"" Style=""{StaticResource RowDesc}""
                         Text=""See-through on each screen while nothing is open there""/>
            </StackPanel>
          </DockPanel>
        </CheckBox>

        <!-- ============================================================ general -->
        <TextBlock Text=""General"" Style=""{StaticResource Section}""/>
        <Border Style=""{StaticResource Row}"">
          <DockPanel>
            <StackPanel DockPanel.Dock=""Right"" Orientation=""Horizontal"" VerticalAlignment=""Center"" Margin=""16,0,0,0"">
              <TextBlock x:Name=""OpacityText"" Width=""40"" TextAlignment=""Right"" Margin=""0,0,12,0""
                         VerticalAlignment=""Center"" Foreground=""__TEXT2__""/>
              <Slider x:Name=""OpacitySlider"" Width=""200"" Minimum=""40"" Maximum=""100"" TickFrequency=""5""
                      IsSnapToTickEnabled=""True"" SmallChange=""5"" LargeChange=""10""
                      AutomationProperties.Name=""Card opacity""/>
            </StackPanel>
            <TextBlock Style=""{StaticResource RowGlyph}"" Text=""&#xE790;""/>
            <StackPanel VerticalAlignment=""Center"">
              <TextBlock Text=""Card opacity"" Style=""{StaticResource RowTitle}""/>
              <TextBlock Text=""How solid the card backgrounds are"" Style=""{StaticResource RowDesc}""/>
            </StackPanel>
          </DockPanel>
        </Border>
        <CheckBox x:Name=""StartupBox"" Style=""{StaticResource Switch}"" AutomationProperties.Name=""Start with Windows"">
          <StackPanel Orientation=""Horizontal"">
            <TextBlock Style=""{StaticResource RowGlyph}"" Text=""&#xE7E8;""/>
            <StackPanel VerticalAlignment=""Center"">
              <TextBlock Text=""Start with Windows"" Style=""{StaticResource RowTitle}""/>
              <TextBlock Text=""Show the widgets as soon as you sign in"" Style=""{StaticResource RowDesc}""/>
            </StackPanel>
          </StackPanel>
        </CheckBox>

        <!-- ============================================================== about -->
        <TextBlock Text=""About"" Style=""{StaticResource Section}""/>
        <Border Style=""{StaticResource Row}"">
          <DockPanel>
            <TextBlock DockPanel.Dock=""Right"" VerticalAlignment=""Center"" Margin=""16,0,0,0"">
              <Hyperlink x:Name=""FolderLink"">Open settings folder</Hyperlink>
            </TextBlock>
            <TextBlock Style=""{StaticResource RowGlyph}"" Text=""&#xE946;""/>
            <StackPanel VerticalAlignment=""Center"">
              <TextBlock Style=""{StaticResource RowTitle}""><Run x:Name=""VersionRun""/></TextBlock>
              <TextBlock Style=""{StaticResource RowDesc}"">
                <Hyperlink x:Name=""CreditLink"">Weather data by Open-Meteo.com</Hyperlink>
              </TextBlock>
            </StackPanel>
          </DockPanel>
        </Border>
      </StackPanel>
    </ScrollViewer>

    <Border Grid.Row=""1"" Background=""__FOOTER__"" BorderBrush=""__DIVIDER__"" BorderThickness=""0,1,0,0"" Padding=""32,16,32,16"">
      <StackPanel Orientation=""Horizontal"" HorizontalAlignment=""Right"">
        <Button x:Name=""SaveButton"" Style=""{StaticResource Accent}"" Content=""Save"" MinWidth=""120"" IsDefault=""True""/>
        <Button x:Name=""CancelButton"" Content=""Cancel"" MinWidth=""120"" Margin=""8,0,0,0"" IsCancel=""True""/>
      </StackPanel>
    </Border>
  </Grid>
</Window>";

        /// <summary>The window markup with a theme's colours filled in.</summary>
        internal static string Markup(DialogTheme theme)
        {
            return theme.Apply(Xaml);
        }

        private readonly WidgetApp app;
        private readonly Window window;
        private readonly DialogTheme theme;
        private readonly Settings draft;
        private readonly bool startupWas;
        private readonly Dictionary<string, CheckBox> cardBoxes = new Dictionary<string, CheckBox>();
        private readonly BrightnessQueue brightnessQueue = new BrightnessQueue(delegate(Action work) { Task.Run(work); });
        private readonly HashSet<BrightnessDisplay> brightnessChanged = new HashSet<BrightnessDisplay>();
        private List<BrightnessDisplay> brightnessDisplays = new List<BrightnessDisplay>();
        private bool saved;
        private bool windowClosed;

        private readonly TextBlock placeText, searchStatus, opacityText;
        private readonly TextBox cityBox, photoFolder;
        private readonly Button searchButton;
        private readonly ListBox cityList;
        private readonly RadioButton unitAuto, unitC, unitF, clockAuto, clock12, clock24;
        private readonly CheckBox idlePhotos, startupBox, clearTaskbar;
        private readonly Border photoRow;
        private readonly Slider opacity;

        private List<GeoPlace> places = new List<GeoPlace>();
        private WeatherLocation chosen;

        internal event EventHandler Closed;

        internal SettingsWindow(WidgetApp app)
        {
            this.app = app;
            draft = app.Store.Settings.Clone();
            theme = DialogTheme.Current();

            using (StringReader sr = new StringReader(Markup(theme)))
            using (XmlReader xr = XmlReader.Create(sr))
            {
                window = (Window)XamlReader.Load(xr);
            }

            // Tall enough to show most of the page, never taller than the screen.
            window.Height = Math.Max(window.MinHeight, Math.Min(window.Height, SystemParameters.WorkArea.Height - 48));
            window.SourceInitialized += delegate
            {
                if (theme.HighContrast) return;
                IntPtr hwnd = new WindowInteropHelper(window).Handle;
                Native.StyleTitleBar(hwnd, theme.Dark, theme.Background.R, theme.Background.G, theme.Background.B);
            };

            placeText = Find<TextBlock>("PlaceText");
            cityBox = Find<TextBox>("CityBox");
            searchButton = Find<Button>("SearchButton");
            searchStatus = Find<TextBlock>("SearchStatus");
            cityList = Find<ListBox>("CityList");
            unitAuto = Find<RadioButton>("UnitAuto");
            unitC = Find<RadioButton>("UnitC");
            unitF = Find<RadioButton>("UnitF");
            clockAuto = Find<RadioButton>("ClockAuto");
            clock12 = Find<RadioButton>("Clock12");
            clock24 = Find<RadioButton>("Clock24");
            idlePhotos = Find<CheckBox>("IdlePhotos");
            photoRow = Find<Border>("PhotoRow");
            photoFolder = Find<TextBox>("PhotoFolder");
            opacity = Find<Slider>("OpacitySlider");
            opacityText = Find<TextBlock>("OpacityText");
            startupBox = Find<CheckBox>("StartupBox");
            clearTaskbar = Find<CheckBox>("ClearTaskbar");

            foreach (string name in Settings.CardNames)
            {
                CheckBox box = Find<CheckBox>("Card" + name);
                box.IsChecked = draft.IsCardEnabled(name);
                cardBoxes[name] = box;
            }
            if (!Native.HasBattery())
            {
                // The card is only ever built on a machine with a battery.
                cardBoxes["Battery"].IsEnabled = false;
                Find<TextBlock>("CardBatteryNote").Text = "No battery in this PC";
            }

            ShowPlace(draft.Location);
            unitAuto.IsChecked = draft.Units == UnitChoice.Auto;
            unitC.IsChecked = draft.Units == UnitChoice.Celsius;
            unitF.IsChecked = draft.Units == UnitChoice.Fahrenheit;
            clockAuto.IsChecked = draft.Clock == ClockChoice.Auto;
            clock12.IsChecked = draft.Clock == ClockChoice.Hour12;
            clock24.IsChecked = draft.Clock == ClockChoice.Hour24;
            idlePhotos.IsChecked = draft.IdlePhotos;
            photoFolder.Text = draft.ResolvedPhotoFolder;
            photoRow.IsEnabled = draft.IdlePhotos;
            opacity.Value = draft.CardOpacity;
            opacityText.Text = draft.CardOpacity + "%";
            startupWas = StartupEntry.IsEnabled();
            startupBox.IsChecked = startupWas;
            string taskbarProblem = TaskbarStyle.Unavailable();
            clearTaskbar.IsChecked = draft.ClearTaskbar && taskbarProblem == null;
            if (taskbarProblem != null)
            {
                clearTaskbar.IsEnabled = false;
                Find<TextBlock>("ClearTaskbarNote").Text = taskbarProblem;
            }
            Find<Run>("VersionRun").Text = AppInfo.DisplayName + " " + AppInfo.Version;
            LoadBrightness();

            searchButton.Click += delegate { Search(); };
            cityBox.KeyDown += delegate(object s, KeyEventArgs e)
            {
                // Enter in the search box searches; it must not reach the
                // default button and save the dialog.
                if (e.Key != Key.Enter) return;
                e.Handled = true;
                Search();
            };
            cityList.SelectionChanged += delegate
            {
                int i = cityList.SelectedIndex;
                if (i < 0 || i >= places.Count) return;
                chosen = new WeatherLocation();
                chosen.Name = places[i].Name;
                chosen.Lat = places[i].Lat;
                chosen.Lon = places[i].Lon;
                placeText.Text = places[i].Label;
            };
            idlePhotos.Click += delegate { photoRow.IsEnabled = idlePhotos.IsChecked == true; };
            Find<Button>("BrowseButton").Click += delegate { BrowseForFolder(); };
            opacity.ValueChanged += delegate { opacityText.Text = (int)opacity.Value + "%"; };

            Find<Hyperlink>("CreditLink").Click += delegate { WidgetApp.Open(Weather.CreditUrl); };
            Find<Hyperlink>("FolderLink").Click += delegate { WidgetApp.Open(app.Store.Dir); };
            Find<Button>("SaveButton").Click += delegate { Save(); };
            Find<Button>("CancelButton").Click += delegate { window.Close(); };
            window.Closed += delegate
            {
                windowClosed = true;
                FinishBrightness();
                EventHandler handler = Closed;
                if (handler != null) handler(this, EventArgs.Empty);
            };
        }

        internal void Show()
        {
            window.Show();
            window.Activate();
        }

        internal void Activate()
        {
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
            window.Activate();
        }

        internal void Close()
        {
            window.Close();
        }

        private T Find<T>(string name) where T : class
        {
            T found = window.FindName(name) as T;
            if (found == null) throw new InvalidOperationException(
                "The settings markup has no " + typeof(T).Name + " named '" + name + "'.");
            return found;
        }

        private void ShowPlace(WeatherLocation where)
        {
            placeText.Text = where == null
                ? "Not set yet. Search for your city to turn the weather card on."
                : where.Name;
        }

        private void BrowseForFolder()
        {
            try
            {
                IntPtr owner = new WindowInteropHelper(window).Handle;
                string picked = FolderPicker.Pick(owner, "Choose a folder of pictures", photoFolder.Text.Trim());
                if (picked != null) photoFolder.Text = picked;
            }
            catch (Exception ex)
            {
                // The text box still works, so a failed dialog is not fatal.
                Log.Warn("settings", "the folder picker failed: " + Log.Describe(ex));
            }
        }

        /// <summary>
        /// Finding the displays takes a moment - each monitor has to answer over
        /// a slow link - so the rows appear when it is done and the window opens
        /// without waiting.
        /// </summary>
        private async void LoadBrightness()
        {
            TextBlock status = Find<TextBlock>("BrightnessStatus");
            List<BrightnessDisplay> found;
            try
            {
                found = await Task.Run(delegate { return Brightness.Discover(); });
            }
            catch (Exception ex)
            {
                Log.Warn("brightness", "could not list the displays: " + Log.Describe(ex));
                status.Text = "Your displays could not be read.";
                return;
            }

            if (windowClosed)
            {
                brightnessQueue.Finish(found);
                return;
            }

            brightnessDisplays = found;
            status.Text = found.Count == 0 ? "No displays were found." : "Changes show as you drag. Cancel puts them back.";
            StackPanel rows = Find<StackPanel>("BrightnessRows");
            foreach (BrightnessDisplay display in found)
            {
                BrightnessDisplay current = display;
                Border row = BrightnessRow(window, current.Name, current.Detail, current.CanChange ? current.Percent : -1);
                Slider slider = row.Tag as Slider;
                if (slider != null)
                {
                    TextBlock value = (TextBlock)slider.Tag;
                    // Hooked up only now, so setting the first value above
                    // does not send the monitor a change.
                    slider.ValueChanged += delegate
                    {
                        int percent = (int)slider.Value;
                        value.Text = percent + "%";
                        brightnessChanged.Add(current);
                        brightnessQueue.Set(current, percent);
                    };
                }
                rows.Children.Add(row);
            }
        }

        /// <summary>
        /// A card for one display. A percent below zero means it cannot be
        /// changed, and the card has no slider; otherwise the card's Tag is the
        /// Slider and the Slider's Tag is the TextBlock showing its value.
        /// </summary>
        internal static Border BrightnessRow(Window owner, string name, string detail, int percent)
        {
            TextBlock title = new TextBlock();
            title.Style = (Style)owner.FindResource("RowTitle");
            title.Text = name;
            TextBlock description = new TextBlock();
            description.Style = (Style)owner.FindResource("RowDesc");
            description.Text = detail;
            StackPanel text = new StackPanel();
            text.VerticalAlignment = VerticalAlignment.Center;
            text.Children.Add(title);
            text.Children.Add(description);

            TextBlock glyph = new TextBlock();
            glyph.Style = (Style)owner.FindResource("RowGlyph");
            glyph.Text = "\uE706";

            DockPanel layout = new DockPanel();
            Border row = new Border();
            row.Style = (Style)owner.FindResource("Row");
            row.Child = layout;

            if (percent >= 0)
            {
                TextBlock value = new TextBlock();
                value.Style = (Style)owner.FindResource("RowValue");
                value.Text = percent + "%";

                Slider slider = new Slider();
                slider.Width = 200;
                slider.Minimum = 0;
                slider.Maximum = 100;
                slider.TickFrequency = 1;
                slider.IsSnapToTickEnabled = true;
                slider.SmallChange = 5;
                slider.LargeChange = 10;
                slider.Value = percent;
                slider.Tag = value;
                AutomationProperties.SetName(slider, "Brightness of " + name);
                row.Tag = slider;

                StackPanel controls = new StackPanel();
                controls.Orientation = Orientation.Horizontal;
                controls.VerticalAlignment = VerticalAlignment.Center;
                controls.Margin = new Thickness(16, 0, 0, 0);
                controls.Children.Add(value);
                controls.Children.Add(slider);
                DockPanel.SetDock(controls, Dock.Right);
                layout.Children.Add(controls);
            }

            layout.Children.Add(glyph);
            layout.Children.Add(text);
            return row;
        }

        /// <summary>
        /// The monitors hold their own brightness, so nothing is stored in the
        /// settings file: saving keeps what is showing, and anything else puts
        /// back what the displays reported when the window opened.
        /// </summary>
        private void FinishBrightness()
        {
            if (!saved)
            {
                foreach (BrightnessDisplay display in brightnessChanged) brightnessQueue.Set(display, display.Percent);
            }
            brightnessQueue.Finish(brightnessDisplays);
        }

        private async void Search()
        {
            string query = cityBox.Text.Trim();
            if (query.Length < 2)
            {
                Status("Type at least two letters of a city name.");
                return;
            }

            Status("Searching...");
            searchButton.IsEnabled = false;
            cityList.Visibility = Visibility.Collapsed;
            try
            {
                string language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                List<GeoPlace> found = await Task.Run(delegate { return Weather.Search(query, language); });

                places = found;
                cityList.Items.Clear();
                foreach (GeoPlace p in found) cityList.Items.Add(p.Label);

                if (found.Count == 0)
                {
                    Status("No place called \"" + query + "\" was found. Check the spelling, or try a larger town nearby.");
                    return;
                }
                searchStatus.Visibility = Visibility.Collapsed;
                cityList.Visibility = Visibility.Visible;
                cityList.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                Log.Warn("settings", "city search failed: " + Log.Describe(ex));
                Status("The search could not reach Open-Meteo. Check your connection and try again.");
            }
            finally
            {
                searchButton.IsEnabled = true;
            }
        }

        private void Status(string message)
        {
            searchStatus.Text = message;
            searchStatus.Visibility = Visibility.Visible;
        }

        private void Save()
        {
            if (chosen != null) draft.Location = chosen;
            draft.Units = unitC.IsChecked == true ? UnitChoice.Celsius
                        : unitF.IsChecked == true ? UnitChoice.Fahrenheit : UnitChoice.Auto;
            draft.Clock = clock12.IsChecked == true ? ClockChoice.Hour12
                        : clock24.IsChecked == true ? ClockChoice.Hour24 : ClockChoice.Auto;
            foreach (KeyValuePair<string, CheckBox> kv in cardBoxes)
            {
                draft.SetCardEnabled(kv.Key, kv.Value.IsChecked == true);
            }
            draft.IdlePhotos = idlePhotos.IsChecked == true;

            // Stored empty while it is still the default, so the setting keeps
            // following the Pictures folder if that is ever moved.
            string folder = photoFolder.Text.Trim();
            draft.PhotoFolder = string.Equals(folder, Settings.DefaultPhotoPath, StringComparison.OrdinalIgnoreCase)
                ? "" : folder;
            draft.CardOpacity = (int)opacity.Value;
            // A switch greyed out because the feature cannot run here keeps
            // whatever was stored, for a copy of Nilam that can.
            if (clearTaskbar.IsEnabled) draft.ClearTaskbar = clearTaskbar.IsChecked == true;

            // Only touch the startup entry if the box was actually changed:
            // rewriting it on every save would repoint it at whichever copy of
            // the app happened to have the settings open.
            bool startup = startupBox.IsChecked == true;
            if (startup != startupWas) StartupEntry.Set(startup);

            saved = true;
            window.Close();
            app.ApplySettings(draft);
        }
    }
}
