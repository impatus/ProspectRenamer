using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using ProspectRenamer.App.Dialogs;
using ProspectRenamer.App.ViewModels;

namespace ProspectRenamer.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private readonly DispatcherTimer _icarusStatusTimer = new()
    {
        Interval = TimeSpan.FromSeconds(2)
    };

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        _icarusStatusTimer.Tick += (_, _) => _viewModel.UpdateIcarusStatus();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
        _viewModel.UpdateIcarusStatus();
        _icarusStatusTimer.Start();
    }

    private void Window_Closed(object? sender, EventArgs e) => _icarusStatusTimer.Stop();

    private async void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select a Prospects folder or parent directory",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (!await _viewModel.UseCustomFolderAsync(dialog.FolderName))
        {
            MessageBox.Show(
                this,
                "No Prospects folder was found at or immediately beneath the selected directory.",
                "Prospect folder not found",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private async void RenameProspect_Click(object sender, RoutedEventArgs e) => await BeginRenameAsync();

    private async Task BeginRenameAsync()
    {
        if (!_viewModel.CanRenameSelected)
        {
            return;
        }

        if (_viewModel.ShowRenameWarning)
        {
            var warning = new RenameWarningDialog { Owner = this };
            if (warning.ShowDialog() != true)
            {
                return;
            }

            if (warning.DontShowAgain)
            {
                _viewModel.SuppressRenameWarning();
            }
        }

        var renameViewModel = _viewModel.CreateRenameViewModel();
        var renameDialog = new RenameDialog(renameViewModel) { Owner = this };
        if (renameDialog.ShowDialog() != true)
        {
            return;
        }

        var operation = renameDialog.SelectedOperation;
        var result = await _viewModel.SaveSelectedAsAsync(renameViewModel.NormalizedName, operation);
        if (!result.IsSuccess)
        {
            var action = operation == ProspectRenamer.Core.Models.ProspectOperationMode.Rename
                ? "renamed"
                : "duplicated";
            var backups = new List<string>();
            if (!string.IsNullOrWhiteSpace(result.BackupPath))
            {
                backups.Add($"Prospect backup: {result.BackupPath}");
            }

            if (!string.IsNullOrWhiteSpace(result.LoadoutBackupPath))
            {
                backups.Add($"Loadout backup: {result.LoadoutBackupPath}");
            }

            if (!string.IsNullOrWhiteSpace(result.GameBackupArchivePath))
            {
                backups.Add($"Game backup archive: {result.GameBackupArchivePath}");
            }

            var backupDetails = backups.Count == 0
                ? string.Empty
                : $"\n\nAutomatic backups:\n{string.Join("\n", backups)}";
            MessageBox.Show(
                this,
                $"The prospect was not {action}.\n\n{result.Error}{backupDetails}",
                $"{char.ToUpperInvariant(action[0])}{action[1..]} failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        else
        {
            var completion = new CompletionDialog(
                result.OldName,
                result.NewName,
                operation == ProspectRenamer.Core.Models.ProspectOperationMode.Rename)
            {
                Owner = this
            };
            completion.ShowDialog();
        }
    }

    private void OpenBackups_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_viewModel.BackupRoot);
            Process.Start(new ProcessStartInfo(_viewModel.BackupRoot) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Could not open backup folder", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog(_viewModel.GetSettingsCopy()) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            try
            {
                _viewModel.SaveSettings(dialog.Settings);
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "Settings could not be saved", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
