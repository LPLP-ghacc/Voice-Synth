using System.IO;
using System.Speech.Synthesis;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using ConseqConcatenation;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using static System.Enum;

namespace VoiceSynthWPF;

// ─── WindowState persistence ─────────────────────────────────────────────────

public class WindowLayout
{
    public double Left      { get; set; } = 100;
    public double Top       { get; set; } = 100;
    public double Width     { get; set; } = 800;
    public double Height    { get; set; } = 450;
    public bool   IsCompact { get; set; } = false;
    // Геометрия нормального режима (сохраняется даже когда активен компактный)
    public double NormalLeft   { get; set; } = 100;
    public double NormalTop    { get; set; } = 100;
    public double NormalWidth  { get; set; } = 800;
    public double NormalHeight { get; set; } = 450;

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
        catch { /* игнорируем — вернём дефолт */ }
        return new WindowLayout();
    }

    public void Save()
    {
        try { File.WriteAllText(Path, JsonSerializer.Serialize(this)); }
        catch { /* не критично */ }
    }
}

// ─── Settings ────────────────────────────────────────────────────────────────

public class Settings
{
    public string VoiceInput { get; }
    public int VoiceSpeed { get; }
    public int VoiceVolume { get; }
    public int StdDelay { get; }
    public string ReaderName { get; }
    public Key HotKeyBringToFront { get; }

    public Settings(string voiceInput, int voiceSpeed, int voiceVolume, int stdDelay, string readerName, Key hotKeyBringToFront) 
    {
        VoiceInput = voiceInput;
        VoiceSpeed = voiceSpeed;
        VoiceVolume = voiceVolume;
        StdDelay = stdDelay;
        ReaderName = readerName;
        HotKeyBringToFront = hotKeyBringToFront;
    }

    public static Settings Default { get; } = new("CABLE Input", 0, 100, 10, "Microsoft Irina", Key.F12);
    
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
    private SpeechSynthesizer? _synth;
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
    private readonly List<string> _inputHistory = new();
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

        double distLeft   = Math.Abs(Left - screen.Left);
        double distRight  = Math.Abs((Left + Width) - screen.Right);
        double distTop    = Math.Abs(Top - screen.Top);
        double distBottom = Math.Abs((Top + Height) - screen.Bottom);

        double minHoriz = Math.Min(distLeft, distRight);
        double minVert  = Math.Min(distTop, distBottom);

        if (minHoriz < SnapDistance || minVert < SnapDistance)
        {
            double newLeft = Left;
            double newTop  = Top;

            if (minHoriz <= minVert)
            {
                // Прилипаем к левой или правой грани
                if (distLeft < distRight)
                {
                    newLeft = screen.Left;
                    // Вертикально — растягиваем на весь экран
                    newTop = screen.Top;
                    Height = screen.Height;
                    Width  = CompactHeight; // вертикальная полоска
                }
                else
                {
                    newLeft = screen.Right - CompactHeight;
                    newTop  = screen.Top;
                    Height  = screen.Height;
                    Width   = CompactHeight;
                }
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
    }

    // Кнопка «Развернуть» в компактном режиме
    private void CompactExpand_Click(object sender, RoutedEventArgs e) => ExitCompactMode();
    
    private async Task InitAsync()
    {
        _settings = await Settings.Load(Environment.CurrentDirectory, FileName);
        await ApplySettingsAsync(_settings);
        await LoadSnippets();
    }

    /// <summary>
    /// Применяет настройки: выбирает аудиоустройство и синтезатор.
    /// Возвращает false если аудиоустройство не найдено.
    /// </summary>
    private async Task<bool> ApplySettingsAsync(Settings settings)
    {
        // Dispose old synth if exists
        _synth?.Dispose();
        _synth = null;

        var enumerator = new MMDeviceEnumerator();
        var allDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();

        // Ищем точное совпадение, затем частичное
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

#pragma warning disable CA1416
        _synth = new SpeechSynthesizer();
        _synth.Rate = settings.VoiceSpeed;
        _synth.Volume = settings.VoiceVolume;

        var voices = _synth.GetInstalledVoices().ToList();
        Log($"Найдено голосов: {voices.Count}");
        foreach (var item in voices)
            Log($"  • {item.VoiceInfo.Name}");

        var selectedVoice = voices.FirstOrDefault(v => v.VoiceInfo.Name == settings.ReaderName);
        if (selectedVoice != null)
        {
            _synth.SelectVoice(settings.ReaderName);
            Log($"Выбран голос: {settings.ReaderName}");
        }
        else if (voices.Count > 0)
        {
            var fallback = voices[0].VoiceInfo.Name;
            _synth.SelectVoice(fallback);
            Log($"[ПРЕДУПРЕЖДЕНИЕ] Голос '{settings.ReaderName}' не найден, используется: {fallback}");
        }
        else
        {
            Log("[ОШИБКА] Не найдено ни одного установленного голоса TTS.");
            _synth.Dispose();
            _synth = null;
            return false;
        }
#pragma warning restore CA1416

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
        if (_synth == null)
        {
            Log("[ОШИБКА] Синтезатор речи не инициализирован. Проверьте настройки.");
            return;
        }

        if (_cableDevice == null)
        {
            Log("[ОШИБКА] Аудиоустройство не найдено. Откройте Настройки и выберите устройство.");
            return;
        }

        var tcs = new TaskCompletionSource();

        Log($"=> {text}");

        using var ms = new MemoryStream();

#pragma warning disable CA1416
        _synth.SetOutputToWaveStream(ms);
        await Task.Run(() => _synth.Speak(text));
#pragma warning restore CA1416

        ms.Position = 0;

        var reader = new WaveFileReader(ms);
        var wasapiOut = new WasapiOut(_cableDevice, AudioClientShareMode.Shared, false, 100);

        wasapiOut.Init(reader);

        wasapiOut.PlaybackStopped += (_, _) =>
        {
            wasapiOut.Dispose();
            reader.Dispose();
            tcs.TrySetResult();
        };

        wasapiOut.Play();

        await tcs.Task;
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