using System.IO;
using SystemSpeech = System.Speech.Synthesis;
using WinRtSpeech  = Windows.Media.SpeechSynthesis;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Windows.Storage.Streams;

namespace VoiceSynthWPF;

// ─── Абстракция провайдера TTS ────────────────────────────────────────────────

public enum TtsProviderType
{
    Sapi,   // System.Speech — встроенный SAPI
    WinRt,  // Windows.Media.SpeechSynthesis — Neural голоса от Microsoft
    Piper   // Piper TTS — локальная нейросеть, лучшее качество
}

public interface ITtsProvider : IDisposable
{
    TtsProviderType Type { get; }

    /// <summary>Список доступных голосов.</summary>
    IReadOnlyList<string> GetVoices();

    /// <summary>Синтезировать text и вернуть PCM/WAV поток.</summary>
    Task<Stream> SynthToStreamAsync(string text);
}

// ─── SAPI провайдер ───────────────────────────────────────────────────────────

public sealed class SapiTtsProvider : ITtsProvider
{
    private readonly SystemSpeech.SpeechSynthesizer _synth;

    public TtsProviderType Type => TtsProviderType.Sapi;

    public SapiTtsProvider(string voiceName, int rate, int volume)
    {
#pragma warning disable CA1416
        _synth = new SystemSpeech.SpeechSynthesizer();
        _synth.Rate   = rate;
        _synth.Volume = volume;

        var voices   = _synth.GetInstalledVoices().ToList();
        var selected = voices.FirstOrDefault(v => v.VoiceInfo.Name == voiceName)
                    ?? voices.FirstOrDefault();

        if (selected != null)
            _synth.SelectVoice(selected.VoiceInfo.Name);
#pragma warning restore CA1416
    }

    public IReadOnlyList<string> GetVoices()
    {
#pragma warning disable CA1416
        return _synth.GetInstalledVoices()
                     .Select(v => v.VoiceInfo.Name)
                     .ToList();
#pragma warning restore CA1416
    }

    public Task<Stream> SynthToStreamAsync(string text)
    {
#pragma warning disable CA1416
        var ms = new MemoryStream();
        _synth.SetOutputToWaveStream(ms);
        _synth.Speak(text);
        ms.Position = 0;
#pragma warning restore CA1416
        return Task.FromResult<Stream>(ms);
    }

    public void Dispose() => _synth.Dispose();
}

// ─── WinRT провайдер ──────────────────────────────────────────────────────────

public sealed class WinRtTtsProvider : ITtsProvider
{
    private readonly WinRtSpeech.SpeechSynthesizer _synth;

    public TtsProviderType Type => TtsProviderType.WinRt;

    public WinRtTtsProvider(string voiceName, double speakingRate, double audioVolume)
    {
        _synth = new WinRtSpeech.SpeechSynthesizer();

        var voice = WinRtSpeech.SpeechSynthesizer.AllVoices
            .FirstOrDefault(v => v.DisplayName == voiceName)
            ?? WinRtSpeech.SpeechSynthesizer.DefaultVoice;

        _synth.Voice = voice;
        _synth.Options.SpeakingRate = speakingRate;
        _synth.Options.AudioVolume  = audioVolume;
    }

    public IReadOnlyList<string> GetVoices() =>
        WinRtSpeech.SpeechSynthesizer.AllVoices
            .Select(v => v.DisplayName)
            .ToList();

    public async Task<Stream> SynthToStreamAsync(string text)
    {
        var stream = await _synth.SynthesizeTextToStreamAsync(text);

        // IRandomAccessStream → MemoryStream
        var ms = new MemoryStream();
        var reader = new DataReader(stream);
        await reader.LoadAsync((uint)stream.Size);
        var bytes = new byte[stream.Size];
        reader.ReadBytes(bytes);
        await ms.WriteAsync(bytes);
        ms.Position = 0;
        return ms;
    }

    public void Dispose() => _synth.Dispose();

    /// <summary>
    /// Маппинг SAPI Rate (-10..+10) → WinRT SpeakingRate (0.5..3.0).
    /// </summary>
    public static double MapRate(int sapiRate)
    {
        // -10 → 0.5,  0 → 1.0,  +10 → 3.0
        return sapiRate >= 0
            ? 1.0 + sapiRate * 0.2
            : 1.0 + sapiRate * 0.05;
    }
}

// ─── Фабрика ─────────────────────────────────────────────────────────────────

