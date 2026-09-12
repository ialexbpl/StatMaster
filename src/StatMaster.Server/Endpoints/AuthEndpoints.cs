using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using StatMaster.Server.Queries;

namespace StatMaster.Server.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth");

        group.MapPost("/login", async (
            LoginRequest request,
            HttpContext httpContext,
            AdminUserService users,
            IPasswordHasher<AdminUserModel> hasher) =>
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
                return Results.BadRequest(new { message = "Username and password are required." });

            var user = await users.FindByUsernameAsync(request.Username);
            if (user is null)
                return Results.Unauthorized();

            var verify = hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
            if (verify == PasswordVerificationResult.Failed)
                return Results.Unauthorized();

            var principal = AuthPrincipal.Create(user.Username, user.MustChangePassword);
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
            bool mustChange = AuthPrincipal.IsMustChangePassword(httpContext.User);
            return Results.Ok(new { username, mustChangePassword = mustChange });
        }).RequireAuthorization();

        group.MapPost("/logout", async (HttpContext httpContext) =>
        {
            await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            if (httpContext.Request.HasFormContentType)
                return Results.Redirect("/login");

            return Results.Ok();
        }).RequireAuthorization();

        group.MapPost("/change-password", async (
            ChangePasswordRequest request,
            HttpContext httpContext,
            AdminUserService users) =>
        {
            if (httpContext.User.Identity?.IsAuthenticated != true)
                return Results.Unauthorized();

            string username = httpContext.User.Identity?.Name ?? string.Empty;
            var status = await users.ChangePasswordAsync(
                username,
                request.OldPassword,
                request.NewPassword,
                request.ConfirmPassword);

            return status switch
            {
                AdminPasswordStatus.Changed => await SignInAndOk(httpContext, username),
                AdminPasswordStatus.UserNotFound => Results.Unauthorized(),
                AdminPasswordStatus.InvalidOld => Results.BadRequest(new { message = "Old password is invalid." }),
                AdminPasswordStatus.Mismatch => Results.BadRequest(new { message = "New password and confirmation do not match." }),
                AdminPasswordStatus.TooShort => Results.BadRequest(new { message = "New password must be at least 8 characters long." }),
                _ => Results.BadRequest(new { message = "All password fields are required." })
            };
        }).RequireAuthorization();
    }

    private static async Task<IResult> SignInAndOk(HttpContext httpContext, string username)
    {
        var principal = AuthPrincipal.Create(username, mustChangePassword: false);
        await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
        return Results.Ok(new { message = "Password changed successfully." });
    }

    private sealed record LoginRequest(string Username, string Password);
    private sealed record ChangePasswordRequest(string OldPassword, string NewPassword, string ConfirmPassword);
}
