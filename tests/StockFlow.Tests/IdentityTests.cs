using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using StockFlow.Web.Data;
using StockFlow.Web.Domain;
using StockFlow.Web.Services;

namespace StockFlow.Tests;

public sealed class IdentityTests(SqlFixture sql) : IClassFixture<SqlFixture>
{
    private async Task<ClaimsPrincipal> User(string role)
    {
        using var scope = sql.ScopeFactory.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var factory = scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>();
        var email = Guid.NewGuid().ToString("N") + "@example.test";
        var user = new ApplicationUser { UserName = email, Email = email };
        Assert.True((await manager.CreateAsync(user, "StockFlow!Test2026")).Succeeded);
        Assert.True((await manager.AddToRoleAsync(user, role)).Succeeded);
        Assert.True(await manager.CheckPasswordAsync(user, "StockFlow!Test2026"));
        return await factory.CreateAsync(user);
    }
    [Fact] public async Task Actor_resolves_current_identity_roles()
    {
        var principal = await User("Operator");
        var actor = new BlazorActor(new FixedState(principal), sql.ScopeFactory);
        var current = await actor.GetUserAsync();
        Assert.True(current.Identity!.IsAuthenticated); Assert.True(current.IsInRole("Operator"));
        Assert.False(current.IsInRole("Admin"));
    }
    [Fact] public async Task Role_change_invalidates_previous_session_for_business_operations()
    {
        var admin = await User("Admin"); var viewer = await User("Viewer");
        var adminActor = new BlazorActor(new FixedState(admin), sql.ScopeFactory);
        var service = new UserAdminService(sql.ScopeFactory, adminActor);
        await service.SetRoleAsync(viewer.FindFirstValue(ClaimTypes.NameIdentifier)!, "Operator");
        var staleActor = new BlazorActor(new FixedState(viewer), sql.ScopeFactory);
        Assert.False((await staleActor.GetUserAsync()).Identity!.IsAuthenticated);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new InventoryService(sql, staleActor).ProductsAsync());
        using var scope = sql.ScopeFactory.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await manager.FindByIdAsync(viewer.FindFirstValue(ClaimTypes.NameIdentifier)!);
        Assert.Equal("Operator", Assert.Single(await manager.GetRolesAsync(user!)));
    }
    [Fact] public async Task Admin_cannot_remove_own_role()
    {
        var principal = await User("Admin");
        var service = new UserAdminService(sql.ScopeFactory, new BlazorActor(new FixedState(principal), sql.ScopeFactory));
        await Assert.ThrowsAsync<BusinessException>(() => service.SetRoleAsync(principal.FindFirstValue(ClaimTypes.NameIdentifier)!, "Viewer"));
    }
    [Fact] public async Task Viewer_cannot_list_or_assign_roles()
    {
        var principal = await User("Viewer");
        var service = new UserAdminService(sql.ScopeFactory, new BlazorActor(new FixedState(principal), sql.ScopeFactory));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ListAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SetRoleAsync("someone", "Admin"));
    }
    private sealed class FixedState(ClaimsPrincipal user) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(user));
    }
}
