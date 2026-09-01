#!/usr/bin/env bash
# Idempotent Cloud Agent bootstrap for the Poly workspace.
# Ensures the pinned .NET 10 SDK (see global.json) is present, then restores packages.
set -euo pipefail

DOTNET_INSTALL_DIR=/usr/share/dotnet

if ! command -v dotnet >/dev/null 2>&1; then
  installer="$(mktemp)"
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$installer"
  sudo bash "$installer" --channel 10.0 --version latest --install-dir "$DOTNET_INSTALL_DIR"
  sudo ln -sf "$DOTNET_INSTALL_DIR/dotnet" /usr/local/bin/dotnet
  rm -f "$installer"
fi

export DOTNET_ROOT="${DOTNET_ROOT:-$DOTNET_INSTALL_DIR}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

# NuGetAudit disabled to match CI: NU1903 on SQLitePCLRaw.lib.e_sqlite3 is
# warning-as-error on the default audit path. Do not weaken NuGet audit elsewhere.
dotnet restore Poly.slnx -p:NuGetAudit=false
