namespace StatMaster.Server.Contracts;

public sealed class AdminUserDto
{
    public int Id { get; set; }
    public required string Username { get; set; }
    public bool MustChangePassword { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public bool IsCurrentUser { get; set; }
}
