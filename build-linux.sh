#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
#  build-linux.sh – Build SoftPlc.AgvSimulator on Ubuntu (no Docker needed)
# ─────────────────────────────────────────────────────────────────────────────
#  Prerequisites:
#    sudo apt install -y dotnet-sdk-8.0 g++ make p7zip-full curl
#
#  Usage:
#    chmod +x build-linux.sh
#    ./build-linux.sh
#
#  Output:
#    publish/linux-x64/  (self-contained binary + libsnap7.so)
# ─────────────────────────────────────────────────────────────────────────────
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
cd "$SCRIPT_DIR"

PUBLISH_DIR="$SCRIPT_DIR/publish/linux-x64"
SNAP7_DIR="$SCRIPT_DIR/.snap7-build"

echo "=== Step 1: Build libsnap7.so ==="
if [ -f "$PUBLISH_DIR/libsnap7.so" ]; then
    echo "  libsnap7.so already exists, skipping build"
else
    mkdir -p "$SNAP7_DIR"
    if [ ! -d "$SNAP7_DIR/snap7-full-1.4.2" ]; then
        echo "  Downloading snap7 source..."
        curl -L -o "$SNAP7_DIR/snap7.7z" \
            "https://sourceforge.net/projects/snap7/files/1.4.2/snap7-full-1.4.2.7z/download"
        cd "$SNAP7_DIR"
        7z x snap7.7z
        rm snap7.7z
        cd "$SCRIPT_DIR"
    fi

    echo "  Compiling snap7 for x86_64-linux..."
    cd "$SNAP7_DIR/snap7-full-1.4.2/build/unix"
    make -f x86_64_linux.mk clean all
    cd "$SCRIPT_DIR"

    mkdir -p "$PUBLISH_DIR"
    cp "$SNAP7_DIR/snap7-full-1.4.2/build/bin/x86_64-linux/libsnap7.so" "$PUBLISH_DIR/"
    echo "  Built: $PUBLISH_DIR/libsnap7.so"
fi

echo ""
echo "=== Step 2: Publish .NET app ==="
dotnet publish src/SoftPlc.AgvSimulator/SoftPlc.AgvSimulator.csproj \
    -c Release -r linux-x64 --self-contained \
    -o "$PUBLISH_DIR"

# Remove Windows DLL (replaced by libsnap7.so)
rm -f "$PUBLISH_DIR/snap7.dll"

# Ensure libsnap7.so is present
if [ ! -f "$PUBLISH_DIR/libsnap7.so" ]; then
    cp "$SNAP7_DIR/snap7-full-1.4.2/build/bin/x86_64-linux/libsnap7.so" "$PUBLISH_DIR/"
fi

echo ""
echo "=== Build complete ==="
echo "Output: $PUBLISH_DIR/"
echo ""
echo "To run on Ubuntu:"
echo "  cd $PUBLISH_DIR"
echo "  chmod +x SoftPlc.AgvSimulator"
echo "  sudo ./SoftPlc.AgvSimulator   # sudo needed for port 102"
echo ""
echo "Or to use port 1102 (no sudo):"
echo "  ./SoftPlc.AgvSimulator"
