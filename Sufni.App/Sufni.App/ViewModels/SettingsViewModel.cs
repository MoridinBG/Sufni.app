using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using SecureStorage;
using Sufni.App.Services;

namespace Sufni.App.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    #region Observable properties

    [ObservableProperty] private string registerLabel = "connect";
    [ObservableProperty] private string? serverUrl;
    [ObservableProperty] private string? username;
    [ObservableProperty] private string? password;
    [ObservableProperty] private bool isRegistered;

    #endregion

    #region Property change handlers

    partial void OnIsRegisteredChanged(bool value)
    {
        RegisterLabel = value ? "disconnect" : "connect";
    }

    #endregion

    #region Private members

    private ISecureStorage? secureStorage;
    private IHttpApiService? httpApiService;

    #endregion Private members

    #region Constructors

    public SettingsViewModel()
    {
        _ = InitAsync();
    }

    #endregion

    #region Private methods

    private async Task InitAsync()
    {
        secureStorage = App.Current?.Services?.GetService<ISecureStorage>();
        httpApiService = App.Current?.Services?.GetService<IHttpApiService>();

        Debug.Assert(httpApiService != null, nameof(httpApiService) + " != null");
        Debug.Assert(secureStorage != null, nameof(secureStorage) + " != null");

        try
        {
            ServerUrl = await secureStorage.GetStringAsync("ServerUrl");
            Username = await secureStorage.GetStringAsync("Username");
            var refreshToken = await secureStorage.GetStringAsync("RefreshToken");
            await RefreshTokensAsync(ServerUrl, refreshToken);
        }
        catch (Exception e)
        {
            ErrorMessages.Add($"Could not read API connection information: {e.Message}");
        }
    }

    private async Task RefreshTokensAsync(string? url, string? refreshToken)
    {
        Debug.Assert(httpApiService != null, nameof(httpApiService) + " != null");
        Debug.Assert(secureStorage != null, nameof(secureStorage) + " != null");

        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(refreshToken))
        {
            IsRegistered = false;
            return;
        }

        // Set the new token's value to the old one, so that in case of e.g. a network error,
        // we don't indicate "unregistered" state.
        // NOTE: We could also check if token is still valid based on time (this would of course
        //       not consider revoked keys, etc.)
        var newRefreshToken = refreshToken;
        IsRegistered = true;

        try
        {
            newRefreshToken = await httpApiService.RefreshTokensAsync(url, refreshToken);
        }
        catch (HttpRequestException e)
        {
            // Null out the refresh token if we explicitly receive a 401 - Unauthorized HTTP response.
            if (e.StatusCode == HttpStatusCode.Unauthorized)
            {
                newRefreshToken = null;
                IsRegistered = false;
                ErrorMessages.Add("Refresh token is not longer valid. Please log in!");
            }
            else
            {
                ErrorMessages.Add($"Could not refresh tokens: {e.Message}");
            }
        }
        finally
        {
            await secureStorage.SetStringAsync("RefreshToken", newRefreshToken);
        }
    }

    private async Task RegisterAsync()
    {
        Debug.Assert(httpApiService != null, nameof(httpApiService) + " != null");
        Debug.Assert(secureStorage != null, nameof(secureStorage) + " != null");

        if (string.IsNullOrEmpty(ServerUrl) || string.IsNullOrEmpty(Username) || string.IsNullOrEmpty(Password)) return;

        try
        {
            var refreshToken = await httpApiService.RegisterAsync(ServerUrl, Username, Password);
            await secureStorage.SetStringAsync("Username", Username);
            await secureStorage.SetStringAsync("ServerUrl", ServerUrl);
            await secureStorage.SetStringAsync("RefreshToken", refreshToken);
            IsRegistered = true;
            ErrorMessages.Clear();
        }
        catch (Exception e)
        {
            ErrorMessages.Add($"Could not register: {e.Message}");
        }
    }

    private async Task UnregisterAsync()
    {
        Debug.Assert(httpApiService != null, nameof(httpApiService) + " != null");
        Debug.Assert(secureStorage != null, nameof(secureStorage) + " != null");

        try
        {
            var refreshToken = await secureStorage.GetStringAsync("RefreshToken");
            await httpApiService.UnregisterAsync(refreshToken!);
            await secureStorage.RemoveAsync("Username");
            await secureStorage.RemoveAsync("ServerUrl");
            await secureStorage.RemoveAsync("RefreshToken");
            Username = null;
            ServerUrl = null;
            Password = null;
            IsRegistered = false;
        }
        catch (Exception e)
        {
            ErrorMessages.Add($"Could not unregister: {e.Message}");
        }
    }

    #endregion Private methods

    #region Commands

    [RelayCommand]
    private async Task RegisterUnregister()
    {
        if (IsRegistered) await UnregisterAsync();
        else
        {
            await RegisterAsync();
            OpenPreviousPage();
        }
    }

    #endregion Commands
}
