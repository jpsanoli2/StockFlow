using Microsoft.AspNetCore.Components;
using StockFlow.Web.Services;

namespace StockFlow.Web.Components.Pages;

public abstract class InventoryPageBase : ComponentBase
{
    [Inject] protected InventoryService Inventory { get; set; } = default!;
    [Inject] private ILogger<InventoryPageBase> Logger { get; set; } = default!;
    protected string? Error;
    protected string? Success;
    protected bool Busy;
    protected async Task RunAsync(Func<Task> work, string? success = null)
    {
        if (Busy) return;
        Busy = true; Error = null; Success = null;
        try { await work(); Success = success; }
        catch (Exception ex) when (ex is Domain.BusinessException or System.ComponentModel.DataAnnotations.ValidationException or UnauthorizedAccessException)
        { Error = ex.Message; }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error al ejecutar una operación de inventario");
            Error = "No se pudo completar la operación. Recargá e intentá de nuevo; si persiste, revisá la conexión con SQL Server.";
        }
        finally { Busy = false; }
    }
}
