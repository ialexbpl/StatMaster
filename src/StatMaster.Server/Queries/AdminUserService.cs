using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StatMaster.Server.Contracts;

namespace StatMaster.Server.Queries;

public sealed class AdminUserService
{
    private static readonly Regex UsernamePattern = new( //HELPER regex pattern for username validation
        @"^[A-Za-z0-9._-]{3,32}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IDbContextFactory<StatMasterDbContext> _dbFactory; //opening a connection to the database
    private readonly IPasswordHasher<AdminUserModel> _hasher; //hashing the password

    public AdminUserService(
        IDbContextFactory<StatMasterDbContext> dbFactory,
        IPasswordHasher<AdminUserModel> hasher)
    {
        _dbFactory = dbFactory;
        _hasher = hasher;
    }

    public async Task<IReadOnlyList<AdminUserDto>> GetUsersAsync(
        string currentUsername, //current user's username coming from the coookie
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct); //opening a connection to the database

        var rows = await db.AdminUsers //getting all the admin users from the database
            .AsNoTracking()
            .OrderBy(x => x.Username)
            .ToListAsync(ct);

        return rows
            .Select(x => new AdminUserDto
            {
                Id = x.Id,
                Username = x.Username,
                MustChangePassword = x.MustChangePassword,
                CreatedAtUtc = x.CreatedAtUtc,
                IsCurrentUser = string.Equals(x.Username, currentUsername, StringComparison.OrdinalIgnoreCase)
            })
            .ToList();
    }

    public async Task<AdminCreateStatus> CreateAsync( //this is used to create a new admin user add admin
        string? username,
        string? password,
        string? confirmPassword,
        CancellationToken ct = default)
    {
        string trimmed = (username ?? string.Empty).Trim();
        if (!UsernamePattern.IsMatch(trimmed))
            return AdminCreateStatus.InvalidUsername;

        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
            return AdminCreateStatus.InvalidPassword;

        if (!string.Equals(password, confirmPassword, StringComparison.Ordinal))
            return AdminCreateStatus.Mismatch;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        bool exists = await db.AdminUsers.AnyAsync(
            x => x.Username.ToLower() == trimmed.ToLower(),
            ct);
        if (exists)
            return AdminCreateStatus.Duplicate;

        var user = new AdminUserModel
        {
            Username = trimmed,
            PasswordHash = string.Empty,
            MustChangePassword = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        user.PasswordHash = _hasher.HashPassword(user, password);
        db.AdminUsers.Add(user);
        await db.SaveChangesAsync(ct);
        return AdminCreateStatus.Created;
    }

    public async Task<AdminDeleteStatus> DeleteAsync(
        int id,
        string currentUsername,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var user = await db.AdminUsers.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (user is null)
            return AdminDeleteStatus.NotFound;

        if (string.Equals(user.Username, currentUsername, StringComparison.OrdinalIgnoreCase))
            return AdminDeleteStatus.IsSelf;

        int count = await db.AdminUsers.CountAsync(ct);
        if (count <= 1)
            return AdminDeleteStatus.LastAdmin;

        db.AdminUsers.Remove(user);
        await db.SaveChangesAsync(ct);
        return AdminDeleteStatus.Deleted;
    }

    public async Task<AdminPasswordStatus> ChangePasswordAsync(
        string currentUsername,
        string? oldPassword,
        string? newPassword,
        string? confirmPassword,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(oldPassword) ||
            string.IsNullOrWhiteSpace(newPassword) ||
            string.IsNullOrWhiteSpace(confirmPassword))
        {
            return AdminPasswordStatus.MissingFields;
        }

        if (!string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
            return AdminPasswordStatus.Mismatch;

        if (newPassword.Length < 8)
            return AdminPasswordStatus.TooShort;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var user = await db.AdminUsers.FirstOrDefaultAsync(
            x => x.Username.ToLower() == currentUsername.ToLower(),
            ct);
        if (user is null)
            return AdminPasswordStatus.UserNotFound;

        var verify = _hasher.VerifyHashedPassword(user, user.PasswordHash, oldPassword);
        if (verify == PasswordVerificationResult.Failed)
            return AdminPasswordStatus.InvalidOld;

        user.PasswordHash = _hasher.HashPassword(user, newPassword);
        user.MustChangePassword = false;
        user.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return AdminPasswordStatus.Changed;
    }

    public async Task<AdminUserModel?> FindByUsernameAsync(string username, CancellationToken ct = default)
    {
        string trimmed = username.Trim();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.AdminUsers.FirstOrDefaultAsync(
            x => x.Username.ToLower() == trimmed.ToLower(),
            ct);
    }
}

public enum AdminCreateStatus
{
    Created,
    InvalidUsername,
    InvalidPassword,
    Mismatch,
    Duplicate
}

public enum AdminDeleteStatus
{
    Deleted,
    NotFound,
    IsSelf,
    LastAdmin
}

public enum AdminPasswordStatus
{
    Changed,
    MissingFields,
    Mismatch,
    TooShort,
    InvalidOld,
    UserNotFound
}
