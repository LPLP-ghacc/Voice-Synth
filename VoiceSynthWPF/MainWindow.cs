using System.IO;
using System.Windows;
using ConseqConcatenation;
using VoiceSynthWPF.CustomControls;

namespace VoiceSynthWPF;

public partial class MainWindow
{
    // История ввода
    private readonly List<string> _inputHistory = [];
    private int _historyIndex = -1; // -1 = не в режиме истории
    
    // Компактный режим
    private bool _isCompact;
    private double _normalLeft, _normalTop, _normalWidth, _normalHeight;

    private const double CompactHeight = 36;
    private const double CompactWidth  = 420;
    private const int    SnapDistance  = 20; // px до края для "прилипания"
    
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
    
    private WindowLayout InitGeometry()
    {
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

        return layout;
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
}