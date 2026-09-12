using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using StatMaster.Server.Queries;

namespace StatMaster.Server.Endpoints;

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admins")
            .RequireAuthorization("DashboardAccess");

        group.MapPost("/create", async (
            HttpRequest request,
            AdminUserService service,
            CancellationToken ct) =>
        {
            var form = await request.ReadFormAsync(ct);
            string returnUrl = NormalizeProfileReturnUrl(form["returnUrl"].ToString());
            string username = form["username"].ToString();
            string password = form["password"].ToString();
            string confirmPassword = form["confirmPassword"].ToString();

            try
            {
                var status = await service.CreateAsync(username, password, confirmPassword, ct);
                return Results.Redirect($"{returnUrl}{Query(returnUrl)}adminStatus={MapCreate(status)}");
            }
            catch (OperationCanceledException)
            {
                return Results.Redirect($"{returnUrl}{Query(returnUrl)}adminStatus=timeout");
            }
        });

        group.MapPost("/{id:int}/delete", async (
            int id,
            HttpRequest request,
            HttpContext httpContext,
            AdminUserService service,
            CancellationToken ct) =>
        {
            var form = await request.ReadFormAsync(ct);
            string returnUrl = NormalizeProfileReturnUrl(form["returnUrl"].ToString());
            string currentUsername = httpContext.User.Identity?.Name ?? string.Empty;

            try
            {
                var status = await service.DeleteAsync(id, currentUsername, ct);
                return Results.Redirect($"{returnUrl}{Query(returnUrl)}adminStatus={MapDelete(status)}");
            }
            catch (OperationCanceledException)
            {
                return Results.Redirect($"{returnUrl}{Query(returnUrl)}adminStatus=timeout");
            }
        });

        group.MapPost("/change-password", async (
            HttpRequest request,
            HttpContext httpContext,
            AdminUserService service,
            CancellationToken ct) =>
        {
            var form = await request.ReadFormAsync(ct);
            string returnUrl = NormalizeProfileReturnUrl(form["returnUrl"].ToString());
            string currentUsername = httpContext.User.Identity?.Name ?? string.Empty;
            string oldPassword = form["oldPassword"].ToString();
            string newPassword = form["newPassword"].ToString();
            string confirmPassword = form["confirmPassword"].ToString();

            try
            {
                var status = await service.ChangePasswordAsync(
                    currentUsername,
                    oldPassword,
                    newPassword,
                    confirmPassword,
                    ct);

                if (status == AdminPasswordStatus.Changed)
                {
                    var principal = AuthPrincipal.Create(currentUsername, mustChangePassword: false);
                    await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
                }

                return Results.Redirect($"{returnUrl}{Query(returnUrl)}passwordStatus={MapPassword(status)}");
            }
            catch (OperationCanceledException)
            {
                return Results.Redirect($"{returnUrl}{Query(returnUrl)}passwordStatus=timeout");
            }
        });
    }

    private static string NormalizeProfileReturnUrl(string? returnUrl)
    {
        if (!string.IsNullOrWhiteSpace(returnUrl) &&
            returnUrl.StartsWith("/", StringComparison.Ordinal) &&
            !returnUrl.StartsWith("//", StringComparison.Ordinal))
        {
            return returnUrl;
        }

        return "/profile";
    }

    private static string Query(string returnUrl)
    {
        return returnUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
    }

    private static string MapCreate(AdminCreateStatus status) => status switch
    {
        AdminCreateStatus.Created => "created",
        AdminCreateStatus.Duplicate => "duplicate",
        AdminCreateStatus.InvalidUsername => "invalid-user",
        AdminCreateStatus.InvalidPassword => "invalid-pass",
        AdminCreateStatus.Mismatch => "mismatch",
        _ => "invalid-user"
    };

    private static string MapDelete(AdminDeleteStatus status) => status switch
    {
        AdminDeleteStatus.Deleted => "deleted",
        AdminDeleteStatus.IsSelf => "self",
        AdminDeleteStatus.LastAdmin => "last",
        _ => "notfound"
    };

    private static string MapPassword(AdminPasswordStatus status) => status switch
    {
        AdminPasswordStatus.Changed => "saved",
        AdminPasswordStatus.InvalidOld => "old",
        AdminPasswordStatus.Mismatch => "mismatch",
        AdminPasswordStatus.TooShort => "invalid",
        AdminPasswordStatus.UserNotFound => "old",
        _ => "invalid"
    };
}
