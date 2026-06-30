# Makefile do go-glpi-agent (módulo/repo: go-fusioninventory-agent)

BINARY      := go-glpi-agent
PKG         := .
VERSION     ?= $(shell git describe --tags --always 2>/dev/null || echo 0.1.0-dev)
LDFLAGS     := -s -w -X go-glpi-agent/internal/version.Version=$(VERSION)
OPTDIR      := /opt/go-glpi-agent
DESTDIR     ?=

GLPI_AGENT_VERSION ?= 1.18

.PHONY: all build build-all test vet clean install package-deb package-rpm fetch-glpi-agent

all: build

build:
	CGO_ENABLED=0 go build -ldflags "$(LDFLAGS)" -o $(BINARY) $(PKG)

# baixa o glpi-agent oficial (AppImage) para dist/, usado como referência nas
# VMs de teste (ver test/vagrant/provision.sh). dist/ é gitignored.
fetch-glpi-agent:
	mkdir -p dist
	curl -fsSL https://github.com/glpi-project/glpi-agent/releases/download/$(GLPI_AGENT_VERSION)/glpi-agent-$(GLPI_AGENT_VERSION)-x86_64.AppImage -o dist/glpi-agent.AppImage
	chmod +x dist/glpi-agent.AppImage

# build estático para linux/amd64
build-all:
	CGO_ENABLED=0 GOOS=linux GOARCH=amd64 go build -ldflags "$(LDFLAGS)" -o dist/$(BINARY) $(PKG)

test:
	go test ./...

vet:
	go vet ./...

clean:
	rm -f $(BINARY)
	rm -rf dist

# instala tudo sob /opt/go-glpi-agent; os units do systemd ficam em
# /lib/systemd/system (exigência do systemd) apontando para o binário em /opt.
install: build
	install -D -m 0755 $(BINARY) $(DESTDIR)$(OPTDIR)/$(BINARY)
	install -D -m 0644 contrib/agent.cfg $(DESTDIR)$(OPTDIR)/agent.cfg
	install -D -m 0644 contrib/$(BINARY).service $(DESTDIR)/lib/systemd/system/$(BINARY).service
	install -D -m 0644 contrib/$(BINARY).timer $(DESTDIR)/lib/systemd/system/$(BINARY).timer
	install -D -m 0644 contrib/$(BINARY)-daemon.service $(DESTDIR)/lib/systemd/system/$(BINARY)-daemon.service

# requer nfpm (https://nfpm.goreleaser.com); usa nfpm.yaml na raiz
package-deb: build-all
	VERSION=$(VERSION) nfpm package --packager deb --target dist/

package-rpm: build-all
	VERSION=$(VERSION) nfpm package --packager rpm --target dist/
