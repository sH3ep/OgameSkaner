using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OgameSkaner.Core.Licensing;

namespace OgameSkaner.App.ViewModels;

/// <summary>
/// The license window: the terms (accepted once per version), activation with a license key through the server, and
/// importing a token by hand (before the server exists, or offline).
/// </summary>
public sealed partial class LicenseViewModel : ObservableObject
{
    private readonly ILicenseService _license;

    public LicenseViewModel(ILicenseService license)
    {
        _license = license;
        _termsAccepted = license.TermsAccepted;
        Refresh();
    }

    public string Terms => LicenseTerms.Text;

    public string MachineId => _license.MachineId;

    public bool CanActivateOnline => _license.CanActivateOnline;

    /// <summary>The terms are accepted here and then stay accepted; unticking is only possible before that.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanContinue), nameof(TermsOpen))]
    [NotifyCanExecuteChangedFor(nameof(ActivateCommand), nameof(ImportCommand))]
    private bool _termsAccepted;

    /// <summary>The checkbox is locked once ticked: the acceptance is stored.</summary>
    public bool TermsOpen => !TermsAccepted;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ActivateCommand))]
    private string _licenseKey = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private string _token = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanContinue))]
    private LicenseStatus _status = new(LicenseState.Missing);

    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ActivateCommand))]
    private bool _isBusy;

    /// <summary>Whether the app can be used: terms accepted and a valid license.</summary>
    public bool CanContinue => TermsAccepted && Status.IsValid;

    /// <summary>What the licensed accounts are, for the status line.</summary>
    public string Accounts => Status.Payload switch
    {
        null => "",
        { MaxAccounts: 0 } => "Any game account",
        var p => $"Game accounts: {p.Accounts.Count} of {p.MaxAccounts} bound"
                 + (p.Accounts.Count > 0 ? " (" + string.Join(", ", p.Accounts.Select(a => a.Universe)) + ")" : "")
    };

    partial void OnTermsAcceptedChanged(bool value)
    {
        if (value && !_license.TermsAccepted)
            _license.AcceptTerms();
    }

    partial void OnStatusChanged(LicenseStatus value) => OnPropertyChanged(nameof(Accounts));

    private bool CanActivate() => TermsAccepted && CanActivateOnline && !IsBusy && !string.IsNullOrWhiteSpace(LicenseKey);

    [RelayCommand(CanExecute = nameof(CanActivate))]
    private async Task ActivateAsync()
    {
        IsBusy = true;
        try
        {
            Status = await _license.ActivateAsync(LicenseKey);
            Message = "Activated.";
        }
        catch (LicenseException ex)
        {
            Message = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanImport() => TermsAccepted && !string.IsNullOrWhiteSpace(Token);

    [RelayCommand(CanExecute = nameof(CanImport))]
    private void Import()
    {
        var status = _license.Import(Token);
        Message = status.IsValid ? "License token imported." : $"Not imported: {status.Text}.";
        if (status.IsValid)
            Token = string.Empty;
        Refresh();
    }

    [RelayCommand]
    private void CopyMachineId()
    {
        Clipboard.SetText(MachineId);
        Message = "Machine id copied.";
    }

    private void Refresh() => Status = _license.Status;
}
