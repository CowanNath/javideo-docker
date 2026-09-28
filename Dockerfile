# syntax=docker/dockerfile:1

# ---------- Stage 1: build the web SPA ----------
FROM node:20-alpine AS web
WORKDIR /web
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY tsconfig.json vite.config.ts uno.config.ts index.html ./
COPY src ./src
RUN npm run build

# ---------- Stage 2: build the .NET worker (framework-dependent) ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS worker
WORKDIR /src
COPY worker/ ./
RUN dotnet publish Javideo.Worker.csproj -c Release -o /publish

# ---------- Stage 3: runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=worker /publish ./
COPY --from=web /web/dist ./wwwroot

# Worker listens on 8080 (serving both the SPA and /api) and keeps all
# mutable state (library.db, avatar/preview caches, cloud-drive metadata
# cache) under /data — mount a volume there.
ENV ASPNETCORE_URLS=http://+:8080 \
    Javideo__DataDir=/data
VOLUME /data
EXPOSE 8080

ENTRYPOINT ["dotnet", "javideo-worker.dll"]
