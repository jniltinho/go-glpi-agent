# Makefile do go-glpi-agent (módulo/repo: go-glpi-agent)

BINARY      := go-glpi-agent
PKG         := .
VERSION     ?= $(shell git describe --tags --always 2>/dev/null || echo 0.1.0-dev)
LDFLAGS     := -s -w -X go-glpi-agent/internal/version.Version=$(VERSION)
OPTDIR      := /opt/go-glpi-agent
DESTDIR     ?=

GLPI_AGENT_VERSION ?= 1.18

.PHONY: all build build-all build-windows package-windows package-msi build-freebsd package-freebsd test vet clean install package-deb package-rpm package-arch packages fetch-glpi-agent fetch-glpi-agent-win

all: build

build:
	CGO_ENABLED=0 go build -ldflags "$(LDFLAGS)" -o $(BINARY) $(PKG)

# baixa o glpi-agent oficial (AppImage) para dist/, usado como referência nas
# VMs de teste (ver test/vagrant/provision.sh). dist/ é gitignored.
fetch-glpi-agent:
	mkdir -p dist
	curl -fsSL https://github.com/glpi-project/glpi-agent/releases/download/$(GLPI_AGENT_VERSION)/glpi-agent-$(GLPI_AGENT_VERSION)-x86_64.AppImage -o dist/glpi-agent.AppImage
	chmod +x dist/glpi-agent.AppImage

# baixa o GLPI Agent oficial (portable zip) para Windows, usado como referência
# de comparação na VM Windows (ver test/vagrant-windows/provision.ps1).
fetch-glpi-agent-win:
	mkdir -p dist/ref
	curl -fsSL https://github.com/glpi-project/glpi-agent/releases/download/$(GLPI_AGENT_VERSION)/GLPI-Agent-$(GLPI_AGENT_VERSION)-x64.zip -o dist/ref/GLPI-Agent-$(GLPI_AGENT_VERSION)-x64.zip

# build estático para linux/amd64
build-all:
	CGO_ENABLED=0 GOOS=linux GOARCH=amd64 go build -ldflags "$(LDFLAGS)" -o dist/$(BINARY) $(PKG)

# build estático para windows/amd64 (cross-compila a partir do Linux)
build-windows:
	CGO_ENABLED=0 GOOS=windows GOARCH=amd64 go build -ldflags "$(LDFLAGS)" -o dist/$(BINARY).exe $(PKG)

# empacota o .exe + agent.cfg + scripts de install/uninstall num .zip
package-windows: build-windows
	mkdir -p dist/windows
	cp dist/$(BINARY).exe dist/windows/
	cp contrib/windows/agent.cfg dist/windows/
	cp contrib/windows/install.ps1 dist/windows/
	cp contrib/windows/uninstall.ps1 dist/windows/
	cd dist/windows && zip -q -r ../$(BINARY)_$(VERSION)_windows_amd64.zip .

# empacota um .msi (WiX/wixl) para deploy via GPO/Intune/SCCM. REQUER wixl
# (msitools): `apt-get install wixl`. O .exe é buildado e estanciado em dist/msi.
# O ProductVersion do MSI precisa ser numérico (x.y[.z]); extraímos o prefixo
# numérico de VERSION (ex.: "1.2.3-5-gabc" -> "1.2.3", "ci-abc" -> "0.0.0").
package-msi: build-windows
	mkdir -p dist/msi
	cp dist/$(BINARY).exe dist/msi/$(BINARY).exe
	cp contrib/windows/msi/agent.cfg dist/msi/agent.cfg
	MSI_VERSION=$$(printf '%s' "$(VERSION)" | grep -oE '[0-9]+(\.[0-9]+){1,2}' | head -1); \
	[ -n "$$MSI_VERSION" ] || MSI_VERSION=0.0.0; \
	echo "MSI ProductVersion: $$MSI_VERSION (from VERSION=$(VERSION))"; \
	wixl -a x64 -D SourceDir=dist/msi -D Version=$$MSI_VERSION \
		-o dist/$(BINARY)_$(VERSION)_x64.msi contrib/windows/msi/$(BINARY).wxs

# build estático para freebsd/amd64 (cross-compila a partir do Linux)
build-freebsd:
	CGO_ENABLED=0 GOOS=freebsd GOARCH=amd64 go build -ldflags "$(LDFLAGS)" -o dist/$(BINARY)-freebsd $(PKG)

# empacota o binário + agent.cfg + rc.d script + notas num .tar.gz
package-freebsd: build-freebsd
	mkdir -p dist/freebsd
	cp dist/$(BINARY)-freebsd dist/freebsd/$(BINARY)
	cp contrib/freebsd/agent.cfg dist/freebsd/
	cp contrib/freebsd/go_glpi_agent dist/freebsd/
	cp contrib/freebsd/INSTALL.md dist/freebsd/
	tar -czf dist/$(BINARY)_$(VERSION)_freebsd_amd64.tar.gz -C dist/freebsd .

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

# Arch Linux package (.pkg.tar.zst), instalável com `pacman -U`
package-arch: build-all
	VERSION=$(VERSION) nfpm package --packager archlinux --target dist/

# todos os formatos de pacote de uma vez
packages: package-deb package-rpm package-arch
