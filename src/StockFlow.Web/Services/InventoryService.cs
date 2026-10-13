using System.ComponentModel.DataAnnotations;
using System.Data;
using Microsoft.EntityFrameworkCore;
using StockFlow.Web.Data;
using StockFlow.Web.Domain;

namespace StockFlow.Web.Services;

public sealed class InventoryService(IDbContextFactory<ApplicationDbContext> factory, IActor actor)
{
    private async Task<string> RequireAsync(bool write = false, bool admin = false)
    {
        var user = await actor.GetUserAsync();
        if (user.Identity?.IsAuthenticated != true ||
            !(user.IsInRole("Admin") || (!admin && user.IsInRole("Operator")) || (!write && !admin && user.IsInRole("Viewer"))))
            throw new UnauthorizedAccessException("No tenés permisos para esta operación.");
        return user.Identity.Name ?? "usuario";
    }

    private static void Validate(object model) => Validator.ValidateObject(model, new ValidationContext(model), true);

    public async Task<List<Product>> ProductsAsync()
    {
        await RequireAsync();
        await using var db = await factory.CreateDbContextAsync();
        return await db.Products.AsNoTracking().Include(x => x.Category).OrderBy(x => x.Name).ToListAsync();
    }
    public async Task<List<Category>> CategoriesAsync()
    {
        await RequireAsync();
        await using var db = await factory.CreateDbContextAsync();
        return await db.Categories.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
    }
    public async Task<List<Order>> OrdersAsync()
    {
        await RequireAsync();
        await using var db = await factory.CreateDbContextAsync();
        return await db.Orders.AsNoTracking().Include(x => x.Lines).OrderByDescending(x => x.CreatedAt).ToListAsync();
    }
    public async Task<List<StockMovement>> MovementsAsync()
    {
        await RequireAsync();
        await using var db = await factory.CreateDbContextAsync();
        return await db.StockMovements.AsNoTracking().Include(x => x.Product).OrderByDescending(x => x.Id).ToListAsync();
    }

    public async Task SaveCategoryAsync(Category input)
    {
        await RequireAsync(write: true, admin: true);
        input.Name = input.Name.Trim(); Validate(input);
        await using var db = await factory.CreateDbContextAsync();
        if (await db.Categories.AnyAsync(x => x.Name == input.Name && x.Id != input.Id))
            throw new BusinessException("Ya existe una categoría con ese nombre.");
        var entity = input.Id == 0 ? new Category() : await db.Categories.FindAsync(input.Id) ?? throw new BusinessException("Categoría inexistente.");
        entity.Name = input.Name; entity.Description = input.Description;
        if (input.Id == 0) db.Add(entity);
        await SaveAsync(db);
    }
    public async Task DeleteCategoryAsync(int id)
    {
        await RequireAsync(write: true, admin: true);
        await using var db = await factory.CreateDbContextAsync();
        if (await db.Products.AnyAsync(x => x.CategoryId == id)) throw new BusinessException("La categoría tiene productos asociados.");
        var item = await db.Categories.FindAsync(id) ?? throw new BusinessException("Categoría inexistente.");
        db.Remove(item); await SaveAsync(db);
    }
    public async Task SaveProductAsync(Product input)
    {
        var user = await RequireAsync(write: true);
        input.Sku = input.Sku.Trim().ToUpperInvariant(); input.Name = input.Name.Trim(); Validate(input);
        await using var db = await factory.CreateDbContextAsync();
        if (!await db.Categories.AnyAsync(x => x.Id == input.CategoryId)) throw new BusinessException("Seleccioná una categoría válida.");
        if (await db.Products.AnyAsync(x => x.Sku == input.Sku && x.Id != input.Id)) throw new BusinessException("El SKU ya existe.");
        var entity = input.Id == 0 ? new Product() : await db.Products.FindAsync(input.Id) ?? throw new BusinessException("Producto inexistente.");
        if (input.Id != 0) db.Entry(entity).Property(x => x.Version).OriginalValue = input.Version;
        entity.Sku = input.Sku; entity.Name = input.Name; entity.Price = decimal.Round(input.Price, 2);
        entity.CategoryId = input.CategoryId; entity.MinimumStock = input.MinimumStock;
        if (input.Id == 0)
        {
            entity.Stock = input.Stock; db.Add(entity);
            if (input.Stock > 0) db.StockMovements.Add(new StockMovement { Product = entity, Quantity = input.Stock, Balance = input.Stock, Reason = "Stock inicial", CreatedBy = user });
        }
        await SaveAsync(db);
    }
    public async Task DeleteProductAsync(int id)
    {
        await RequireAsync(write: true, admin: true);
        await using var db = await factory.CreateDbContextAsync();
        if (await db.StockMovements.AnyAsync(x => x.ProductId == id) || await db.OrderLines.AnyAsync(x => x.ProductId == id))
            throw new BusinessException("No se puede eliminar un producto con historial de movimientos o pedidos.");
        var entity = await db.Products.FindAsync(id) ?? throw new BusinessException("Producto inexistente.");
        db.Remove(entity); await SaveAsync(db);
    }
    public async Task AdjustStockAsync(int productId, int quantity, string reason)
    {
        var user = await RequireAsync(write: true);
        if (quantity == 0 || Math.Abs((long)quantity) > 1000000 || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 200)
            throw new BusinessException("Ingresá una cantidad distinta de cero y un motivo de hasta 200 caracteres.");
        await using var db = await factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var product = await db.Products.FindAsync(productId) ?? throw new BusinessException("Producto inexistente.");
        var balance = (long)product.Stock + quantity;
        if (balance < 0 || balance > 1000000) throw new BusinessException("El stock resultante debe estar entre 0 y 1.000.000.");
        product.Stock = (int)balance;
        db.StockMovements.Add(new StockMovement { ProductId = productId, Quantity = quantity, Balance = product.Stock, Reason = reason.Trim(), CreatedBy = user });
        await SaveAsync(db); await tx.CommitAsync();
    }

