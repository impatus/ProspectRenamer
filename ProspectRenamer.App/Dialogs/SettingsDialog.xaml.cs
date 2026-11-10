using System.Windows;
using Microsoft.Win32;
using ProspectRenamer.Core.Services;

namespace ProspectRenamer.App.Dialogs;

public partial class SettingsDialog : Window
{
    public SettingsDialog(AppSettings settings)
    {
        InitializeComponent();
        Settings = settings;
        DataContext = Settings;
    }

    public AppSettings Settings { get; }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select backup location", Multiselect = false };
        if (dialog.ShowDialog(this) == true)
        {
            Settings.BackupFolder = dialog.FolderName;
            DataContext = null;
            DataContext = Settings;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
