using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace StatMaster.Server.Endpoints;

internal static class AuthPrincipal
{
    public const string MustChangeClaimType = "must_change_password"; //HELPER claim type for must change password program.cs 

    public static ClaimsPrincipal Create(string username, bool mustChangePassword) //create 
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, username),
            new(MustChangeClaimType, mustChangePassword ? "true" : "false")
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        return new ClaimsPrincipal(identity); //claims principal is an object ASP.NET Core uses to represent the current user
    }

    public static bool IsMustChangePassword(ClaimsPrincipal principal) //check if the user must change their password
    {
        return string.Equals(
            principal.FindFirstValue(MustChangeClaimType),
            "true",
            StringComparison.OrdinalIgnoreCase);
    }
}
