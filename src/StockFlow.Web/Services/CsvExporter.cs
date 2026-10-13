using System.Globalization;
using StockFlow.Web.Domain;

namespace StockFlow.Web.Services;

public static class CsvExporter
{
    // Evita fórmulas al abrir campos de texto no confiables en Excel.
    public static string Cell(string value)
    {
        if (value.TrimStart().StartsWith('=') || value.TrimStart().StartsWith('+') || value.TrimStart().StartsWith('-') || value.TrimStart().StartsWith('@')) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
    public static string Inventory(IEnumerable<Product> products) => "SKU;Producto;Categoria;Precio;Stock;Minimo;Valor\r\n" +
        string.Join("\r\n", products.Select(p => string.Join(";", Cell(p.Sku), Cell(p.Name), Cell(p.Category?.Name ?? ""),
            p.Price.ToString(CultureInfo.InvariantCulture), p.Stock, p.MinimumStock, (p.Price * p.Stock).ToString(CultureInfo.InvariantCulture))));
    public static string Sales(IEnumerable<Order> orders) => "Pedido;FechaUTC;Cliente;Usuario;Total\r\n" +
        string.Join("\r\n", orders.Select(o => string.Join(";", o.Id, o.CreatedAt.ToString("O"), Cell(o.Customer), Cell(o.CreatedBy), o.Total.ToString(CultureInfo.InvariantCulture))));
}
