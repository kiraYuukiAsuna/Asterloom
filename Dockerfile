# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json ./
COPY Backend/ Backend/
COPY Proto/ Proto/

RUN dotnet restore Backend/Asterloom.Server/Asterloom.Server.csproj
RUN dotnet publish Backend/Asterloom.Server/Asterloom.Server.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false
RUN dotnet publish Backend/Tools/Asterloom.Migrations/Asterloom.Migrations.csproj \
    --configuration Release \
    --output /app/migrations \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0

EXPOSE 8080

COPY --from=build --chown=$APP_UID:$APP_UID /app/publish ./
COPY --from=build --chown=$APP_UID:$APP_UID /app/migrations ./migrations/

USER $APP_UID
ENTRYPOINT ["dotnet", "Asterloom.Server.dll"]
