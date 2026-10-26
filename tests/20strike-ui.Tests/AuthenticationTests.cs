using System.Security.Claims;
using _20strike_ui.Data;

namespace _20strike_ui.Tests;

public class AuthenticationTests
{
    [Fact]
    public void PasswordHasher_ProducesUppercaseHexSha256()
    {
        // SHA-256("password") and SHA-256("admin") in uppercase hex, matching the simulated store.
        Assert.Equal("5E884898DA28047151D0E56F8DC6292773603D0D6AABBDD62A11EF721D1542D8", PasswordHasher.Hash("password"));
        Assert.Equal("8C6976E5B5410415BDE908BD4DEE15DFB167A9C873FC4BB8A81F6F2AB448A918", PasswordHasher.Hash("admin"));
    }

    [Fact]
    public void SimulatedUserStore_FindsUserByExactCredentials()
    {
        var store = new SimulatedDataProviderService();

        var user = store.FindUser("User1", PasswordHasher.Hash("password"));

        Assert.NotNull(user);
        Assert.Equal("User1", user!.Username);
    }

    [Fact]
    public void SimulatedUserStore_RejectsWrongPassword()
    {
        var store = new SimulatedDataProviderService();

        Assert.Null(store.FindUser("User1", "wrong"));
    }

    [Fact]
    public void SimulatedUserStore_RejectsUnknownUser()
    {
        var store = new SimulatedDataProviderService();

        Assert.Null(store.FindUser("nobody", PasswordHasher.Hash("password")));
    }

    [Fact]
    public void UserIdentityFactory_CreatesNameClaimWithDummyScheme()
    {
        var identity = UserIdentityFactory.CreateIdentity(new User { Username = "User1", Password = "hash" });

        Assert.Equal("dummy", identity.AuthenticationType);
        Assert.Equal("User1", identity.Name);
        Assert.Equal("hash", identity.FindFirst(ClaimTypes.Hash)?.Value);
    }
}