using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using NAudio.CoreAudioApi;
using SystemSpeech = System.Speech.Synthesis;
using WinRtSpeech  = Windows.Media.SpeechSynthesis;

namespace VoiceSynthWPF.CustomControls;

public partial class SettingsWindow : Window
{
    public Settings? ResultSettings { get; private set; }

    private readonly Settings _current;

    private static readonly (TtsProviderType Type, string Label)[] Providers =
    [
        (TtsProviderType.Sapi,  "SAPI (Windows built-in)"),
        (TtsProviderType.WinRt, "WinRT Neural (Microsoft Edge TTS)"),
        (TtsProviderType.Piper, "Piper TTS (local neural)"),
    ];

    private static readonly (string Code, string Label)[] Languages =
    [
        ("en", "English"),
        ("ru", "Русский"),
    ];

    public SettingsWindow(Settings current)
    {
        InitializeComponent();
        _current = current;

        PopulateDevices();
        PopulateProviders();
        PopulateLanguages();

        SpeedSlider.Value  = current.VoiceSpeed;
        VolumeSlider.Value = current.VoiceVolume;
        // Если горячая клавиша задана — показываем её, иначе остаётся плейсхолдер из ресурса
        var hotKey = current.HotKeyBringToFront.ToString();
        if (current.HotKeyBringToFront != Key.None)
            HotKeyBringToFront.Text = hotKey;
    }

    // ─── Devices ─────────────────────────────────────────────────────────────

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
            DeviceComboBox.SelectedItem =
                devices.FirstOrDefault(d => d == _current.VoiceInput)
                ?? devices.FirstOrDefault(d => d.Contains(_current.VoiceInput))
                ?? devices.FirstOrDefault();
        }
        catch (Exception ex)
        {
            DeviceComboBox.ItemsSource = new[] { $"Error: {ex.Message}" };
        }
    }

    // ─── Providers ───────────────────────────────────────────────────────────

    private void PopulateProviders()
    {
        ProviderComboBox.ItemsSource   = Providers.Select(p => p.Label).ToList();
        ProviderComboBox.SelectedIndex = Providers.IndexOf(p => p.Type == _current.TtsProvider);
        if (ProviderComboBox.SelectedIndex < 0) ProviderComboBox.SelectedIndex = 0;
    }

    private void ProviderComboBox_SelectionChanged(object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        PopulateVoices(GetSelectedProvider());
    }

    private TtsProviderType GetSelectedProvider()
    {
        var idx = ProviderComboBox.SelectedIndex;
        return idx >= 0 && idx < Providers.Length ? Providers[idx].Type : TtsProviderType.Sapi;
    }

    // ─── Voices ──────────────────────────────────────────────────────────────

    private void PopulateVoices(TtsProviderType provider)
    {
        List<string> voices;
        try
        {
            voices = provider switch
            {
                TtsProviderType.WinRt => WinRtSpeech.SpeechSynthesizer.AllVoices
                    .Select(v => v.DisplayName).ToList(),

                TtsProviderType.Piper => new PiperTtsProvider("", 0)
                    .GetVoices().ToList(),

                _ => GetSapiVoices()
            };
        }
        catch (Exception ex)
        {
            voices = [$"Error: {ex.Message}"];
        }

        VoiceComboBox.ItemsSource  = voices;
        VoiceComboBox.SelectedItem = voices.FirstOrDefault(v => v == _current.ReaderName)
                                  ?? voices.FirstOrDefault();
    }

    private static List<string> GetSapiVoices()
    {
#pragma warning disable CA1416
        using var synth = new SystemSpeech.SpeechSynthesizer();
        return synth.GetInstalledVoices().Select(v => v.VoiceInfo.Name).ToList();
#pragma warning restore CA1416
    }

    // ─── Language ────────────────────────────────────────────────────────────

    private void PopulateLanguages()
    {
        LanguageComboBox.ItemsSource = Languages.Select(l => l.Label).ToList();

        var current = string.IsNullOrEmpty(_current.Language)
            ? LocalizationManager.CurrentLanguage
            : _current.Language;

        var idx = Languages.IndexOf(l => l.Code == current);
        LanguageComboBox.SelectedIndex = idx >= 0 ? idx : 0;
    }

    private void LanguageComboBox_SelectionChanged(object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var idx = LanguageComboBox.SelectedIndex;
        if (idx < 0 || idx >= Languages.Length) return;
        // Применяем превью сразу — пользователь видит результат не выходя из окна
        LocalizationManager.SetLanguage(Languages[idx].Code);
    }

    private string GetSelectedLanguageCode()
    {
        var idx = LanguageComboBox.SelectedIndex;
        return idx >= 0 && idx < Languages.Length ? Languages[idx].Code : "en";
    }

    // ─── Silero ──────────────────────────────────────────────────────────────

    private void SileroDownload_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName        = "https://github.com/snakers4/silero-models/releases",
            UseShellExecute = true
        });
    }

    // ─── Misc ─────────────────────────────────────────────────────────────────

    private void TopBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void SpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        => SpeedLabel?.SetCurrentValue(System.Windows.Controls.TextBlock.TextProperty,
               ((int)e.NewValue).ToString());

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        => VolumeLabel?.SetCurrentValue(System.Windows.Controls.TextBlock.TextProperty,
               ((int)e.NewValue).ToString());

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var device   = DeviceComboBox.SelectedItem as string ?? string.Empty;
        var voice    = VoiceComboBox.SelectedItem  as string ?? string.Empty;
        var provider = GetSelectedProvider();
        var lang     = GetSelectedLanguageCode();

        ResultSettings = new Settings(
            voiceInput:         device,
            voiceSpeed:         (int)SpeedSlider.Value,
            voiceVolume:        (int)VolumeSlider.Value,
            stdDelay:           _current.StdDelay,
            readerName:         voice,
            hotKeyBringToFront: Settings.StringToKey(HotKeyBringToFront.Text),
            ttsProvider:        provider,
            language:           lang
        );

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        // Откатываем язык если пользователь нажал отмену
        var savedLang = string.IsNullOrEmpty(_current.Language)
            ? LocalizationManager.CurrentLanguage
            : _current.Language;
        LocalizationManager.SetLanguage(savedLang);

        DialogResult = false;
        Close();
    }

    private void InputKey_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        HotKeyBringToFront.Text = e.Key.ToString();
    }
}

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
