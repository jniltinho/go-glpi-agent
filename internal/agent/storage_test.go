package agent

import (
	"os"
	"path/filepath"
	"testing"
	"time"
)

func TestGenerateDeviceIDFormat(t *testing.T) {
	now := time.Date(2026, 6, 29, 15, 30, 45, 0, time.UTC)
	id := generateDeviceID("srv-web.example.com", now)
	want := "srv-web-2026-06-29-15-30-45"
	if id != want {
		t.Errorf("id = %q, esperado %q", id, want)
	}
}

func TestLoadOrCreatePersists(t *testing.T) {
	dir := t.TempDir()
	now := time.Date(2026, 1, 2, 3, 4, 5, 0, time.UTC)

	id1, err := LoadOrCreateDeviceID(dir, "host", now)
	if err != nil {
		t.Fatalf("primeira chamada: %v", err)
	}
	// segunda chamada (tempo diferente) deve reusar o mesmo ID persistido
	id2, err := LoadOrCreateDeviceID(dir, "host", now.Add(time.Hour))
	if err != nil {
		t.Fatalf("segunda chamada: %v", err)
	}
	if id1 != id2 {
		t.Errorf("device id não persistiu: %q != %q", id1, id2)
	}
}

func TestImportPerlDeviceID(t *testing.T) {
	dir := t.TempDir()
	// simula um .dump Storable com o deviceid embutido em bytes binários
	perlID := "old-srv-2024-03-15-08-00-00"
	// no Storable, chave e valor são strings com prefixo de comprimento
	// (byte binário), então há um separador não-textual antes do valor.
	dump := []byte{0x04, 0x08, 0x00}
	dump = append(dump, []byte("deviceid")...)
	dump = append(dump, byte(len(perlID))) // prefixo de comprimento (não-\w)
	dump = append(dump, []byte(perlID)...)
	dump = append(dump, 0x00)
	dump = append(dump, []byte("more")...)
	if err := os.WriteFile(filepath.Join(dir, dumpFile), dump, 0o644); err != nil {
		t.Fatal(err)
	}

	id, err := LoadOrCreateDeviceID(dir, "host", time.Now())
	if err != nil {
		t.Fatalf("LoadOrCreateDeviceID: %v", err)
	}
	if id != perlID {
		t.Errorf("device id = %q, esperado importar %q do .dump", id, perlID)
	}
}
