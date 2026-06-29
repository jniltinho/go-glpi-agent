# Makefile do go-fusioninventory-agent

BINARY      := fusioninventory-agent
PKG         := ./cmd/fusioninventory-agent
VERSION     ?= $(shell git describe --tags --always 2>/dev/null || echo 0.1.0-dev)
LDFLAGS     := -s -w -X go-fusioninventory-agent/internal/version.Version=$(VERSION)
PREFIX      ?= /usr
DESTDIR     ?=

.PHONY: all build test vet clean install package-deb package-rpm

all: build

build:
	CGO_ENABLED=0 go build -ldflags "$(LDFLAGS)" -o $(BINARY) $(PKG)

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

install: build
	install -D -m 0755 $(BINARY) $(DESTDIR)$(PREFIX)/bin/$(BINARY)
	install -D -m 0644 contrib/$(BINARY).service $(DESTDIR)/lib/systemd/system/$(BINARY).service

# requer nfpm (https://nfpm.goreleaser.com)
package-deb: build-all
	nfpm package --packager deb --target dist/

package-rpm: build-all
	nfpm package --packager rpm --target dist/
