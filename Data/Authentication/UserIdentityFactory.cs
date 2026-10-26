using System.Security.Claims;

namespace _20strike_ui.Data;

/// <summary>Builds the claims identity for a signed-in user (dummy authentication scheme).</summary>
internal static class UserIdentityFactory
{
    public static ClaimsIdentity CreateIdentity(User user) => new(new Claim[]
    {
        new(ClaimTypes.Name, user.Username),
        new(ClaimTypes.Hash, user.Password)
    }, "dummy");
}