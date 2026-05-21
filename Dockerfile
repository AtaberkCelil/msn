# ── Build stage ──────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project file and restore dependencies (cached layer)
COPY Server/Server.csproj ./Server/
RUN dotnet restore Server/Server.csproj

# Copy everything else and publish
COPY Server/ ./Server/
COPY Web/ ./Web/
RUN dotnet publish Server/Server.csproj -c Release -o /app/publish --no-restore

# ── Runtime stage ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Copy published output
COPY --from=build /app/publish .

# Copy web files next to the executable so HttpServer can serve them
COPY --from=build /src/Web ./Web

# Expose HTTP port (Railway will set PORT env var)
EXPOSE 8080
# Expose TCP port for the messenger protocol
EXPOSE 1863

# Avatar directory will be created at runtime
ENV AVATARS_DIR=/app/Avatars

ENTRYPOINT ["dotnet", "Server.dll"]
