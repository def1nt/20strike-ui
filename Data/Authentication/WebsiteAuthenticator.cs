using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using System.Text.Json;

namespace _20strike_ui.Data;

/// <summary>
/// Blazor authentication state provider backed by the simulated user store. Persists the signed-in
/// user in protected browser storage and re-validates it on every state request. Also implements
/// <see cref="IAuthenticationService"/> so pages can log in/out without touching Blazor plumbing.
/// </summary>
public class WebsiteAuthenticator : AuthenticationStateProvider, IAuthenticationService
{
    private readonly ProtectedLocalStorage _protectedLocalStorage;
    private readonly SimulatedDataProviderService _dataProviderService;

    public WebsiteAuthenticator(ProtectedLocalStorage protectedLocalStorage, SimulatedDataProviderService dataProviderService)
    {
        _protectedLocalStorage = protectedLocalStorage;
        _dataProviderService = dataProviderService;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var principal = new ClaimsPrincipal();

        try
        {
            var storedPrincipal = await _protectedLocalStorage.GetAsync<string>("identity");

            if (storedPrincipal.Success)
            {
                var user = JsonSerializer.Deserialize<User>(storedPrincipal.Value!);
                var isLookUpSuccess = _dataProviderService.FindUser(user!.Username, user.Password) is not null;

                if (isLookUpSuccess)
                {
                    principal = new(UserIdentityFactory.CreateIdentity(user));
                }
            }
        }
        catch { }

        return new AuthenticationState(principal);
    }

    public async Task LoginAsync(string username, string password)
    {
        var userInDatabase = _dataProviderService.FindUser(username, PasswordHasher.Hash(password));
        var principal = new ClaimsPrincipal();

        if (userInDatabase is not null)
        {
            principal = new ClaimsPrincipal(UserIdentityFactory.CreateIdentity(userInDatabase));
            await _protectedLocalStorage.SetAsync("identity", JsonSerializer.Serialize(userInDatabase));
        }

        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(principal)));
    }

    public async Task LogoutAsync()
    {
        await _protectedLocalStorage.DeleteAsync("identity");
        var principal = new ClaimsPrincipal();
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(principal)));
    }

    public async Task<string> GetCurrentUserNameAsync()
    {
        try
        {
            var storedPrincipal = await _protectedLocalStorage.GetAsync<string>("identity");

            if (storedPrincipal.Success)
            {
                var user = JsonSerializer.Deserialize<User>(storedPrincipal.Value!);
                return user!.Username;
            }
        }
        catch
        {
            return "Error!";
        }
        return "None";
    }
}