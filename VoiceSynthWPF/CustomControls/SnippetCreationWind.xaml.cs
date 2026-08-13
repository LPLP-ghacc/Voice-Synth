using System.Windows;
using System.Windows.Input;

namespace VoiceSynthWPF.CustomControls;

public partial class SnippetCreationWind : Window
{
    public string? ResultText1 { get; private set; }
    public Key SelectedKey { get; private set; }

    public SnippetCreationWind(string title, string description)
    {
        InitializeComponent();

        TitleTextBlock.Text = title;
        DescTextField.Text = description;
    }

    private void TopBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        ResultText1 = Input.Text;

        DialogResult = true;
        Close();
    }

    private void Input2_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        SelectedKey = e.Key;
        Input2.Text = SelectedKey.ToString();
    }
    
    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}