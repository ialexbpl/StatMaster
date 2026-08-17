using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace StatMaster.Server.Endpoints;

public static class AuthEndpoints
{
    private const string MustChangeClaimType = "must_change_password";

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth");

        group.MapPost("/login", async (
            LoginRequest request,
            HttpContext httpContext,
            IDbContextFactory<StatMasterDbContext> dbFactory,
            IPasswordHasher<AdminUserModel> hasher) =>
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
                return Results.BadRequest(new { message = "Username and password are required." });

            await using var db = await dbFactory.CreateDbContextAsync();
            var user = await db.AdminUsers.FirstOrDefaultAsync(x => x.Username == request.Username);
            if (user is null)
                return Results.Unauthorized();

            var verify = hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
            if (verify == PasswordVerificationResult.Failed)
                return Results.Unauthorized();

            var principal = CreatePrincipal(user.Username, user.MustChangePassword);
            await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

            return Results.Ok(new
            {
                username = user.Username,
                mustChangePassword = user.MustChangePassword
            });
        }).AllowAnonymous();

        group.MapGet("/me", (HttpContext httpContext) =>
        {
            if (httpContext.User.Identity?.IsAuthenticated != true)
                return Results.Unauthorized();

            string username = httpContext.User.Identity?.Name ?? string.Empty;
            bool mustChange = IsMustChangePassword(httpContext.User);
            return Results.Ok(new { username, mustChangePassword = mustChange });
        }).RequireAuthorization();

        group.MapPost("/logout", async (HttpContext httpContext) =>
        {
            await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok();
        }).RequireAuthorization();

        group.MapPost("/change-password", async (
            ChangePasswordRequest request,
            HttpContext httpContext,
            IDbContextFactory<StatMasterDbContext> dbFactory,
            IPasswordHasher<AdminUserModel> hasher) =>
        {
            if (httpContext.User.Identity?.IsAuthenticated != true)
                return Results.Unauthorized();

            if (string.IsNullOrWhiteSpace(request.OldPassword) ||
                string.IsNullOrWhiteSpace(request.NewPassword) ||
                string.IsNullOrWhiteSpace(request.ConfirmPassword))
            {
                return Results.BadRequest(new { message = "All password fields are required." });
            }

            if (!string.Equals(request.NewPassword, request.ConfirmPassword, StringComparison.Ordinal))
                return Results.BadRequest(new { message = "New password and confirmation do not match." });

            if (request.NewPassword.Length < 8)
                return Results.BadRequest(new { message = "New password must be at least 8 characters long." });

            string username = httpContext.User.Identity?.Name ?? string.Empty;
            await using var db = await dbFactory.CreateDbContextAsync();
            var user = await db.AdminUsers.FirstOrDefaultAsync(x => x.Username == username);
            if (user is null)
                return Results.Unauthorized();

            var verify = hasher.VerifyHashedPassword(user, user.PasswordHash, request.OldPassword);
            if (verify == PasswordVerificationResult.Failed)
                return Results.BadRequest(new { message = "Old password is invalid." });

            user.PasswordHash = hasher.HashPassword(user, request.NewPassword);
            user.MustChangePassword = false;
            user.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();

            var principal = CreatePrincipal(user.Username, mustChangePassword: false);
            await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

            return Results.Ok(new { message = "Password changed successfully." });
        }).RequireAuthorization();
    }

    private static ClaimsPrincipal CreatePrincipal(string username, bool mustChangePassword)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, username),
            new(MustChangeClaimType, mustChangePassword ? "true" : "false")
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        return new ClaimsPrincipal(identity);
    }

    private static bool IsMustChangePassword(ClaimsPrincipal principal)
    {
        return string.Equals(
            principal.FindFirstValue(MustChangeClaimType),
            "true",
            StringComparison.OrdinalIgnoreCase);
    }

    private sealed record LoginRequest(string Username, string Password);
    private sealed record ChangePasswordRequest(string OldPassword, string NewPassword, string ConfirmPassword);
}
