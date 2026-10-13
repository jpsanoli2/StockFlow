using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StockFlow.Web.Data;

namespace StockFlow.Web.Services;

public record UserRoleRow(string Id, string Email, string Role);
public sealed class UserAdminService(IServiceScopeFactory scopes, IActor actor)
{
    private async Task<string> RequireAsync()
    {
        var user = await actor.GetUserAsync();
        if (user.Identity?.IsAuthenticated != true || !user.IsInRole("Admin")) throw new UnauthorizedAccessException();
        return user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value;
    }
    public async Task<List<UserRoleRow>> ListAsync()
    {
        await RequireAsync(); using var scope = scopes.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var result = new List<UserRoleRow>();
        foreach (var user in await manager.Users.OrderBy(x => x.Email).ToListAsync())
            result.Add(new(user.Id, user.Email ?? "", string.Join(", ", await manager.GetRolesAsync(user))));
        return result;
    }
    public async Task SetRoleAsync(string id, string role)
    {
        var ownId = await RequireAsync();
        if (id == ownId) throw new Domain.BusinessException("No podés modificar tu propio rol.");
        if (!new[] { "Admin", "Operator", "Viewer" }.Contains(role)) throw new Domain.BusinessException("Rol inválido.");
        using var scope = scopes.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync();
        var user = await manager.FindByIdAsync(id) ?? throw new Domain.BusinessException("Usuario inexistente.");
        var oldRoles = await manager.GetRolesAsync(user);
        Check(await manager.RemoveFromRolesAsync(user, oldRoles));
        Check(await manager.AddToRoleAsync(user, role));
        Check(await manager.UpdateSecurityStampAsync(user));
        await tx.CommitAsync();
    }
    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded) throw new Domain.BusinessException(string.Join("; ", result.Errors.Select(x => x.Description)));
    }
}
