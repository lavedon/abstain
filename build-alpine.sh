#!/usr/bin/env sh
# Build abstain as a native-AOT linux-musl-x64 binary and install it as "abs".
# Run this on Alpine Linux. Override the install dir with ABSTAIN_INSTALL_DIR.
#
# Prerequisites:
#   apk add clang build-base zlib-dev
set -eu

RID="linux-musl-x64"
PROJ="$(cd "$(dirname "$0")" && pwd)"
DEST="${ABSTAIN_INSTALL_DIR:-$HOME/.local/bin}"
DATA_DEST="$DEST/Data"

if [ ! -f /etc/alpine-release ]; then
    echo "error: run this on Alpine Linux so Native AOT targets musl libc." >&2
    exit 1
fi

if ! command -v dotnet >/dev/null 2>&1; then
    echo "error: dotnet not found. Install the .NET 10 SDK before running this script." >&2
    exit 1
fi

if ! command -v clang >/dev/null 2>&1; then
    echo "error: clang not found. Install the AOT prerequisites:" >&2
    echo "  apk add clang build-base zlib-dev" >&2
    exit 1
fi

echo "Publishing abstain ($RID, native AOT)..."
dotnet publish "$PROJ" -c Release -r "$RID" --self-contained true --nologo

BIN="$PROJ/bin/Release/net10.0/$RID/native/abstain"
if [ ! -f "$BIN" ]; then
    echo "error: native binary not found at $BIN" >&2
    exit 1
fi

mkdir -p "$DEST" "$DATA_DEST"
install -m 0755 "$BIN" "$DEST/abs"

if [ ! -f "$DATA_DEST/abstain.db" ]; then
    if [ -f "$PROJ/Data/abstain.db" ]; then
        install -m 0644 "$PROJ/Data/abstain.db" "$DATA_DEST/abstain.db"
    elif [ -f "$PROJ/bin/Debug/net10.0/Data/abstain.db" ]; then
        install -m 0644 "$PROJ/bin/Debug/net10.0/Data/abstain.db" "$DATA_DEST/abstain.db"
    fi
fi

echo "Deployed: $DEST/abs"
echo "Data directory: $DATA_DEST"

case ":$PATH:" in
    *":$DEST:"*) ;;
    *) echo "note: $DEST is not on your PATH - add it so you can run 'abs' from anywhere." ;;
esac
