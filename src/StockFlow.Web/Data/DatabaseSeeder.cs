using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StockFlow.Web.Domain;

namespace StockFlow.Web.Data;

public static class DatabaseSeeder
{
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration config, bool development)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in new[] { "Admin", "Operator", "Viewer" })
            if (!await roles.RoleExistsAsync(role)) Check(await roles.CreateAsync(new IdentityRole(role)));
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var password = config["Seed:AdminPassword"] ?? (development ? "StockFlow!2026" : null);
        if (password != null)
        {
            var email = config["Seed:AdminEmail"] ?? "admin@stockflow.local";
            var admin = await users.FindByEmailAsync(email);
            if (admin == null)
            {
                admin = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
                Check(await users.CreateAsync(admin, password));
            }
            if (!await users.IsInRoleAsync(admin, "Admin")) Check(await users.AddToRoleAsync(admin, "Admin"));
        }
        if (development && !await db.Categories.AnyAsync())
        {
            var technology = new Category { Name = "Tecnología", Description = "Periféricos y accesorios" };
            var office = new Category { Name = "Oficina", Description = "Material de trabajo" };
            db.Categories.AddRange(technology, office);
            var products = new[] {
                new Product { Sku = "TEC-001", Name = "Teclado mecánico", Price = 79.90m, Stock = 24, MinimumStock = 5, Category = technology },
                new Product { Sku = "TEC-002", Name = "Mouse inalámbrico", Price = 29.50m, Stock = 4, MinimumStock = 5, Category = technology },
                new Product { Sku = "OFI-001", Name = "Cuaderno A5", Price = 6.50m, Stock = 60, MinimumStock = 10, Category = office }
            };
            db.Products.AddRange(products);
            foreach (var product in products)
                db.StockMovements.Add(new StockMovement { Product = product, Quantity = product.Stock, Balance = product.Stock, Reason = "Stock inicial de demostración", CreatedBy = "Sistema" });
            await db.SaveChangesAsync();
        }
    }
    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded) throw new InvalidOperationException(string.Join("; ", result.Errors.Select(x => x.Description)));
    }
}
