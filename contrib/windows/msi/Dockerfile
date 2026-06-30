# Build the go-glpi-agent Windows .msi in a container — no Windows host, no Wine.
# wixl (msitools) builds the MSI from the WiX source on plain Linux.
#
# Build the image and the MSI (run from the repo root):
#   docker build -f contrib/windows/msi/Dockerfile -t go-glpi-agent-msi .
#   docker run --rm -e VERSION=1.2.3 -v "$PWD/dist:/out" go-glpi-agent-msi
#
# The .msi lands in ./dist on the host.
FROM golang:1.26-bookworm

# wixl + msitools (the only build dependency beyond Go).
RUN apt-get update \
    && apt-get install -y --no-install-recommends wixl msitools \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /src
# Prime the module cache.
COPY go.mod go.sum ./
RUN go mod download
COPY . .

# VERSION is injected at run time (defaults to git-describe inside the Makefile).
ENV VERSION=0.0.0-dev
VOLUME /out

# Build the Windows exe + MSI, then copy the artifact to the mounted /out dir.
CMD set -eu; \
    make package-msi VERSION="$VERSION"; \
    cp dist/go-glpi-agent_*_x64.msi /out/; \
    ls -l /out/*.msi