    public async Task<int> CreateOrderAsync(string customer, IReadOnlyCollection<OrderItem> items, Guid requestId)
    {
        var user = await RequireAsync(write: true);
        if (string.IsNullOrWhiteSpace(customer) || customer.Trim().Length > 120 || items.Count == 0 || items.Count > 100 || requestId == Guid.Empty)
            throw new BusinessException("Ingresá un cliente y entre 1 y 100 líneas.");
        if (items.Any(x => x.Quantity <= 0 || x.Quantity > 1000000)) throw new BusinessException("Todas las cantidades deben ser positivas.");
        // Serializable y rowversion protegen frente a pedidos concurrentes. Todo se confirma junto.
        await using var db = await factory.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var existing = await db.Orders.SingleOrDefaultAsync(x => x.RequestId == requestId);
        if (existing != null) return existing.Id;
        var order = new Order { Customer = customer.Trim(), CreatedBy = user, RequestId = requestId };
        foreach (var group in items.GroupBy(x => x.ProductId).OrderBy(x => x.Key))
        {
            var quantity = group.Sum(x => (long)x.Quantity);
            var product = await db.Products.FindAsync(group.Key) ?? throw new BusinessException("Producto inexistente.");
            if (quantity > product.Stock) throw new BusinessException($"Stock insuficiente para {product.Name}: disponible {product.Stock}.");
            product.Stock -= (int)quantity;
            order.Lines.Add(new OrderLine { ProductId = product.Id, ProductName = product.Name, Sku = product.Sku, Quantity = (int)quantity, UnitPrice = product.Price });
            db.StockMovements.Add(new StockMovement { ProductId = product.Id, Quantity = -(int)quantity, Balance = product.Stock, Reason = "Venta", CreatedBy = user, Order = order });
        }
        order.Total = order.Lines.Sum(x => x.Subtotal);
        db.Orders.Add(order);
        await SaveAsync(db); await tx.CommitAsync(); return order.Id;
    }
    private static async Task SaveAsync(ApplicationDbContext db)
    {
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { throw new BusinessException("El registro cambió. Recargá los datos y reintentá."); }
        catch (DbUpdateException) { throw new BusinessException("No se pudo guardar: revisá duplicados o referencias asociadas."); }
    }
}
