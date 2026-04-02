# Stardew Valley Dedicated Server - Docker Image
# Runs the full game client headlessly with Xvfb + SMAPI + DedicatedServer mod
#
# BUILD:  docker build -t stardew-server .
# RUN:    docker run -d -p 24642:24642/udp -v ./saves:/saves -v ./config:/config stardew-server
#
# NOTE: You must provide the game files. Stardew Valley is NOT included in this image.
# Mount your game directory to /game or use the GAME_DIR environment variable.

FROM debian:bookworm-slim

LABEL maintainer="StardewServer"
LABEL description="Stardew Valley Dedicated Multiplayer Server"

# Avoid interactive prompts during package installation
ENV DEBIAN_FRONTEND=noninteractive

# Install dependencies
RUN apt-get update && apt-get install -y --no-install-recommends \
    # Virtual display
    xvfb \
    # .NET 6 runtime
    dotnet-runtime-6.0 \
    # OpenGL / Mesa (MonoGame needs a GL context even in Xvfb)
    libgl1-mesa-glx \
    libgl1-mesa-dri \
    mesa-utils \
    # Audio (some games crash without audio device)
    pulseaudio \
    # SDL2 (MonoGame dependency)
    libsdl2-2.0-0 \
    # OpenAL (game audio library)
    libopenal1 \
    # Utilities
    procps \
    ca-certificates \
    && rm -rf /var/lib/apt/lists/*

# Create directories
RUN mkdir -p /game /saves /config /scripts

# Copy launch script
COPY ../scripts/start.sh /scripts/start.sh
RUN chmod +x /scripts/start.sh

# Environment
ENV GAME_DIR=/game
ENV DISPLAY=:99

# Configure PulseAudio for dummy output (prevents audio crashes)
RUN mkdir -p /root/.config/pulse && \
    echo "load-module module-null-sink" > /root/.config/pulse/default.pa && \
    echo "set-default-sink null" >> /root/.config/pulse/default.pa

# Game port
EXPOSE 24642/udp

# Health check - verify the game process is running
HEALTHCHECK --interval=30s --timeout=5s --start-period=60s --retries=3 \
    CMD pgrep -f "StardewModdingAPI" > /dev/null || exit 1

# Volumes for persistence
VOLUME ["/saves", "/config"]

# Entry point
ENTRYPOINT ["/scripts/start.sh"]
CMD ["/game"]
