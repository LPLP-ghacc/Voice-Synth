using System.IO;
using System.Text.Json;

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