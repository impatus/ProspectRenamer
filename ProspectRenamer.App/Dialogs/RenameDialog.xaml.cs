using System.Windows;
using ProspectRenamer.App.ViewModels;
using ProspectRenamer.Core.Models;

namespace ProspectRenamer.App.Dialogs;

public partial class RenameDialog : Window
{
    public ProspectOperationMode SelectedOperation { get; private set; } = ProspectOperationMode.Rename;

    public RenameDialog(RenameViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        SelectedOperation = ProspectOperationMode.Rename;
        DialogResult = true;
    }

    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        SelectedOperation = ProspectOperationMode.Duplicate;
        DialogResult = true;
    }
}
