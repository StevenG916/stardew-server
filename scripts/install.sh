#!/bin/bash
# Stardew Valley Dedicated Server - Linux Dependency Installer
# Installs required system packages for running the server headlessly

set -e

echo "=== Stardew Valley Dedicated Server - Dependency Installer ==="
echo ""

# Detect package manager
if command -v apt-get &>/dev/null; then
    PKG_MANAGER="apt-get"
    INSTALL_CMD="sudo apt-get install -y"
elif command -v dnf &>/dev/null; then
    PKG_MANAGER="dnf"
    INSTALL_CMD="sudo dnf install -y"
elif command -v yum &>/dev/null; then
    PKG_MANAGER="yum"
    INSTALL_CMD="sudo yum install -y"
elif command -v pacman &>/dev/null; then
    PKG_MANAGER="pacman"
    INSTALL_CMD="sudo pacman -S --noconfirm"
else
    echo "ERROR: No supported package manager found (apt-get, dnf, yum, pacman)"
    exit 1
fi

echo "Detected package manager: $PKG_MANAGER"
echo ""

# Update package list
echo "Updating package list..."
case $PKG_MANAGER in
    apt-get) sudo apt-get update -qq ;;
    dnf|yum) ;; # These update automatically
    pacman) sudo pacman -Sy ;;
esac

# Install required packages
echo "Installing dependencies..."

# Xvfb - Virtual framebuffer for headless rendering
echo "  -> Xvfb (virtual display)"
case $PKG_MANAGER in
    apt-get) $INSTALL_CMD xvfb ;;
    dnf|yum) $INSTALL_CMD xorg-x11-server-Xvfb ;;
    pacman) $INSTALL_CMD xorg-server-xvfb ;;
esac

# .NET 6 Runtime (for SMAPI)
echo "  -> .NET 6 Runtime"
case $PKG_MANAGER in
    apt-get) $INSTALL_CMD dotnet-runtime-6.0 ;;
    dnf) $INSTALL_CMD dotnet-runtime-6.0 ;;
    yum)
        echo "    Note: .NET 6 may need Microsoft's repo for older distros"
        $INSTALL_CMD dotnet-runtime-6.0 || echo "    -> Install manually from https://dotnet.microsoft.com/download"
        ;;
    pacman) $INSTALL_CMD dotnet-runtime-6.0 ;;
esac

# OpenGL libraries (MonoGame dependency)
echo "  -> OpenGL libraries"
case $PKG_MANAGER in
    apt-get) $INSTALL_CMD libgl1-mesa-glx libgl1-mesa-dri ;;
    dnf|yum) $INSTALL_CMD mesa-libGL mesa-dri-drivers ;;
    pacman) $INSTALL_CMD mesa ;;
esac

# Additional libraries the game may need
echo "  -> Additional game dependencies"
case $PKG_MANAGER in
    apt-get) $INSTALL_CMD libopenal1 libsdl2-2.0-0 ;;
    dnf|yum) $INSTALL_CMD openal-soft SDL2 ;;
    pacman) $INSTALL_CMD openal sdl2 ;;
esac

echo ""
echo "=== Installation Complete ==="
echo ""
echo "Next steps:"
echo "  1. Install Stardew Valley (via SteamCMD or copy game files)"
echo "  2. Install SMAPI (download from https://smapi.io)"
echo "  3. Copy the StardewDedicatedServer mod to Mods/ folder"
echo "  4. Run: ./scripts/start.sh"
echo ""
