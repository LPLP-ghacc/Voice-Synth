using System.IO;
using System.Speech.Synthesis;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using ConseqConcatenation;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using static System.Enum;
using WpfShapes = System.Windows.Shapes;

namespace VoiceSynthWPF;

// ─── WindowState persistence ─────────────────────────────────────────────────

public class WindowLayout
{
    public double Left      { get; init; } = 100;
    public double Top       { get; init; } = 100;
    public double Width     { get; init; } = 800;
    public double Height    { get; init; } = 450;
    public bool   IsCompact { get; init; } = false;
    // Геометрия нормального режима (сохраняется даже когда активен компактный)
    public double NormalLeft   { get; init; } = 100;
    public double NormalTop    { get; init; } = 100;
    public double NormalWidth  { get; init; } = 800;
    public double NormalHeight { get; init; } = 450;

    private static readonly string Path =
        System.IO.Path.Combine(Environment.CurrentDirectory, "window.json");

    public static WindowLayout Load()
    {
        try
        {
            if (File.Exists(Path))
            {
                var json = File.ReadAllText(Path);
                return JsonSerializer.Deserialize<WindowLayout>(json) ?? new WindowLayout();
            }
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception.StackTrace);
        }
        return new WindowLayout();
    }

    public void Save()
    {
        try { File.WriteAllText(Path, JsonSerializer.Serialize(this)); }
        catch (Exception exception)
        {
            Console.WriteLine(exception.StackTrace);
        }
    }
}

// ─── Settings ────────────────────────────────────────────────────────────────

public class Settings(
    string voiceInput,
    int voiceSpeed,
    int voiceVolume,
    int stdDelay,
    string readerName,
    Key hotKeyBringToFront,
    TtsProviderType ttsProvider = TtsProviderType.Sapi,
    string language = "")
{
    public string VoiceInput { get; } = voiceInput;
    public int VoiceSpeed { get; } = voiceSpeed;
    public int VoiceVolume { get; } = voiceVolume;
    public int StdDelay { get; } = stdDelay;
    public string ReaderName { get; } = readerName;
    public Key HotKeyBringToFront { get; } = hotKeyBringToFront;
    public TtsProviderType TtsProvider { get; } = ttsProvider;
    public string Language { get; } = language;

    public static Settings Default { get; } =
        new("CABLE Input", 0, 100, 10, "Microsoft Irina", Key.F12, TtsProviderType.Sapi, "");
    
    public async Task Save(string path, string fileName)
    {
        var json = JsonSerializer.Serialize(this);
        
        await File.WriteAllTextAsync(Path.Combine(path, fileName), json);
    }

    public static async Task<Settings> Load(string path, string fileName)
    {
        if (!File.Exists(Path.Combine(path, fileName)))
        {
            MainWindow.Instance?.Log($"Файл {Path.Combine(path, fileName)} не найден, либо невозможно считать.");
            MainWindow.Instance?.Log($"Создаем новый файл сохранения ({fileName}).");
            
            var tempSave = JsonSerializer.Serialize(Default);
            await File.WriteAllTextAsync(Path.Combine(path, fileName), tempSave);
            
            return Default;
        }
        
        var json = await File.ReadAllTextAsync(Path.Combine(path, fileName));
        try
        {
            var settings = JsonSerializer.Deserialize<Settings>(json);
                
            if (settings != null)
            {
                return settings;
            }
        }
        catch
        {
            MainWindow.Instance?.Log($"Файл {Path.Combine(path, fileName)} не найден, либо невозможно считать.");
            return Default;
        }

        return Default;
    }

    public static Key StringToKey(string key)
    {
        var result = Key.None;
        try
        {
#pragma warning disable CA1806
            TryParse(key, out result);
#pragma warning restore CA1806
        }
        catch (Exception exception)
        {
            MainWindow.Instance!.Log(exception.ToString());
        }

        return result;
    }
}

public partial class MainWindow
{
    public static MainWindow? Instance;
    private static Settings? _settings;
    private ITtsProvider? _ttsProvider;
    private MMDevice? _cableDevice;

    private readonly Action<string> _synthHandler;

