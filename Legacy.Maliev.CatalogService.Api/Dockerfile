# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
ENV GITHUB_ACTIONS=false
COPY nuget.config .
COPY .dependencies/ .dependencies/
COPY Legacy.Maliev.CatalogService.Api/Legacy.Maliev.CatalogService.Api.csproj Legacy.Maliev.CatalogService.Api/
COPY Legacy.Maliev.CatalogService.Application/Legacy.Maliev.CatalogService.Application.csproj Legacy.Maliev.CatalogService.Application/
COPY Legacy.Maliev.CatalogService.Domain/Legacy.Maliev.CatalogService.Domain.csproj Legacy.Maliev.CatalogService.Domain/
COPY Legacy.Maliev.CatalogService.Data/Legacy.Maliev.CatalogService.Data.csproj Legacy.Maliev.CatalogService.Data/
RUN dotnet restore Legacy.Maliev.CatalogService.Api/Legacy.Maliev.CatalogService.Api.csproj \
    -p:MalievWorkspaceRoot=/src/.dependencies
COPY . .
RUN dotnet publish Legacy.Maliev.CatalogService.Api/Legacy.Maliev.CatalogService.Api.csproj \
    --configuration Release --no-restore --output /app /p:UseAppHost=false \
    -p:MalievWorkspaceRoot=/src/.dependencies

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build --chown=$APP_UID:$APP_UID /app .
USER $APP_UID
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "Legacy.Maliev.CatalogService.Api.dll"]
