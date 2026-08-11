using System.Windows;

namespace VoiceSynthWPF;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        LocalizationManager.InitFromSystem();
    }
}
