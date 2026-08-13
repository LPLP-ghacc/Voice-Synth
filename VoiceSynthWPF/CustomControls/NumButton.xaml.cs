using System.Windows;
using System.Windows.Input;

namespace VoiceSynthWPF.CustomControls;

public partial class NumButton
{
    public Key ActivationKey;
    public string FullText;
    
    public NumButton()
    {
        FullText = string.Empty;
        InitializeComponent();
        ClampPreviewText();
    }

    public void SetText(string text)
    {
        FullText = text;
        PreviewText.Text = text;
        ClampPreviewText();
    }

    private void ClampPreviewText()
    {
        const int maxValue = 8;
        if (PreviewText.Text.Length > maxValue)
        {
            PreviewText.Text = PreviewText.Text[..maxValue] + "...";
        }
    }
    
    private void Settings_OnClick(object sender, RoutedEventArgs e)
    {
        var window = new SnippetCreationWind(
            "Настройка кнопки",
            FullText
        )
        {
            Owner = Window.GetWindow(this)
        };

        if (window.ShowDialog() != true) return;
        if (window.ResultText1 != null) SetText(window.ResultText1);
        ActivationKey = window.SelectedKey;
        KeyHandler.Text = ActivationKey.ToString();
    }

    private void Delete_OnClick(object sender, RoutedEventArgs e)
    {
        MainWindow.Instance!.Snippets.Children.Remove(this);
    }
}