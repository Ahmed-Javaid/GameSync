using System.ComponentModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameSync.Host;

namespace GameSync.UI.ViewModels;

/// <summary>
/// Connect the cloud, after first run skipped it (design system → ConnectCloudDialog): from Home's Connect the cloud,
/// the same two ways as first run's step. What's chosen is connected at once, and what waited on this PC goes up in the
/// background; Done closes it, or Not now before.
/// </summary>
public sealed partial class ConnectCloudViewModel : ObservableObject
{
    private readonly Func<string, CancellationToken, Task> _connect;

    [ObservableProperty]
    private bool _isConnecting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    /// <param name="connect">Makes the choice games.json's cloud and syncs: "drive" or a folder.</param>
    /// <param name="close">Closes the dialog.</param>
    public ConnectCloudViewModel(CloudActions actions, Func<string, CancellationToken, Task> connect, Action close)
    {
        _connect = connect;
        Cloud = new CloudSetupViewModel(actions with { SignedIn = false });
        Cloud.PropertyChanged += CloudChanged;
        CloseCommand = new RelayCommand(close);
    }

    public CloudSetupViewModel Cloud { get; }

    public bool HasError => Error is not null;

    public string CloseLabel => Cloud.IsConnected ? "Done" : "Not now";

    public bool CloseIsMain => Cloud.IsConnected;

    public ICommand CloseCommand { get; }

    private async void CloudChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CloudSetupViewModel.Choice))
        {
            return;
        }

        OnPropertyChanged(nameof(CloseLabel));
        OnPropertyChanged(nameof(CloseIsMain));
        if (!Cloud.IsConnected)
        {
            return;
        }

        IsConnecting = true;
        Error = null;
        try
        {
            await _connect(Cloud.Remote, CancellationToken.None);
        }
        catch (Exception ex) when (ex is UsageException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Error = $"GameSync couldn't connect it: {ex.Message}";
        }
        finally
        {
            IsConnecting = false;
        }
    }
}
