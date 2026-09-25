FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY . .

FROM build AS gateway-publish
RUN dotnet publish src/UZLLM.Gateway.Api/UZLLM.Gateway.Api.csproj -c Release -o /publish /p:UseAppHost=false

FROM build AS management-publish
RUN dotnet publish src/UZLLM.Management.Api/UZLLM.Management.Api.csproj -c Release -o /publish /p:UseAppHost=false

FROM build AS worker-publish
RUN dotnet publish src/UZLLM.Worker/UZLLM.Worker.csproj -c Release -o /publish /p:UseAppHost=false

FROM build AS migrator-publish
RUN dotnet publish src/UZLLM.Migrator/UZLLM.Migrator.csproj -c Release -o /publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS gateway
WORKDIR /app
COPY --from=gateway-publish /publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "UZLLM.Gateway.Api.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS management
WORKDIR /app
COPY --from=management-publish /publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "UZLLM.Management.Api.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS worker
WORKDIR /app
COPY --from=worker-publish /publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "UZLLM.Worker.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS migrator
WORKDIR /app
COPY --from=migrator-publish /publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "UZLLM.Migrator.dll"]
