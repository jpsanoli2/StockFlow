using System.ComponentModel.DataAnnotations;

namespace StockFlow.Web.Domain;

public class Category
{
    public int Id { get; set; }
    [Required, StringLength(80)] public string Name { get; set; } = "";
    [StringLength(300)] public string Description { get; set; } = "";
}

public class Product
{
    public int Id { get; set; }
    [Required, StringLength(40)] public string Sku { get; set; } = "";
    [Required, StringLength(120)] public string Name { get; set; } = "";
    [Range(0.01, 99999999)] public decimal Price { get; set; }
    [Range(0, 1000000)] public int Stock { get; set; }
    [Range(0, 1000000)] public int MinimumStock { get; set; } = 5;
    [Range(1, int.MaxValue)] public int CategoryId { get; set; }
    public Category? Category { get; set; }
    [Timestamp] public byte[] Version { get; set; } = [];
}

public class Order
{
    public int Id { get; set; }
    public Guid RequestId { get; set; }
    [Required, StringLength(120)] public string Customer { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = "";
    public decimal Total { get; set; }
    public List<OrderLine> Lines { get; set; } = [];
}

public class OrderLine
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public string ProductName { get; set; } = "";
    public string Sku { get; set; } = "";
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Subtotal => Quantity * UnitPrice;
}

public class StockMovement
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int Quantity { get; set; }
    public int Balance { get; set; }
    public string Reason { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public int? OrderId { get; set; }
    public Order? Order { get; set; }
}

public record OrderItem(int ProductId, int Quantity);
public class BusinessException(string message) : Exception(message);
