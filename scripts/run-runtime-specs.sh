#!/bin/bash
set -e

# 1. Build the compiler
echo "[1/3] Building compiler..."
dotnet build RobloxCS.CLI -c Debug

# 2. Transpile the runtime specifications from C# to Luau
echo "[2/3] Transpiling runtime specs..."
./RobloxCS.CLI/bin/Debug/net9.0/RobloxCS.CLI --project tests/runtime

# 3. Run the specs using Lune
echo "[3/3] Running specs via Lune..."
if ! command -v lune &> /dev/null; then
    echo "Error: 'lune' is not installed. Please install Lune to run runtime specs."
    exit 1
fi

lune run tests/runtime/run.lua
