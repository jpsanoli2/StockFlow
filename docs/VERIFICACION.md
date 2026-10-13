# Verificación de la entrega

Fecha: 12 de octubre de 2026. Entorno de validación: Windows, SDK .NET 10.0.301, runtime .NET 10.0.9 y SQL Server Express LocalDB.

| Comprobación | Resultado |
|---|---|
| Restauración desde NuGet.org | Correcta |
| Build Release de la solución | 0 errores, 0 advertencias |
| xUnit contra SQL Server y exportador CSV | 30 aprobados, 0 fallidos, 0 omitidos |
| Migraciones aplicadas sobre bases nuevas | Correcto |
| Comparación modelo/migraciones | Sin cambios pendientes |
| Publicación Release | Correcta |
| Inicio de la aplicación con LocalDB | Correcto |
| Inicio de sesión de administrador en navegador | Correcto |
| Dashboard y alerta de reposición | Correcto |
| Alta de categoría desde navegador | Correcta |
| Alta y edición de producto desde navegador | Correctas |
| Pedido de dos unidades | Confirmado; stock de teclado pasó de 24 a 22 |
| Movimiento de venta e historial | Salida de 2 unidades, saldo 22, pedido relacionado |
| Reporte de ventas | Un pedido de 159,80, dos unidades vendidas |
| Descarga CSV desde navegador | Correcta; contenido verificado |
| Vista móvil y escritorio | Revisadas; tablas con desplazamiento horizontal |

Los tests verifican totales y auditoría, rollback por falta de stock o producto inexistente, cantidades inválidas, agrupación de líneas repetidas, idempotencia, conservación del precio y nombre históricos, ajustes, permisos de Viewer/Operator/Admin, SKU duplicado, restricciones de borrado, stock protegido al editar y `rowversion`. Dos ventas concurrentes sobre 5 unidades, intentando vender 4 cada una, confirman sólo una venta y dejan saldo 1. Los tests de Identity verifican hashes de contraseña, roles, prohibición de cambiar el propio rol y rechazo de una sesión anterior a un cambio de permisos. Los tests CSV cubren escape de comillas y neutralización de fórmulas.

El resultado original de xUnit se conservó en la entrega local. Los logs de esa máquina se excluyen del repositorio público; nuevas ejecuciones generan sus propios resultados. Las capturas corresponden a una sesión de demostración local; los cambios realizados durante la revisión no se incluyen como base de datos en el ZIP. En una instalación nueva se cargan los tres productos iniciales del seed.

Docker y el workflow de GitHub Actions se incluyen como configuración reproducible, pero no se ejecutaron en esta máquina (Docker no está instalado). La validación efectiva se hizo directamente con .NET y SQL Server LocalDB. El envío de correo externo no está configurado: véase la sección de Identity en el README.

Comandos ejecutados desde la raíz de StockFlow:

```powershell
dotnet build StockFlow.sln -c Release
dotnet ef migrations has-pending-model-changes --project src/StockFlow.Web --configuration Release --no-build
dotnet test StockFlow.sln -c Release --no-build --logger trx
dotnet publish src/StockFlow.Web -c Release --no-build
```
