#!/usr/bin/env bash
set -euo pipefail

repo_root="$(git rev-parse --show-toplevel)"
cd "$repo_root"

src="$repo_root/githooks/pre-push"
dst="$repo_root/.git/hooks/pre-push"

if [[ ! -f "$src" ]]; then
  echo "Hook fonte não encontrado: $src" >&2
  exit 1
fi

mkdir -p "$repo_root/.git/hooks"
cp "$src" "$dst"
chmod +x "$dst" || true

echo "Instalado: $dst"

