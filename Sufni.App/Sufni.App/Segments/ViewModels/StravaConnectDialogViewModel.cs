using Sufni.App.Shared.Models;
using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Sufni.App.Infrastructure;
using Sufni.App.Segments.Models;
using Sufni.App.Segments.Coordinators;
namespace Sufni.App.Segments.ViewModels;

/// <summary>
/// Content of the dialog that connects the user's own Strava API app. The user
/// opens the authorization URL in the browser and pastes the redirect URL
/// back. The dialog completes on cancel and after a successful connect.
/// </summary>
public sealed partial class StravaConnectDialogViewModel : ObservableObject, IContentDialogCompletionSource, IDisposable
{
    private readonly ISegmentCoordinator segmentCoordinator;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AuthorizationUrl))]
    [NotifyPropertyChangedFor(nameof(HasClientCredentials))]
    [NotifyPropertyChangedFor(nameof(CanOpenAuthorizationUrl))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    public partial string? ClientId { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AuthorizationUrl))]
    [NotifyPropertyChangedFor(nameof(HasClientCredentials))]
    [NotifyPropertyChangedFor(nameof(CanOpenAuthorizationUrl))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    public partial string? ClientSecret { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    public partial string? RedirectUrl { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial FailurePresentation? Error { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpenAuthorizationUrl))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    public partial bool IsConnecting { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    public partial bool HasOpenedAuthorizationUrl { get; private set; }

    public StravaConnectDialogViewModel(ISegmentCoordinator segmentCoordinator, StravaConnectionSnapshot connection)
    {
        this.segmentCoordinator = segmentCoordinator;
        ClientId = connection.ClientId;
        ClientSecret = connection.ClientSecret;
    }

    public string AuthorizationUrl => HasClientCredentials
        ? segmentCoordinator.CreateStravaAuthorizationUrl(ClientId!.Trim())
        : string.Empty;

    public bool HasClientCredentials =>
        !string.IsNullOrWhiteSpace(ClientId) &&
        !string.IsNullOrWhiteSpace(ClientSecret);

    public bool CanOpenAuthorizationUrl => HasClientCredentials && !IsConnecting;
    public bool HasError => Error is not null;

    public event EventHandler? Completed;

    public void MarkAuthorizationUrlOpened()
    {
        if (HasClientCredentials)
        {
            HasOpenedAuthorizationUrl = true;
        }
    }

    partial void OnClientIdChanged(string? value) => HasOpenedAuthorizationUrl = false;

    partial void OnClientSecretChanged(string? value) => HasOpenedAuthorizationUrl = false;

    [RelayCommand]
    private void Cancel()
    {
        ConnectCommand.Cancel();
        Completed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => ConnectCommand.Cancel();

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task Connect(CancellationToken cancellationToken)
    {
        Error = null;
        IsConnecting = true;
        try
        {
            var result = await segmentCoordinator.ConnectStravaAsync(
                new StravaConnectRequest(ClientId, ClientSecret, RedirectUrl),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            Error = result switch
            {
                StravaConnectResult.ValidationFailed validation => new FailurePresentation(validation.Message),
                StravaConnectResult.MissingRequiredScope => new FailurePresentation("Strava did not grant activity:read_all scope."),
                StravaConnectResult.RateLimited => new FailurePresentation("Strava rate limit reached."),
                StravaConnectResult.Failed failed => FailurePresentation.From(failed.Reason, failed.Details),
                _ => null,
            };
            if (result is StravaConnectResult.Connected)
            {
                Completed?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            IsConnecting = false;
        }
    }

    private bool CanConnect() =>
        !IsConnecting &&
        HasClientCredentials &&
        HasOpenedAuthorizationUrl &&
        !string.IsNullOrWhiteSpace(RedirectUrl);
}
