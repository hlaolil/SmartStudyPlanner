#!/usr/bin/env bash
# Vercel's build image has no .NET SDK, so install it into the build cache directory.
set -euo pipefail
DOTNET_DIR="$HOME/.dotnet"
if [ ! -x "$DOTNET_DIR/dotnet" ]; then
  curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  bash /tmp/dotnet-install.sh --channel 10.0 --install-dir "$DOTNET_DIR"
fi
"$DOTNET_DIR/dotnet" --version
