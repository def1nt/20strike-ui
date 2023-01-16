using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
namespace _20strike_ui.Data;

public class WebsiteAuthenticator : AuthenticationStateProvider
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
                var (_, isLookUpSuccess) = LookUpUser(user!.Username, user.Password);

                if (isLookUpSuccess)
                {
                    var identity = CreateIdentityFromUser(user);
                    principal = new(identity);
                }
            }
        }
        catch { }

        return new AuthenticationState(principal);
    }

    public async Task LoginAsync(string Username, string Password)
    {
        var (userInDatabase, isSuccess) = LookUpUser(Username, Password);
        var principal = new ClaimsPrincipal();

        if (isSuccess)
        {
            var identity = CreateIdentityFromUser(userInDatabase!);
            principal = new ClaimsPrincipal(identity);
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

    private static ClaimsIdentity CreateIdentityFromUser(User user)
    {
        return new ClaimsIdentity(new Claim[]
        {
            new (ClaimTypes.Name, user.Username),
            new (ClaimTypes.Hash, user.Password)
        }, "BlazorSchool");
    }

    private (User?, bool) LookUpUser(string username, string password)
    {
        var result = _dataProviderService.Users.FirstOrDefault(u => username == u.Username && password == u.Password);

        return (result, result is not null);
    }

    public async Task<string> Who()
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

public class SimulatedDataProviderService
{
    public List<User> Users { get; set; } = new()
    {
        new()
        {
            Username = "User1",
            Password = "5E884898DA28047151D0E56F8DC6292773603D0D6AABBDD62A11EF721D1542D8"
        },
        new()
        {
            Username = "admin",
            Password = "8C6976E5B5410415BDE908BD4DEE15DFB167A9C873FC4BB8A81F6F2AB448A918"
        }
    };

    public IEnumerable<string> GetUserRoles(User user)
    {
        var result = user.Username switch
        {
            "User1" => new List<string>() { "Admin", "User" },
            "User2" => new List<string>() { "User" },
            _ => new List<string>() { },
        };

        return result;
    }
}

public class User
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}
