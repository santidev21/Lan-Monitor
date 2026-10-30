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

## Instalar como comando
    git clone <repo> && cd Lan-Monitor
    ./install.sh              # comando: lan-monitor
    ./install.sh monitor-lan  # o el nombre que prefieras
    ./install.sh --uninstall

Requiere el SDK de .NET 10 solo para compilar. Instala en `~/.local/lib/lan-monitor` y crea el comando en `~/.local/bin`.

## Tests
    dotnet test
