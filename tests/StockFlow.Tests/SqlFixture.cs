using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using StockFlow.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace StockFlow.Tests;

public sealed class SqlFixture : IAsyncLifetime, IDbContextFactory<ApplicationDbContext>
{
    private readonly string database = "StockFlowTests_" + Guid.NewGuid().ToString("N");
    private ServiceProvider provider = default!;
    public IServiceScopeFactory ScopeFactory => provider.GetRequiredService<IServiceScopeFactory>();
    public async Task InitializeAsync()
    {
        var connection = Environment.GetEnvironmentVariable("STOCKFLOW_TEST_SQL") ??
            "Server=(localdb)\\mssqllocaldb;Trusted_Connection=True;TrustServerCertificate=True";
        var builder = new SqlConnectionStringBuilder(connection) { InitialCatalog = database };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>(new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider());
        services.AddDbContextFactory<ApplicationDbContext>(options => options.UseSqlServer(builder.ConnectionString));
        services.AddIdentityCore<ApplicationUser>(options => options.Stores.SchemaVersion = IdentitySchemaVersions.Version3)
            .AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>().AddDefaultTokenProviders();
        provider = services.BuildServiceProvider();
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
        using var scope = provider.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in new[] { "Admin", "Operator", "Viewer" })
        {
            var result = await roles.CreateAsync(new IdentityRole(role));
            if (!result.Succeeded) throw new InvalidOperationException("No se pudieron crear los roles de prueba.");
        }
    }
    public ApplicationDbContext CreateDbContext() => provider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>().CreateDbContext();
    public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    public async Task DisposeAsync()
    {
        // Sólo elimina la base aislada y aleatoria creada por esta fixture.
        await using var db = CreateDbContext();
        if (new SqlConnectionStringBuilder(db.Database.GetConnectionString()).InitialCatalog != database || !database.StartsWith("StockFlowTests_"))
            throw new InvalidOperationException("Base de pruebas inesperada.");
        await db.Database.EnsureDeletedAsync();
        await provider.DisposeAsync();
    }
}
