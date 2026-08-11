using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace VoiceSynthWPF;

/// <summary>
/// Управляет визуальными индикаторами голосовой активности:
///   • Нормальный режим  — TextBlock "Voice Synth": белый → синий
///   • Компактный режим — прямоугольник под полем ввода: серый → синий
/// </summary>
public static class VoiceActivityIndicator
{
    // Цвета
    private static readonly Color IdleColor   = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF); // белый
    private static readonly Color ActiveColor = Color.FromArgb(0xFF, 0x29, 0x9D, 0xFF); // синий

    private static readonly Color BarIdleColor   = Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF); // тонкая серая полоска
    private static readonly Color BarActiveColor = Color.FromArgb(0xFF, 0x29, 0x9D, 0xFF); // синяя

    // Длительность fade-in и fade-out (мс)
    private const int FadeInMs  = 300;
    private const int FadeOutMs = 600;

    // Ссылки на UI элементы — заполняются при инициализации
    private static System.Windows.Controls.TextBlock? _titleText;
    private static Rectangle? _compactBar;

    // ─── Инициализация ───────────────────────────────────────────────────────

    /// <summary>
    /// Вызвать один раз после загрузки UI.
    /// </summary>
    public static void Init(
        System.Windows.Controls.TextBlock titleText,
        Rectangle compactBar)
    {
        _titleText  = titleText;
        _compactBar = compactBar;

        // Устанавливаем начальные SolidColorBrush чтобы анимировать их
        _titleText.Foreground  = new SolidColorBrush(IdleColor);
        _compactBar.Fill       = new SolidColorBrush(BarIdleColor);
    }

    // ─── Публичное API ───────────────────────────────────────────────────────

    /// <summary>Запустить анимацию — начало воспроизведения.</summary>
    public static void OnSpeechStart()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            AnimateBrush(_titleText?.Foreground as SolidColorBrush,
                IdleColor, ActiveColor, FadeInMs);

            AnimateBrush(_compactBar?.Fill as SolidColorBrush,
                BarIdleColor, BarActiveColor, FadeInMs);

            // Масштаб полоски — расширяется в высоту для эффекта «пульса»
            if (_compactBar != null)
                AnimateBarHeight(_compactBar, 2, 4, FadeInMs);
        });
    }

    /// <summary>Остановить анимацию — воспроизведение закончено.</summary>
    public static void OnSpeechStop()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            AnimateBrush(_titleText?.Foreground as SolidColorBrush,
                ActiveColor, IdleColor, FadeOutMs);

            AnimateBrush(_compactBar?.Fill as SolidColorBrush,
                BarActiveColor, BarIdleColor, FadeOutMs);

            if (_compactBar != null)
                AnimateBarHeight(_compactBar, 4, 2, FadeOutMs);
        });
    }

    // ─── Внутренние методы ───────────────────────────────────────────────────

    private static void AnimateBrush(
        SolidColorBrush? brush, Color from, Color to, int durationMs)
    {
        if (brush == null) return;

        var anim = new ColorAnimation
        {
            From           = from,
            To             = to,
            Duration       = new Duration(TimeSpan.FromMilliseconds(durationMs)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };

        brush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
    }

    private static void AnimateBarHeight(Rectangle bar, double from, double to, int durationMs)
    {
        var anim = new DoubleAnimation
        {
            From           = from,
            To             = to,
            Duration       = new Duration(TimeSpan.FromMilliseconds(durationMs)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        bar.BeginAnimation(Rectangle.HeightProperty, anim);
    }
}
