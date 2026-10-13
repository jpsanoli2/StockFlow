using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using StockFlow.Web.Data;

namespace StockFlow.Web.Services;

public interface IActor
{
    Task<ClaimsPrincipal> GetUserAsync();
}
public sealed class BlazorActor(AuthenticationStateProvider provider, IServiceScopeFactory scopes) : IActor
{
    public async Task<ClaimsPrincipal> GetUserAsync()
    {
        var principal = (await provider.GetAuthenticationStateAsync()).User;
        if (principal.Identity?.IsAuthenticated != true) return principal;
        using var scope = scopes.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await manager.GetUserAsync(principal);
        if (user == null || principal.FindFirstValue("AspNet.Identity.SecurityStamp") != await manager.GetSecurityStampAsync(user))
            return new ClaimsPrincipal(new ClaimsIdentity());
        var claims = principal.Claims.Where(x => x.Type != ClaimTypes.Role).ToList();
        claims.AddRange((await manager.GetRolesAsync(user)).Select(role => new Claim(ClaimTypes.Role, role)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Identity.Application"));
    }
}
