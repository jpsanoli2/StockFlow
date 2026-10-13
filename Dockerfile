FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY NuGet.Config ./
COPY src/StockFlow.Web/StockFlow.Web.csproj src/StockFlow.Web/
RUN dotnet restore src/StockFlow.Web/StockFlow.Web.csproj
COPY src/ src/
RUN dotnet publish src/StockFlow.Web/StockFlow.Web.csproj -c Release -o /app --no-restore
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "StockFlow.Web.dll"]
