# ── Stage 1: Restore ──────────────────────────────────────────────────────────
# Copy only .csproj files first so Docker can cache the restore layer.
# A code change won't invalidate this cache — only a dependency change will.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS restore
WORKDIR /src

COPY ["src/NexusPM.Domain/NexusPM.Domain.csproj",           "src/NexusPM.Domain/"]
COPY ["src/NexusPM.Application/NexusPM.Application.csproj", "src/NexusPM.Application/"]
COPY ["src/NexusPM.Infrastructure/NexusPM.Infrastructure.csproj", "src/NexusPM.Infrastructure/"]
COPY ["src/NexusPM.API/NexusPM.API.csproj",                 "src/NexusPM.API/"]

RUN dotnet restore "src/NexusPM.API/NexusPM.API.csproj" \
    --runtime linux-x64

# ── Stage 2: Build ────────────────────────────────────────────────────────────
FROM restore AS build
WORKDIR /src

COPY . .

RUN dotnet build "src/NexusPM.API/NexusPM.API.csproj" \
    --configuration Release \
    --no-restore \
    --runtime linux-x64 \
    -o /app/build

# ── Stage 3: Publish ──────────────────────────────────────────────────────────
FROM build AS publish

RUN dotnet publish "src/NexusPM.API/NexusPM.API.csproj" \
    --configuration Release \
    --no-restore \
    --runtime linux-x64 \
    --self-contained false \
    -p:UseAppHost=false \
    -o /app/publish

# ── Stage 4: Final runtime image ──────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

# Security: create non-root user
RUN groupadd --system appgroup \
 && useradd  --system --gid appgroup --no-create-home appuser

WORKDIR /app

# Expose HTTP only — TLS is terminated at Azure Front Door / App Service
EXPOSE 8080

# Copy published output
COPY --from=publish /app/publish .

# Set ownership to non-root user
RUN chown -R appuser:appgroup /app

USER appuser

# Liveness probe target
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
    CMD wget --no-verbose --tries=1 --spider http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "NexusPM.API.dll"]