public static class TtsProviderFactory
{
    public static ITtsProvider Create(Settings settings)
    {
        return settings.TtsProvider switch
        {
            TtsProviderType.WinRt => new WinRtTtsProvider(
                settings.ReaderName,
                WinRtTtsProvider.MapRate(settings.VoiceSpeed),
                settings.VoiceVolume / 100.0),

            TtsProviderType.Piper => new PiperTtsProvider(
                settings.ReaderName,
                settings.VoiceSpeed),

            _ => new SapiTtsProvider(
                settings.ReaderName,
                settings.VoiceSpeed,
                settings.VoiceVolume)
        };
    }
}

// ─── Piper провайдер ──────────────────────────────────────────────────────────

/// <summary>
/// Запускает piper.exe как дочерний процесс.
/// Текст подаётся на stdin, WAV читается из stdout.
/// Модели (.onnx) ищутся в папке piper\ рядом с exe.
/// </summary>
public sealed class PiperTtsProvider : ITtsProvider
{
    public TtsProviderType Type => TtsProviderType.Piper;

    // Путь к папке piper\ — рядом с исполняемым файлом приложения
    private static readonly string PiperDir =
        Path.Combine(AppContext.BaseDirectory, "piper");

    private static readonly string PiperExe =
        Path.Combine(PiperDir, "piper.exe");

    private readonly string _modelPath;
    private readonly double _lengthScale; // >1 = медленнее, <1 = быстрее

    /// <param name="modelName">Имя файла модели без расширения, напр. "ru_RU-irina-medium"</param>
    /// <param name="sapiRate">Скорость -10..+10 как в SAPI</param>
    public PiperTtsProvider(string modelName, int sapiRate)
    {
        _modelPath   = Path.Combine(PiperDir, modelName + ".onnx");
        _lengthScale = MapRate(sapiRate);
    }

    /// <summary>Возвращает список .onnx моделей найденных в папке piper\.</summary>
    public IReadOnlyList<string> GetVoices()
    {
        if (!Directory.Exists(PiperDir))
            return ["(папка piper не найдена)"];

        var models = Directory.GetFiles(PiperDir, "*.onnx")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .ToList();

        return models.Count > 0 ? models : ["(нет .onnx моделей в папке piper)"];
    }

    public async Task<Stream> SynthToStreamAsync(string text)
    {
        if (!File.Exists(PiperExe))
            throw new FileNotFoundException($"piper.exe не найден: {PiperExe}");

        if (!File.Exists(_modelPath))
            throw new FileNotFoundException($"Модель не найдена: {_modelPath}");

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName               = PiperExe,
            Arguments              = $"--model \"{_modelPath}\" --length_scale {_lengthScale.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} --output-raw",
            WorkingDirectory       = PiperDir,
            UseShellExecute        = false,
            RedirectStandardInput  = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = false,
            CreateNoWindow         = true,
            StandardInputEncoding  = System.Text.Encoding.UTF8,
        };

        using var process = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("Не удалось запустить piper.exe");

        await process.StandardInput.WriteAsync(text);
        process.StandardInput.Close();

        // Читаем raw PCM bytes пока процесс не завершится
        var rawBytes = Array.Empty<byte>();
        using (var rawMs = new MemoryStream())
        {
            await process.StandardOutput.BaseStream.CopyToAsync(rawMs);
            rawBytes = rawMs.ToArray();
        }

        await process.WaitForExitAsync();

        // Оборачиваем raw PCM (16-bit, 22050 Hz, моно) в WAV-контейнер
        // WaveFileWriter закрывает переданный stream — поэтому пишем во временный,
        // а потом копируем байты в финальный незакрытый MemoryStream
        var waveFormat = new WaveFormat(22050, 16, 1);
        byte[] wavBytes;
        using (var tempMs = new MemoryStream())
        {
            // leaveOpen: false — WaveFileWriter закроет tempMs, но нам уже не важно
            using (var writer = new WaveFileWriter(tempMs, waveFormat))
            {
                writer.Write(rawBytes, 0, rawBytes.Length);
            } // здесь tempMs закрывается, но ToArray() работает даже на закрытом MemoryStream
            wavBytes = tempMs.ToArray();
        }

        // Возвращаем свежий незакрытый поток
        return new MemoryStream(wavBytes);
    }

    public void Dispose() { /* нет долгоживущих ресурсов */ }

    /// <summary>SAPI Rate (-10..+10) → Piper length_scale (0.5..2.0).<br/>
    /// length_scale: 1.0 = нормально, >1 = медленнее, &lt;1 = быстрее.</summary>
    public static double MapRate(int sapiRate)
    {
        // -10 → 0.5 (быстро),  0 → 1.0,  +10 → 2.0 (медленно)
        return sapiRate >= 0
            ? 1.0 + sapiRate * 0.1
            : 1.0 + sapiRate * 0.05;
    }

    /// <summary>Проверяет что piper.exe доступен.</summary>
    public static bool IsAvailable() => File.Exists(PiperExe);
}
