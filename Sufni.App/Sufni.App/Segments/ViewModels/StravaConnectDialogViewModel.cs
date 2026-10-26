using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Sufni.App.Infrastructure;
using Sufni.App.Segments.Models;
using Sufni.App.Segments.Services;
namespace Sufni.App.Segments.ViewModels;

/// <summary>
/// Content of the dialog that connects the user's own Strava API app. The user
/// opens the authorization URL in the browser and pastes the redirect URL
/// back. The dialog completes on cancel and after a successful connect.
/// </summary>
public sealed partial class StravaConnectDialogViewModel : ObservableObject, IContentDialogCompletionSource
{
    private readonly IStravaSegmentService stravaService;

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
    public partial string? ErrorMessage { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOpenAuthorizationUrl))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    public partial bool IsConnecting { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    public partial bool HasOpenedAuthorizationUrl { get; private set; }

    public StravaConnectDialogViewModel(IStravaSegmentService stravaService, StravaConnectionSnapshot connection)
    {
        this.stravaService = stravaService;
        ClientId = connection.ClientId;
        ClientSecret = connection.ClientSecret;
    }

    public string AuthorizationUrl => HasClientCredentials
        ? stravaService.CreateAuthorizationUrl(ClientId!.Trim())
        : string.Empty;

    public bool HasClientCredentials =>
        !string.IsNullOrWhiteSpace(ClientId) &&
        !string.IsNullOrWhiteSpace(ClientSecret);

    public bool CanOpenAuthorizationUrl => HasClientCredentials && !IsConnecting;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

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
    private void Cancel() => Completed?.Invoke(this, EventArgs.Empty);

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task Connect(CancellationToken cancellationToken)
    {
        ErrorMessage = null;
        IsConnecting = true;
        try
        {
            var result = await stravaService.ConnectAsync(
                new StravaConnectRequest(ClientId, ClientSecret, RedirectUrl),
                cancellationToken);
            ErrorMessage = result switch
            {
                StravaConnectResult.ValidationFailed validation => validation.Message,
                StravaConnectResult.MissingRequiredScope => "Strava did not grant activity:read_all scope.",
                StravaConnectResult.RateLimited => "Strava rate limit reached.",
                StravaConnectResult.Failed failed => failed.Message,
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
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
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
