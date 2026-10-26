namespace _20strike_ui.Data;

/// <summary>A simulated user account. The password is stored as a SHA-256 hex digest.</summary>
public class User
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}