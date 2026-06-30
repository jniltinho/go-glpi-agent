# Static build of go-glpi-agent
FROM golang:1.26 AS build
WORKDIR /src
COPY go.mod go.sum ./
RUN go mod download
COPY . .
RUN CGO_ENABLED=0 go build -ldflags "-s -w" -o /out/go-glpi-agent .

# Minimal final image. For a full inventory (dmidecode, lsblk) use a base with
# those tools and run with access to the host's /sys and /proc.
FROM debian:stable-slim
RUN apt-get update && apt-get install -y --no-install-recommends \
        dmidecode util-linux ca-certificates \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /out/go-glpi-agent /opt/go-glpi-agent/go-glpi-agent
ENTRYPOINT ["/opt/go-glpi-agent/go-glpi-agent"]
CMD ["run", "--local", "/tmp/inventory"]
