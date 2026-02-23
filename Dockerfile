# ─────────────────────────────────────────────────────────────────────────────
#  Multi-stage Dockerfile – Build SoftPlc.AgvSimulator for Ubuntu (linux-x64)
# ─────────────────────────────────────────────────────────────────────────────
#  Usage:
#    docker build -t softplc-agv .
#    docker run --rm -p 102:102 -p 15000:15000 softplc-agv
#
#  Or extract the published binary (no Docker runtime needed):
#    docker create --name tmp softplc-agv
#    docker cp tmp:/app ./publish-linux
#    docker rm tmp
# ─────────────────────────────────────────────────────────────────────────────

# ── Stage 1: Build snap7 native library (libsnap7.so) ────────────────────────
FROM ubuntu:22.04 AS snap7-build

RUN apt-get update && apt-get install -y --no-install-recommends \
        g++ make p7zip-full curl ca-certificates \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /snap7

# Download and extract snap7 source
RUN curl -L -o snap7.7z \
        "https://sourceforge.net/projects/snap7/files/1.4.2/snap7-full-1.4.2.7z/download" \
    && 7z x snap7.7z \
    && rm snap7.7z

# Build libsnap7.so for x86_64 Linux
WORKDIR /snap7/snap7-full-1.4.2/build/unix
RUN make -f x86_64_linux.mk all

# The compiled library is in ../bin/x86_64-linux/libsnap7.so
RUN ls -la ../bin/x86_64-linux/


# ── Stage 2: Build .NET application ─────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS dotnet-build

WORKDIR /src
COPY SoftPlc.slnx Directory.Build.targets ./
COPY src/ src/

# Publish for linux-x64 self-contained
RUN dotnet publish src/SoftPlc.AgvSimulator/SoftPlc.AgvSimulator.csproj \
        -c Release -r linux-x64 --self-contained \
        -o /publish \
    && rm -f /publish/snap7.dll


# ── Stage 3: Runtime image ──────────────────────────────────────────────────
FROM ubuntu:22.04 AS runtime

# Avalonia / X11 dependencies + ICU for .NET globalization
RUN apt-get update && apt-get install -y --no-install-recommends \
        libx11-6 libx11-xcb1 libxcb1 libxcursor1 libxrandr2 libxi6 \
        libxext6 libxfixes3 libxrender1 libfontconfig1 libfreetype6 \
        libicu70 libssl3 ca-certificates \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

# Copy published .NET app
COPY --from=dotnet-build /publish .

# Copy compiled libsnap7.so
COPY --from=snap7-build /snap7/snap7-full-1.4.2/build/bin/x86_64-linux/libsnap7.so .

# Expose ports: S7 (102 or 1102) + REST API
EXPOSE 102 1102 15000

# Run the simulator
ENTRYPOINT ["./SoftPlc.AgvSimulator"]
