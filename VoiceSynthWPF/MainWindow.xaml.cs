using System.Speech.Synthesis;
using System.Windows;
using System.Windows.Input;
using ConseqConcatenation;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using VoiceSynthWPF.CustomControls;
using WpfShapes = System.Windows.Shapes;

namespace VoiceSynthWPF;

public partial class MainWindow
{
    public static MainWindow? Instance;
    private static Settings? _settings;
    private ITtsProvider? _ttsProvider;
    private MMDevice? _cableDevice;

    private readonly Action<string> _synthHandler;

    private const string SnippetsFile = "snippets.сс";
    private const string FileName = "settings.json";
    
    public MainWindow()
    {
        InitializeComponent();
        Instance = this;
        
        var layout = InitGeometry();

        Loaded += async (_, _) =>
        {
            // Aero Glass — нужен реальный HWND, доступен после Loaded
            AeroGlass.Enable(this, useAcrylic: true, tintColor: 0xCC1A1A1A);
            AeroGlass.HookCompositionChange(this, useAcrylic: true, tintColor: 0xCC1A1A1A);

            // Инициализируем индикатор голосовой активности
            var titleText = TopPanelControl.TitleText;
            VoiceActivityIndicator.Init(titleText, (WpfShapes.Rectangle)FindName("CompactActivityBar"));

            await InitAsync();
            // Восстанавливаем компактный режим после загрузки UI
            if (layout.IsCompact)
                EnterCompactMode(restoring: true);
        };

        Action<string> onMessageSend = async void (text) =>
        {
            try
            {
                PushHistory(text);
                InputBox.Text = string.Empty;
                await SynthAsync(text);
                Scroll.ScrollToEnd();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                Log(e.Message);
            }
        };

        _synthHandler = async void (text) =>
        {
            try
            {
                await SynthAsync(text);
                Scroll.ScrollToEnd();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                Log(e.Message);
            }
        };
        
        InputBox.PreviewKeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Enter when !string.IsNullOrEmpty(InputBox.Text.Trim()):
                    onMessageSend.Invoke(InputBox.Text.Trim());
                    e.Handled = true;
                    break;
                case Key.Up:
                    NavigateHistory(InputBox, -1);
                    e.Handled = true;
                    break;
                case Key.Down:
                    NavigateHistory(InputBox, +1);
                    e.Handled = true;
                    break;
            }
        };

        CompactInputBox.PreviewKeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Enter when !string.IsNullOrEmpty(CompactInputBox.Text.Trim()):
                {
                    var text = CompactInputBox.Text.Trim();
                    PushHistory(text);
                    CompactInputBox.Text = string.Empty;
                    _synthHandler.Invoke(text);
                    e.Handled = true;
                    break;
                }
                case Key.Up:
                    NavigateHistory(CompactInputBox, -1);
                    e.Handled = true;
                    break;
                case Key.Down:
                    NavigateHistory(CompactInputBox, +1);
                    e.Handled = true;
                    break;
            }
        };
        
        CompactActivityBar.MouseDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        };

        // Snap к краям при перемещении компактного окна
        LocationChanged += (_, _) =>
        {
            if (_isCompact)
                SnapToEdge();
        };
        
        GlobalKeyboardHook.Start();
        GlobalKeyboardHook.KeyPressed += OnGlobalKeyPressed;
    }
    
    private async Task InitAsync()
    {
        _settings = await Settings.Load(Environment.CurrentDirectory, FileName);
        // Применяем сохранённый язык (если пустой — уже выбран системный в App.OnStartup)
        if (!string.IsNullOrEmpty(_settings.Language))
            LocalizationManager.SetLanguage(_settings.Language);
        await ApplySettingsAsync(_settings);
        await LoadSnippets();
    }

    private async Task<bool> ApplySettingsAsync(Settings settings)
    {
        _ttsProvider?.Dispose();
        _ttsProvider = null;

        var enumerator = new MMDeviceEnumerator();
        var allDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();

        _cableDevice = allDevices.FirstOrDefault(d => d.FriendlyName == settings.VoiceInput)
                    ?? allDevices.FirstOrDefault(d => d.FriendlyName.Contains(settings.VoiceInput));

        if (_cableDevice == null)
        {
            var available = string.Join(", ", allDevices.Select(d => d.FriendlyName));
            Log($"[ОШИБКА] Аудиоустройство '{settings.VoiceInput}' не найдено.");
            Log($"Доступные устройства: {available}");
            Log("Откройте Настройки и выберите нужное устройство.");
            return false;
        }

        try
        {
            _ttsProvider = TtsProviderFactory.Create(settings);
            var voices = _ttsProvider.GetVoices();
            Log($"Провайдер: {settings.TtsProvider} | Найдено голосов: {voices.Count}");
            foreach (var v in voices) Log($"  • {v}");
            Log($"Выбран голос: {settings.ReaderName}");
        }
        catch (Exception ex)
        {
            Log($"[ОШИБКА] Не удалось создать TTS провайдер: {ex.Message}");
            return false;
        }

        await Task.CompletedTask;
        return true;
    }

    public async Task OpenSettingsAsync()
    {
        var current = _settings ?? Settings.Default;
        var window = new SettingsWindow(current) { Owner = this };

        if (window.ShowDialog() != true || window.ResultSettings == null)
            return;

        _settings = window.ResultSettings;

        var ok = await ApplySettingsAsync(_settings);
        if (ok)
            Log("Настройки применены.");

        await _settings.Save(Environment.CurrentDirectory, FileName);
    }
    
    private async Task SynthAsync(string text)
    {
        if (_ttsProvider == null)
        {
            Log("[ОШИБКА] TTS провайдер не инициализирован. Проверьте настройки.");
            return;
        }

        if (_cableDevice == null)
        {
            Log("[ОШИБКА] Аудиоустройство не найдено. Откройте Настройки и выберите устройство.");
            return;
        }

        Log($"=> {text}");

        VoiceActivityIndicator.OnSpeechStart();
        try
        {
            var audioStream = await Task.Run(() => _ttsProvider.SynthToStreamAsync(text));

            var tcs       = new TaskCompletionSource();
            var reader    = new WaveFileReader(audioStream);
            var wasapiOut = new WasapiOut(_cableDevice, AudioClientShareMode.Shared, false, 100);

            wasapiOut.Init(reader);
            wasapiOut.PlaybackStopped += (_, _) =>
            {
                wasapiOut.Dispose();
                reader.Dispose();
                audioStream.Dispose();
                tcs.TrySetResult();
            };
            wasapiOut.Play();

            await tcs.Task;
        }
        finally
        {
            VoiceActivityIndicator.OnSpeechStop();
        }
    }
    
    private void OnGlobalKeyPressed(Key key)
    {
        if (key == _settings!.HotKeyBringToFront)
        {
            BringToFront();
        }
        
        Dispatcher.Invoke(() =>
        {
            foreach (var button in Snippets.Children.OfType<NumButton>())
            {
                if (button.ActivationKey == key)
                {
                    _synthHandler?.Invoke(button.FullText);
                }
            }
        });
    }
    
    protected override async void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        try
        {
            // Сохраняем геометрию окна
            new WindowLayout
            {
                Left      = _isCompact ? _normalLeft   : Left,
                Top       = _isCompact ? _normalTop    : Top,
                Width     = _isCompact ? _normalWidth  : Width,
                Height    = _isCompact ? _normalHeight : Height,
                IsCompact = _isCompact,
                // Компактная позиция
                NormalLeft   = _normalLeft,
                NormalTop    = _normalTop,
                NormalWidth  = _normalWidth,
                NormalHeight = _normalHeight
            }.Save();

            Log("Сохраняем настройки и выходим.");
            await SaveSnippetsAsync();
            await _settings?.Save(Environment.CurrentDirectory, FileName)!;
            base.OnClosing(e);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            Log(ex.Message);
        }
    }
    
    public void Log(string message) => OutputBox.Text += message + Environment.NewLine;
}