    private const string SnippetsFile = "snippets.сс";
    private const string FileName = "settings.json";

    // Компактный режим
    private bool _isCompact;
    private double _normalLeft, _normalTop, _normalWidth, _normalHeight;

    private const double CompactHeight = 36;
    private const double CompactWidth  = 420;
    private const int    SnapDistance  = 20; // px до края для "прилипания"

    // История ввода (как в консоли)
    private readonly List<string> _inputHistory = [];
    private int _historyIndex = -1; // -1 = не в режиме истории

    public MainWindow()
    {
        InitializeComponent();
        Instance = this;

        // Восстанавливаем геометрию ДО отрисовки
        var layout = WindowLayout.Load();

        // Клампим позицию в пределы рабочего стола
        var workArea = SystemParameters.WorkArea;
        Left   = Math.Max(workArea.Left, Math.Min(layout.Left, workArea.Right  - layout.Width));
        Top    = Math.Max(workArea.Top,  Math.Min(layout.Top,  workArea.Bottom - layout.Height));
        Width  = layout.Width;
        Height = layout.Height;
        _normalLeft   = layout.NormalLeft;
        _normalTop    = layout.NormalTop;
        _normalWidth  = layout.NormalWidth;
        _normalHeight = layout.NormalHeight;

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

        // Snap к краям при перемещении компактного окна
        LocationChanged += (_, _) =>
        {
            if (_isCompact)
                SnapToEdge();
        };
        
        GlobalKeyboardHook.Start();
        GlobalKeyboardHook.KeyPressed += OnGlobalKeyPressed;
    }

    // ─── История ввода ───────────────────────────────────────────────────────

    private void PushHistory(string text)
    {
        // Не дублируем последний элемент
        if (_inputHistory.Count == 0 || _inputHistory[^1] != text)
            _inputHistory.Add(text);
        _historyIndex = -1; // сбрасываем курсор истории
    }

    /// <param name="box"></param>
    /// <param name="direction">-1 = вверх (старее), +1 = вниз (новее)</param>
    private void NavigateHistory(System.Windows.Controls.TextBox box, int direction)
    {
        if (_inputHistory.Count == 0) return;

        if (_historyIndex == -1)
        {
            // Начинаем навигацию только вверх
            if (direction == -1)
                _historyIndex = _inputHistory.Count - 1;
            else
                return;
        }
        else
        {
            _historyIndex += direction;

            if (_historyIndex < 0)
            {
                _historyIndex = 0;
                return;
            }

            if (_historyIndex >= _inputHistory.Count)
            {
                // Дошли до конца — очищаем поле
                _historyIndex = -1;
                box.Text = string.Empty;
                return;
            }
        }

        box.Text = _inputHistory[_historyIndex];
        box.CaretIndex = box.Text.Length;
    }

    // ─── Компактный режим ────────────────────────────────────────────────────

    /// <summary>Переключить компактный / нормальный режим.</summary>
    public void ToggleCompactMode()
    {
        if (_isCompact)
            ExitCompactMode();
        else
            EnterCompactMode();
    }

    private void EnterCompactMode(bool restoring = false)
    {
        if (!restoring)
        {
            // Запоминаем текущую геометрию нормального режима
            _normalLeft   = Left;
            _normalTop    = Top;
            _normalWidth  = Width;
            _normalHeight = Height;
        }

        _isCompact = true;

        NormalModeBorder.Visibility  = Visibility.Collapsed;
        CompactModeBorder.Visibility = Visibility.Visible;

        MinWidth  = 0;
        MinHeight = 0;
        ResizeMode = ResizeMode.NoResize;

        Width  = CompactWidth;
        Height = CompactHeight;

        SnapToEdge();

        Topmost = true;
        CompactInputBox.Focus();

        // Переприменяем стекло после смены размеров
        AeroGlass.Enable(this, useAcrylic: true, tintColor: 0xCC1A1A1A);
    }

