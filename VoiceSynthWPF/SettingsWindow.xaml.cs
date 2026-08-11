using System.Windows;
using System.Windows.Input;
using NAudio.CoreAudioApi;
using SystemSpeech = System.Speech.Synthesis;
using WinRtSpeech  = Windows.Media.SpeechSynthesis;

namespace VoiceSynthWPF;

public partial class SettingsWindow : Window
{
    public Settings? ResultSettings { get; private set; }

    private readonly Settings _current;

    // Описания провайдеров для отображения в ComboBox
    private static readonly (TtsProviderType Type, string Label)[] Providers =
    [
        (TtsProviderType.Sapi,  "SAPI (встроенный Windows)"),
        (TtsProviderType.WinRt, "WinRT Neural (Microsoft Edge TTS)"),
        (TtsProviderType.Piper, "Piper TTS (локальная нейросеть)"),
    ];

    public SettingsWindow(Settings current)
    {
        InitializeComponent();
        _current = current;

        PopulateDevices();
        PopulateProviders();
        // Голоса заполняются в PopulateProviders через SelectionChanged

        SpeedSlider.Value  = current.VoiceSpeed;
        VolumeSlider.Value = current.VoiceVolume;
        HotKeyBringToFront.Text = current.HotKeyBringToFront.ToString();
    }

    private void PopulateDevices()
    {
        try
        {
            var enumerator = new MMDeviceEnumerator();
            var devices = enumerator
                .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                .Select(d => d.FriendlyName)
                .ToList();

            DeviceComboBox.ItemsSource = devices;

            var match = devices.FirstOrDefault(d => d == _current.VoiceInput)
                        ?? devices.FirstOrDefault(d => d.Contains(_current.VoiceInput))
                        ?? devices.FirstOrDefault();

            DeviceComboBox.SelectedItem = match;
        }
        catch (Exception ex)
        {
            DeviceComboBox.ItemsSource = new[] { $"Ошибка: {ex.Message}" };
        }
    }

    private void PopulateProviders()
    {
        ProviderComboBox.ItemsSource  = Providers.Select(p => p.Label).ToList();
        ProviderComboBox.SelectedIndex = Providers.IndexOf(p => p.Type == _current.TtsProvider);
        if (ProviderComboBox.SelectedIndex < 0) ProviderComboBox.SelectedIndex = 0;
    }

    private void ProviderComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        PopulateVoices(GetSelectedProvider());
    }

    private TtsProviderType GetSelectedProvider()
    {
        var idx = ProviderComboBox.SelectedIndex;
        return idx >= 0 && idx < Providers.Length ? Providers[idx].Type : TtsProviderType.Sapi;
    }

    private void PopulateVoices(TtsProviderType provider)
    {
        List<string> voices;
        try
        {
            voices = provider switch
            {
                TtsProviderType.WinRt => WinRtSpeech.SpeechSynthesizer.AllVoices
                    .Select(v => v.DisplayName)
                    .ToList(),

                TtsProviderType.Piper => new PiperTtsProvider("", 0)
                    .GetVoices()
                    .ToList(),

                _ => GetSapiVoices()
            };
        }
        catch (Exception ex)
        {
            voices = [$"Ошибка: {ex.Message}"];
        }

        VoiceComboBox.ItemsSource = voices;
        var match = voices.FirstOrDefault(v => v == _current.ReaderName)
                    ?? voices.FirstOrDefault();
        VoiceComboBox.SelectedItem = match;
    }

    private static List<string> GetSapiVoices()
    {
#pragma warning disable CA1416
        using var synth = new SystemSpeech.SpeechSynthesizer();
        return synth.GetInstalledVoices()
            .Select(v => v.VoiceInfo.Name)
            .ToList();
#pragma warning restore CA1416
    }

    private void TopBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void SpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SpeedLabel != null)
            SpeedLabel.Text = ((int)e.NewValue).ToString();
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (VolumeLabel != null)
            VolumeLabel.Text = ((int)e.NewValue).ToString();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var device   = DeviceComboBox.SelectedItem as string ?? string.Empty;
        var voice    = VoiceComboBox.SelectedItem  as string ?? string.Empty;
        var provider = GetSelectedProvider();

        ResultSettings = new Settings(
            voiceInput:          device,
            voiceSpeed:          (int)SpeedSlider.Value,
            voiceVolume:         (int)VolumeSlider.Value,
            stdDelay:            _current.StdDelay,
            readerName:          voice,
            hotKeyBringToFront:  Settings.StringToKey(HotKeyBringToFront.Text),
            ttsProvider:         provider
        );

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void InputKey_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        HotKeyBringToFront.Text = e.Key.ToString();
    }
}

// Маленький хелпер чтобы не тащить пакет
file static class ArrayExtensions
{
    public static int IndexOf<T>(this IEnumerable<T> source, Func<T, bool> predicate)
    {
        var i = 0;
        foreach (var item in source)
        {
            if (predicate(item)) return i;
            i++;
        }
        return -1;
    }
}
