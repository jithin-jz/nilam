using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Navigation;
using System.Xml;

namespace Nilam
{
    /// <summary>
    /// The settings window. Unlike the cards it is an ordinary window with
    /// standard, unstyled controls - so keyboard navigation, screen readers and
    /// Windows contrast themes all work without any effort on our part.
    /// </summary>
    internal class SettingsWindow
    {
        // internal so the tests can check it parses and defines every control.
        internal const string Xaml =
@"<Window xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
        xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml""
        Title=""Nilam settings"" Width=""460"" SizeToContent=""Height""
        ResizeMode=""NoResize"" WindowStartupLocation=""CenterScreen""
        FontFamily=""Segoe UI"" FontSize=""13"" UseLayoutRounding=""True""
        TextOptions.TextFormattingMode=""Display"">
  <Window.Resources>
    <Style x:Key=""Head"" TargetType=""TextBlock"">
      <Setter Property=""FontWeight"" Value=""SemiBold""/>
      <Setter Property=""Margin"" Value=""0,16,0,8""/>
    </Style>
    <Style x:Key=""Label"" TargetType=""TextBlock"">
      <Setter Property=""Width"" Value=""104""/>
      <Setter Property=""VerticalAlignment"" Value=""Center""/>
    </Style>
    <Style TargetType=""RadioButton"">
      <Setter Property=""Margin"" Value=""0,0,18,0""/>
      <Setter Property=""VerticalContentAlignment"" Value=""Center""/>
    </Style>
    <Style TargetType=""CheckBox"">
      <Setter Property=""Margin"" Value=""0,0,0,7""/>
      <Setter Property=""VerticalContentAlignment"" Value=""Center""/>
    </Style>
    <Style TargetType=""Button"">
      <Setter Property=""MinWidth"" Value=""80""/>
      <Setter Property=""Padding"" Value=""12,3,12,3""/>
    </Style>
  </Window.Resources>
  <StackPanel Margin=""22,6,22,20"">

    <TextBlock Text=""Weather"" Style=""{StaticResource Head}""/>
    <TextBlock x:Name=""PlaceText"" Margin=""0,0,0,8"" TextWrapping=""Wrap""/>
    <DockPanel>
      <Button x:Name=""SearchButton"" DockPanel.Dock=""Right"" Content=""Search"" Margin=""8,0,0,0""/>
      <TextBox x:Name=""CityBox"" Padding=""3"" AutomationProperties.Name=""City to search for""/>
    </DockPanel>
    <TextBlock x:Name=""SearchStatus"" Margin=""0,6,0,0"" TextWrapping=""Wrap"" Visibility=""Collapsed""/>
    <ListBox x:Name=""CityList"" MaxHeight=""116"" Margin=""0,6,0,0"" Visibility=""Collapsed""
             AutomationProperties.Name=""Matching places""/>
    <StackPanel Orientation=""Horizontal"" Margin=""0,12,0,0"">
      <TextBlock Text=""Temperature"" Style=""{StaticResource Label}""/>
      <RadioButton x:Name=""UnitAuto"" GroupName=""Units"" Content=""Automatic""/>
      <RadioButton x:Name=""UnitC"" GroupName=""Units"" Content=""&#176;C""/>
      <RadioButton x:Name=""UnitF"" GroupName=""Units"" Content=""&#176;F""/>
    </StackPanel>

    <TextBlock Text=""Clock"" Style=""{StaticResource Head}""/>
    <StackPanel Orientation=""Horizontal"">
      <TextBlock Text=""Time format"" Style=""{StaticResource Label}""/>
      <RadioButton x:Name=""ClockAuto"" GroupName=""Clock"" Content=""Automatic""/>
      <RadioButton x:Name=""Clock12"" GroupName=""Clock"" Content=""12-hour""/>
      <RadioButton x:Name=""Clock24"" GroupName=""Clock"" Content=""24-hour""/>
    </StackPanel>

    <TextBlock Text=""Cards"" Style=""{StaticResource Head}""/>
    <UniformGrid Columns=""3"">
      <CheckBox x:Name=""CardClock"" Content=""Clock""/>
      <CheckBox x:Name=""CardWeather"" Content=""Weather""/>
      <CheckBox x:Name=""CardMedia"" Content=""Media""/>
      <CheckBox x:Name=""CardSystem"" Content=""System""/>
      <CheckBox x:Name=""CardBattery"" Content=""Battery""/>
      <CheckBox x:Name=""CardNotes"" Content=""Notes""/>
      <CheckBox x:Name=""CardDayBanner"" Content=""Day banner""/>
    </UniformGrid>

    <TextBlock Text=""Media card"" Style=""{StaticResource Head}""/>
    <CheckBox x:Name=""IdlePhotos"" Content=""Show pictures from a folder while nothing is playing""/>
    <DockPanel>
      <TextBlock Text=""Folder"" Style=""{StaticResource Label}""/>
      <TextBox x:Name=""PhotoFolder"" Padding=""3"" AutomationProperties.Name=""Picture folder""/>
    </DockPanel>