    private void ExitCompactMode()
    {
        _isCompact = false;

        NormalModeBorder.Visibility  = Visibility.Visible;
        CompactModeBorder.Visibility = Visibility.Collapsed;

        MinWidth  = 800;
        MinHeight = 450;
        ResizeMode = ResizeMode.CanResize;
        Topmost = false;

        Width  = _normalWidth;
        Height = _normalHeight;
        Left   = _normalLeft;
        Top    = _normalTop;

        InputBox.Focus();
    }

    /// <summary>
    /// Прилипает окно к ближайшей грани рабочего стола.
    /// Рассматривает все четыре края, берёт ближайший.
    /// </summary>
    private void SnapToEdge()
    {
        var screen = System.Windows.SystemParameters.WorkArea;

        var distLeft   = Math.Abs(Left - screen.Left);
        var distRight  = Math.Abs((Left + Width) - screen.Right);
        var distTop    = Math.Abs(Top - screen.Top);
        var distBottom = Math.Abs((Top + Height) - screen.Bottom);

        var minHoriz = Math.Min(distLeft, distRight);
        var minVert  = Math.Min(distTop, distBottom);

        if (!(minHoriz < SnapDistance) && !(minVert < SnapDistance)) return;
        double newLeft;
        double newTop;

        if (minHoriz <= minVert)
        {
            // Прилипаем к левой или правой грани
            if (distLeft < distRight)
            {
                newLeft = screen.Left;
                // Вертикально — растягиваем на весь экран
                // вертикальная полоска
            }
            else
            {
                newLeft = screen.Right - CompactHeight;
            }

            newTop = screen.Top;
            Height = screen.Height;
            Width  = CompactHeight; // вертикальная полоска
        }
        else
        {
            // Прилипаем к верхней или нижней грани
            if (distTop < distBottom)
                newTop = screen.Top;
            else
                newTop = screen.Bottom - CompactHeight;

            newLeft = Left; // горизонтальная позиция не меняется
            Height  = CompactHeight;
            Width   = CompactWidth;
        }

        Left = newLeft;
        Top  = newTop;
    }

    // Кнопка «Развернуть» в компактном режиме
    private void CompactExpand_Click(object sender, RoutedEventArgs e) => ExitCompactMode();
    
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

    public void Log(string message) => OutputBox.Text += message + Environment.NewLine;

    private void OutputBox_OnGotFocus(object sender, RoutedEventArgs e) => InputBox.Focus();

    private void CreateSnippet_OnClick(object sender, RoutedEventArgs e)
    {
        var window = new SnippetCreationWind(
            "Создание сниппета",
            string.Empty
        )
        {
            Owner = GetWindow(this)
        };

        if (window.ShowDialog() != true) return;
        
        var nb = new NumButton();
        if (window.ResultText1 != null) nb.SetText(window.ResultText1);
        
        nb.ActivationKey = window.SelectedKey;

        nb.KeyHandler.Text = nb.ActivationKey.ToString();

        Snippets.Children.Add(nb);
    }
    
    private void BringToFront()
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        Topmost = true;
        Activate();
        Focus();

        Dispatcher.BeginInvoke(new Action(() =>
        {
            Topmost = false;
        }));
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
    
    public async Task SaveSnippetsAsync()
    {
        var models = Snippets.Children
            .OfType<NumButton>()
            .Select(b => new SnippetModel
            {
                Text = b.FullText,
                ActivationKey = b.ActivationKey
            })
            .ToList();

        var text = Conseq.Conqsequalize(models, ConseqFormat.Readable);
        
        await File.WriteAllTextAsync(
            Path.Combine(Environment.CurrentDirectory, SnippetsFile),
            text);
    }

    private async Task LoadSnippets()
    {
        var path = Path.Combine(Environment.CurrentDirectory, SnippetsFile);

        if (!File.Exists(path))
            return;

        var text = await File.ReadAllTextAsync(path);

        try
        {
            var models = Conseq.Deconqsequalize<List<SnippetModel>>(text);
            
            foreach (var model in models)
            {
                var nb = new NumButton();

                nb.SetText(model.Text);
                nb.ActivationKey = model.ActivationKey;
                nb.KeyHandler.Text = model.ActivationKey.ToString();

                Snippets.Children.Add(nb);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
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
}