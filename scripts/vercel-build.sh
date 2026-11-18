#!/usr/bin/env bash
# Publishes the Blazor WebAssembly app as static files into publish/wwwroot.
set -euo pipefail
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
dotnet publish SmartStudyPlanner.csproj -c Release -o publish
