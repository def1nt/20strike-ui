namespace _20strike_ui.Data;

/// <summary>
/// In-memory user store backing the simulated authentication. Replace with a real identity
/// provider when backend authentication is introduced.
/// </summary>
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

    /// <summary>Finds a user by exact username and password hash, or <c>null</c> when credentials do not match.</summary>
    public User? FindUser(string username, string passwordHash) =>
        Users.FirstOrDefault(u => username == u.Username && passwordHash == u.Password);
}