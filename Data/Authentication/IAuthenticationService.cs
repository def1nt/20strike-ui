namespace _20strike_ui.Data;

/// <summary>
/// Authentication operations used by pages. The concrete implementation also serves as the Blazor
/// <see cref="Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider"/>; both are
/// registered to the same scoped instance so login/logout notifications reach AuthorizeView.
/// </summary>
public interface IAuthenticationService
{
    /// <summary>Validates the plain-text password against the simulated store and signs the user in.</summary>
    Task LoginAsync(string username, string password);

    /// <summary>Signs the current user out.</summary>
    Task LogoutAsync();

    /// <summary>Returns the current user's name, "None" when signed out, or "Error!" on storage failure.</summary>
    Task<string> GetCurrentUserNameAsync();
}