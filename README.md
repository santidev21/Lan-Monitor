# Lan-Monitor

Monitor de red para terminal (TUI, .NET 10 + Terminal.Gui v2). Corre **sin sudo**.

| Tecla | Acción |
|---|---|
| F1–F6 | Dashboard · Ping · Velocidad · Dispositivos · Alertas · Ajustes |
| r | Escanear la red ahora |
| s | Test de velocidad (Cloudflare) |
| q | Salir |
| Dispositivos: k / n / Enter / `/` / o / Supr | conocido · apodo · detalle (notas, puertos, Wake-on-LAN) · filtrar · solo online · olvidar |
| Ping: w · Alertas: f | cambiar ventana · cambiar filtro |
| Ajustes: Enter / e | editar · exportar CSV a `~/lan-monitor-export/` |

Datos: `~/.local/share/lan-monitor/lanmonitor.db` · Config: `~/.config/lan-monitor/config.json`

## Ejecutar
    dotnet run --project src/LanMonitor.App

## Binario único
    dotnet publish src/LanMonitor.App -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o publish
    ./publish/lan-monitor
    # opcional: ln -s "$PWD/publish/lan-monitor" ~/.local/bin/lan-monitor

## Tests
    dotnet test
