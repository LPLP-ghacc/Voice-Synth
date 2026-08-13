using System.IO;
using System.Text.Json;
using System.Windows.Input;

namespace VoiceSynthWPF;

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
            Enum.TryParse(key, out result);
#pragma warning restore CA1806
        }
        catch (Exception exception)
        {
            MainWindow.Instance!.Log(exception.ToString());
        }

        return result;
    }
}