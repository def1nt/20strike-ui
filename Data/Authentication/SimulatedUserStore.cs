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
            Password = "197FDEE283DDF055C71813C9ABCCEF83B9CD3283331431C07DA05315BD1848C2"
        },
        new()
        {
            Username = "admin",
            Password = "2E2B24F8EE40BB847FE85BB23336A39EF5948E6B49D897419CED68766B16967A"
        }
    };

    /// <summary>Finds a user by exact username and password hash, or <c>null</c> when credentials do not match.</summary>
    public User? FindUser(string username, string passwordHash) =>
        Users.FirstOrDefault(u => username == u.Username && passwordHash == u.Password);
}