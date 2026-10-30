# Lan-Monitor

Monitor de red para terminal (TUI, .NET 10 + Terminal.Gui v2). Corre **sin sudo**.

| Tecla | Acción |
|---|---|
| F1–F7 | Dashboard · Ping · Velocidad · Dispositivos · Alertas · Ajustes · Salud del módem |
| r | Escanear la red ahora |
| s | Test de velocidad (Cloudflare) |
| q | Salir |
| Dispositivos: k / n / Enter / `/` / o / Supr | conocido · apodo · detalle (notas, puertos, Wake-on-LAN) · filtrar · solo online · olvidar |
| Ping: w · Alertas: f | cambiar ventana · cambiar filtro |
| Ajustes: Enter / e | editar · exportar CSV a `~/lan-monitor-export/` |

Datos: `~/.local/share/lan-monitor/lanmonitor.db` · Config: `~/.config/lan-monitor/config.json`

## Salud del módem (F7)
Evalúa cada 30 s si conviene reiniciar el módem y avisa (alerta + notificación, máx. 1 vez cada 2 h). Señales:
corte con el router vivo >2 min · 3+ cortes en 3 h · pérdida/jitter sostenidos hacia internet · latencia ≥2–3× su
normal de 24 h · tests de velocidad a menos de la mitad de lo habitual. Si la inestabilidad ya está entre tu equipo y
el router, lo atribuye al WiFi y **no** recomienda reiniciar el módem.

## MAC privadas
Celulares y laptops modernos rotan su MAC por red (⚑). Si reaparece con otra MAC privada y el mismo hostname, se
trata como el mismo dispositivo (conserva apodo e historial). Los desconocidos con MAC privada inactivos más allá de
la retención se limpian solos.

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
