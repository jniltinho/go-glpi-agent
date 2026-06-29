# Build estático do go-fusioninventory-agent
FROM golang:1.26 AS build
WORKDIR /src
COPY go.mod go.sum ./
RUN go mod download
COPY . .
RUN CGO_ENABLED=0 go build -ldflags "-s -w" -o /out/fusioninventory-agent ./cmd/fusioninventory-agent

# Imagem final mínima. Para inventário completo (dmidecode, lsblk) use uma
# base com essas ferramentas e rode com acesso a /sys e /proc do host.
FROM debian:stable-slim
RUN apt-get update && apt-get install -y --no-install-recommends \
        dmidecode util-linux ca-certificates \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /out/fusioninventory-agent /usr/bin/fusioninventory-agent
ENTRYPOINT ["/usr/bin/fusioninventory-agent"]
CMD ["--local", "/tmp/inventory"]
