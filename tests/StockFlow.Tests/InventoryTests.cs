using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using StockFlow.Web.Domain;
using StockFlow.Web.Services;

namespace StockFlow.Tests;

public sealed class InventoryTests(SqlFixture sql) : IClassFixture<SqlFixture>
{
    private InventoryService Service(string role = "Admin") => new(sql, new FakeActor(role));
    private async Task<Product> Product(int stock = 10, decimal price = 12.50m)
    {
        var service = Service(); var name = Guid.NewGuid().ToString("N");
        await service.SaveCategoryAsync(new Category { Name = name });
        await using var db = sql.CreateDbContext();
        var category = await db.Categories.SingleAsync(x => x.Name == name);
        await service.SaveProductAsync(new Product { Name = "Producto " + name, Sku = name, Price = price, Stock = stock, CategoryId = category.Id });
        return await db.Products.AsNoTracking().SingleAsync(x => x.CategoryId == category.Id);
    }

    [Fact] public async Task Order_updates_stock_total_and_movement()
    {
        var p = await Product();
        var id = await Service("Operator").CreateOrderAsync("Cliente", [new(p.Id, 3)], Guid.NewGuid());
        await using var db = sql.CreateDbContext();
        Assert.Equal(7, (await db.Products.FindAsync(p.Id))!.Stock);
        var order = await db.Orders.Include(x => x.Lines).SingleAsync(x => x.Id == id);
        Assert.Equal(37.50m, order.Total); Assert.Single(order.Lines);
        var movement = await db.StockMovements.SingleAsync(x => x.OrderId == id);
        Assert.Equal(-3, movement.Quantity); Assert.Equal(7, movement.Balance); Assert.Equal("tester", movement.CreatedBy);
    }
    [Fact] public async Task Insufficient_stock_rolls_back_all_lines()
    {
        var first = await Product(10); var second = await Product(1); var key = Guid.NewGuid();
        await Assert.ThrowsAsync<BusinessException>(() => Service().CreateOrderAsync("Cliente", [new(first.Id, 2), new(second.Id, 2)], key));
        await using var db = sql.CreateDbContext();
        Assert.Equal(10, (await db.Products.FindAsync(first.Id))!.Stock);
        Assert.False(await db.Orders.AnyAsync(x => x.RequestId == key));
        Assert.Single(await db.StockMovements.Where(x => x.ProductId == first.Id).ToListAsync());
    }
    [Fact] public async Task Repeated_lines_are_grouped()
    {
        var p = await Product(); var id = await Service().CreateOrderAsync("Cliente", [new(p.Id, 2), new(p.Id, 3)], Guid.NewGuid());
        await using var db = sql.CreateDbContext();
        var lines = await db.OrderLines.Where(x => x.OrderId == id).ToListAsync();
        Assert.Equal(5, Assert.Single(lines).Quantity); Assert.Equal(5, (await db.Products.FindAsync(p.Id))!.Stock);
    }
    [Fact] public async Task Duplicate_request_does_not_sell_twice()
    {
        var p = await Product(); var key = Guid.NewGuid(); var service = Service();
        var first = await service.CreateOrderAsync("Cliente", [new(p.Id, 2)], key);
        var second = await service.CreateOrderAsync("Cliente", [new(p.Id, 2)], key);
        Assert.Equal(first, second);
        await using var db = sql.CreateDbContext(); Assert.Equal(8, (await db.Products.FindAsync(p.Id))!.Stock);
        Assert.Single(await db.StockMovements.Where(x => x.OrderId == first).ToListAsync());
    }
    [Theory] [InlineData(0)] [InlineData(-1)] [InlineData(1000001)]
    public async Task Invalid_order_quantity_is_rejected(int quantity)
    {
        var p = await Product();
        await Assert.ThrowsAsync<BusinessException>(() => Service().CreateOrderAsync("Cliente", [new(p.Id, quantity)], Guid.NewGuid()));
    }
    [Fact] public async Task Missing_product_rolls_back()
    {
        var p = await Product(); var key = Guid.NewGuid();
        await Assert.ThrowsAsync<BusinessException>(() => Service().CreateOrderAsync("Cliente", [new(p.Id, 1), new(int.MaxValue, 1)], key));
        await using var db = sql.CreateDbContext(); Assert.Equal(10, (await db.Products.FindAsync(p.Id))!.Stock);
        Assert.False(await db.Orders.AnyAsync(x => x.RequestId == key));
    }
    [Fact] public async Task Empty_customer_or_cart_is_rejected()
    {
        await Assert.ThrowsAsync<BusinessException>(() => Service().CreateOrderAsync("", [new(1, 1)], Guid.NewGuid()));
        await Assert.ThrowsAsync<BusinessException>(() => Service().CreateOrderAsync("Cliente", [], Guid.NewGuid()));
    }
    [Fact] public async Task Snapshot_keeps_original_price_and_name()
    {
        var p = await Product(); var id = await Service().CreateOrderAsync("Cliente", [new(p.Id, 1)], Guid.NewGuid());
        await using var db = sql.CreateDbContext(); var fresh = await db.Products.AsNoTracking().SingleAsync(x => x.Id == p.Id);
        fresh.Price = 99; fresh.Name = "Nombre nuevo"; await Service().SaveProductAsync(fresh);
        var line = await db.OrderLines.SingleAsync(x => x.OrderId == id);
        Assert.Equal(p.Price, line.UnitPrice); Assert.Equal(p.Name, line.ProductName);
    }
    [Fact] public async Task Negative_adjustment_cannot_overdraw()
    {
        var p = await Product(2);
        await Assert.ThrowsAsync<BusinessException>(() => Service().AdjustStockAsync(p.Id, -3, "Salida"));
        await using var db = sql.CreateDbContext(); Assert.Equal(2, (await db.Products.FindAsync(p.Id))!.Stock);
    }
    [Fact] public async Task Adjustment_creates_audit_entry()
    {
        var p = await Product(); await Service("Operator").AdjustStockAsync(p.Id, 5, "Reposición");
        await using var db = sql.CreateDbContext(); Assert.Equal(15, (await db.Products.FindAsync(p.Id))!.Stock);
        var m = await db.StockMovements.Where(x => x.ProductId == p.Id).OrderByDescending(x => x.Id).FirstAsync();
        Assert.Equal(5, m.Quantity); Assert.Equal("Reposición", m.Reason); Assert.Equal(15, m.Balance);
    }
    [Fact] public async Task Viewer_cannot_mutate_any_inventory_resource()
    {
        var viewer = Service("Viewer");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => viewer.SaveProductAsync(new()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => viewer.SaveCategoryAsync(new()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => viewer.DeleteProductAsync(1));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => viewer.DeleteCategoryAsync(1));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => viewer.AdjustStockAsync(1, 1, "test"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => viewer.CreateOrderAsync("Cliente", [new(1, 1)], Guid.NewGuid()));
        Assert.NotNull(await viewer.ProductsAsync());
    }
    [Fact] public async Task Anonymous_cannot_read()
    { await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new InventoryService(sql, new FakeActor(null)).ProductsAsync()); }
    [Fact] public async Task Operator_cannot_manage_categories_or_delete_products()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service("Operator").SaveCategoryAsync(new()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service("Operator").DeleteProductAsync(1));
    }
    [Fact] public async Task Duplicate_sku_is_rejected()
    {
        var p = await Product();
        await Assert.ThrowsAsync<BusinessException>(() => Service().SaveProductAsync(new Product { Sku = p.Sku.ToLowerInvariant(), Name = "Otro", Price = 1, CategoryId = p.CategoryId }));
    }
    [Fact] public async Task Historical_product_and_used_category_cannot_be_deleted()
    {
        var p = await Product();
        await Assert.ThrowsAsync<BusinessException>(() => Service().DeleteProductAsync(p.Id));
        await Assert.ThrowsAsync<BusinessException>(() => Service().DeleteCategoryAsync(p.CategoryId));
    }
    [Fact] public async Task Zero_stock_product_and_empty_category_can_be_deleted()
    {
        var p = await Product(0); await Service().DeleteProductAsync(p.Id); await Service().DeleteCategoryAsync(p.CategoryId);
        await using var db = sql.CreateDbContext(); Assert.Null(await db.Products.FindAsync(p.Id)); Assert.Null(await db.Categories.FindAsync(p.CategoryId));
    }
    [Fact] public async Task Editing_catalog_cannot_silently_change_stock()
    {
        var p = await Product(); p.Stock = 999; p.Name = "Editado"; await Service().SaveProductAsync(p);
        await using var db = sql.CreateDbContext(); var saved = await db.Products.FindAsync(p.Id);
        Assert.Equal(10, saved!.Stock); Assert.Equal("Editado", saved.Name);
    }
    [Fact] public async Task Stale_edit_is_rejected_by_rowversion()
    {
        var p = await Product(); await Service().AdjustStockAsync(p.Id, 1, "Reposición");
        p.Name = "Obsoleto"; await Assert.ThrowsAsync<BusinessException>(() => Service().SaveProductAsync(p));
        await using var db = sql.CreateDbContext(); Assert.NotEqual("Obsoleto", (await db.Products.FindAsync(p.Id))!.Name);
    }
    [Fact] public async Task Concurrent_orders_never_oversell()
    {
        var p = await Product(5);
        async Task<bool> TrySell() {
            try { await Service().CreateOrderAsync("Concurrente", [new(p.Id, 4)], Guid.NewGuid()); return true; }
            catch (BusinessException) { return false; }
            catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 1205) { return false; }
        }
        var results = await Task.WhenAll(TrySell(), TrySell());
        Assert.Single(results, x => x);
        await using var db = sql.CreateDbContext(); Assert.Equal(1, (await db.Products.FindAsync(p.Id))!.Stock);
        Assert.Single(await db.OrderLines.Where(x => x.ProductId == p.Id).ToListAsync());
    }
}

internal sealed class FakeActor(string? role) : IActor
{
    public Task<ClaimsPrincipal> GetUserAsync() => Task.FromResult(role == null ? new ClaimsPrincipal(new ClaimsIdentity()) :
        new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "tester"), new Claim(ClaimTypes.NameIdentifier, "tester-id"), new Claim(ClaimTypes.Role, role)], "Test")));
}
