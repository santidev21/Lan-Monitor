# Uso fácil: docker compose run --rm lan-monitor  (ver compose.yaml)
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/LanMonitor.App/LanMonitor.App.csproj src/LanMonitor.App/
COPY src/LanMonitor.Core/LanMonitor.Core.csproj src/LanMonitor.Core/
RUN dotnet restore src/LanMonitor.App
COPY src/ src/
# Sin RID: el resultado es portable y sirve en x64, arm64 y arm.
RUN dotnet publish src/LanMonitor.App -c Release --no-restore -o /app --nologo

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .
# Config, base de datos y exportaciones quedan bajo /data (montar un volumen para no perderlos).
ENV HOME=/data
VOLUME /data
ENTRYPOINT ["dotnet", "lan-monitor.dll"]