    <TextBlock Text=""Appearance"" Style=""{StaticResource Head}""/>
    <DockPanel>
      <TextBlock Text=""Card opacity"" Style=""{StaticResource Label}""/>
      <TextBlock x:Name=""OpacityText"" DockPanel.Dock=""Right"" Width=""44"" TextAlignment=""Right""
                 VerticalAlignment=""Center""/>
      <Slider x:Name=""OpacitySlider"" Minimum=""40"" Maximum=""100"" TickFrequency=""5""
              IsSnapToTickEnabled=""True"" SmallChange=""5"" LargeChange=""10""
              AutomationProperties.Name=""Card opacity""/>
    </DockPanel>
    <CheckBox x:Name=""StartupBox"" Content=""Start with Windows"" Margin=""0,16,0,0""/>

    <Separator Margin=""0,16,0,14""/>
    <DockPanel>
      <StackPanel DockPanel.Dock=""Right"" Orientation=""Horizontal"" VerticalAlignment=""Bottom"">
        <Button x:Name=""SaveButton"" Content=""Save"" IsDefault=""True""/>
        <Button x:Name=""CancelButton"" Content=""Cancel"" Margin=""8,0,0,0"" IsCancel=""True""/>
      </StackPanel>
      <TextBlock VerticalAlignment=""Center"" TextWrapping=""Wrap"" FontSize=""12"">
        <Run x:Name=""VersionRun""/><LineBreak/>
        <Hyperlink x:Name=""CreditLink"">Weather data by Open-Meteo.com</Hyperlink><LineBreak/>
        <Hyperlink x:Name=""FolderLink"">Open the settings folder</Hyperlink>
      </TextBlock>
    </DockPanel>
  </StackPanel>
</Window>";

        private readonly WidgetApp app;
        private readonly Window window;
        private readonly Settings draft;
        private readonly bool startupWas;
        private readonly Dictionary<string, CheckBox> cardBoxes = new Dictionary<string, CheckBox>();

        private readonly TextBlock placeText, searchStatus, opacityText;
        private readonly TextBox cityBox, photoFolder;
        private readonly Button searchButton;
        private readonly ListBox cityList;
        private readonly RadioButton unitAuto, unitC, unitF, clockAuto, clock12, clock24;
        private readonly CheckBox idlePhotos, startupBox;
        private readonly Slider opacity;

        private List<GeoPlace> places = new List<GeoPlace>();
        private WeatherLocation chosen;

        internal event EventHandler Closed;

        internal SettingsWindow(WidgetApp app)
        {
            this.app = app;
            draft = app.Store.Settings.Clone();

            using (StringReader sr = new StringReader(Xaml))
            using (XmlReader xr = XmlReader.Create(sr))
            {
                window = (Window)XamlReader.Load(xr);
            }

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
            photoFolder = Find<TextBox>("PhotoFolder");
            opacity = Find<Slider>("OpacitySlider");
            opacityText = Find<TextBlock>("OpacityText");
            startupBox = Find<CheckBox>("StartupBox");

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
                cardBoxes["Battery"].Content = "Battery (none found)";
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
            photoFolder.IsEnabled = draft.IdlePhotos;
            opacity.Value = draft.CardOpacity;
            opacityText.Text = draft.CardOpacity + "%";
            startupWas = StartupEntry.IsEnabled();
            startupBox.IsChecked = startupWas;
            Find<Run>("VersionRun").Text = AppInfo.DisplayName + " " + AppInfo.Version;

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
                placeText.Text = "Showing weather for " + places[i].Label + ".";
            };
            idlePhotos.Click += delegate { photoFolder.IsEnabled = idlePhotos.IsChecked == true; };
            opacity.ValueChanged += delegate { opacityText.Text = (int)opacity.Value + "%"; };

            Find<Hyperlink>("CreditLink").Click += delegate { WidgetApp.Open(Weather.CreditUrl); };
            Find<Hyperlink>("FolderLink").Click += delegate { WidgetApp.Open(app.Store.Dir); };
            Find<Button>("SaveButton").Click += delegate { Save(); };
            Find<Button>("CancelButton").Click += delegate { window.Close(); };
            window.Closed += delegate
            {
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
                ? "No city chosen yet. Search for yours to turn the weather card on."
                : "Showing weather for " + where.Name + ". Search to change it.";
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

            // Only touch the startup entry if the box was actually changed:
            // rewriting it on every save would repoint it at whichever copy of
            // the app happened to have the settings open.
            bool startup = startupBox.IsChecked == true;
            if (startup != startupWas) StartupEntry.Set(startup);

            window.Close();
            app.ApplySettings(draft);
        }
    }
}
