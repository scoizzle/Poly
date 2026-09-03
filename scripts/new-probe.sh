#!/usr/bin/env bash
# Scaffolds a new probe domain. Usage: scripts/new-probe.sh <name>
# Creates docs/probes/<name>/<name>.poly with a minimal template.
set -euo pipefail
cd "$(dirname "$0")/.."

NAME="${1:?usage: scripts/new-probe.sh <name>}"
DIR="docs/probes/$NAME"
[ -e "$DIR" ] && { echo "probe already exists: $DIR" >&2; exit 1; }
mkdir -p "$DIR"

cat > "$DIR/$NAME.poly" <<EOF
domain ${NAME}

Placeholder: entity {
  Name: Text required
}
EOF

echo "created $DIR/$NAME.poly"
echo "next: edit the domain, then run scripts/run-probe.sh $DIR/$NAME.poly"
