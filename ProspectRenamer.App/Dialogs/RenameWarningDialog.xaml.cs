using System.Windows;

namespace ProspectRenamer.App.Dialogs;

public partial class RenameWarningDialog : Window
{
    public RenameWarningDialog() => InitializeComponent();
    public bool DontShowAgain => DontShowCheckBox.IsChecked == true;
    private void Continue_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
