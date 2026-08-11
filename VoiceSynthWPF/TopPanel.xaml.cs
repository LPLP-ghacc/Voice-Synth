using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace VoiceSynthWPF;

public partial class TopPanel
{
    public TopPanel()
    {
        InitializeComponent();
        
        TopBorder.MouseDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Left)
                MainWindow.Instance!.DragMove();
        };
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => MainWindow.Instance!.WindowState = WindowState.Minimized;

    private void CompactMode_Click(object sender, RoutedEventArgs e) => MainWindow.Instance!.ToggleCompactMode();

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        _ = MainWindow.Instance!.OpenSettingsAsync();
    }

    private async void Close_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await MainWindow.Instance!.SaveSnippetsAsync();
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            MainWindow.Instance!.Log(ex.Message);
        }
    }
}