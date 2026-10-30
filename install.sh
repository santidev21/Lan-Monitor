#!/usr/bin/env bash
# Instala Lan-Monitor como comando global.   Uso: ./install.sh [nombre-del-comando]   |   ./install.sh --uninstall
set -euo pipefail

NAME="${1:-lan-monitor}"
LIB_DIR="${LAN_MONITOR_LIB_DIR:-$HOME/.local/lib/lan-monitor}"
BIN_DIR="${LAN_MONITOR_BIN_DIR:-$HOME/.local/bin}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if [[ "$NAME" == "--uninstall" ]]; then
  rm -rf "$LIB_DIR"
  # Borra solo wrappers creados por este instalador.
  grep -l "lan-monitor-wrapper" "$BIN_DIR"/* 2>/dev/null | xargs -r rm -f
  echo "Desinstalado. (Tus datos en ~/.local/share/lan-monitor y ~/.config/lan-monitor siguen ahí.)"
  exit 0
fi

command -v dotnet >/dev/null || { echo "Error: no encuentro 'dotnet' (SDK 10)."; exit 1; }

echo "==> Compilando (Release, binario único)..."
dotnet publish "$ROOT/src/LanMonitor.App" -c Release -r linux-x64 --self-contained \
  -p:PublishSingleFile=true -o "$ROOT/publish" --nologo -v m -nodeReuse:false -p:UseSharedCompilation=false

echo "==> Instalando en $LIB_DIR"
rm -rf "$LIB_DIR" && mkdir -p "$LIB_DIR" "$BIN_DIR"
cp -r "$ROOT/publish/." "$LIB_DIR/"

# Wrapper en vez de symlink: el binario necesita encontrar sus librerías nativas junto a él.
cat > "$BIN_DIR/$NAME" <<WRAP
#!/usr/bin/env bash
# lan-monitor-wrapper
exec "$LIB_DIR/lan-monitor" "\$@"
WRAP
chmod +x "$BIN_DIR/$NAME"

echo "==> Listo. Ejecuta:  $NAME"
case ":$PATH:" in *":$BIN_DIR:"*) ;; *) echo "Aviso: $BIN_DIR no está en tu PATH; agrégalo a tu ~/.zshrc.";; esac
