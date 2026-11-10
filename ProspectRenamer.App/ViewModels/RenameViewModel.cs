using ProspectRenamer.Core.Models;
using ProspectRenamer.Core.Services;

namespace ProspectRenamer.App.ViewModels;

public sealed class RenameViewModel : ObservableObject
{
    private readonly ProspectService _prospectService;
    private string _newName;
    private string? _validationMessage;
    private bool _canRename;

    public RenameViewModel(ProspectEntry prospect, ProspectService prospectService)
    {
        Prospect = prospect;
        _prospectService = prospectService;
        _newName = prospect.ProspectName;
        Validate();
    }

    public ProspectEntry Prospect { get; }
    public string CurrentName => Prospect.ProspectName;

    public string NewName
    {
        get => _newName;
        set
        {
            if (SetProperty(ref _newName, value))
            {
                Validate();
            }
        }
    }

    public string? ValidationMessage
    {
        get => _validationMessage;
        private set => SetProperty(ref _validationMessage, value);
    }

    public bool CanRename
    {
        get => _canRename;
        private set => SetProperty(ref _canRename, value);
    }

    public string NormalizedName => _prospectService.ValidateNewName(Prospect, NewName).NormalizedName;

    private void Validate()
    {
        var result = _prospectService.ValidateNewName(Prospect, NewName);
        ValidationMessage = result.Error;
        CanRename = result.IsValid;
    }
}
