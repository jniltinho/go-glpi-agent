package agent

import (
	"os"
	"path/filepath"
	"testing"
	"time"
)

func TestWritableVarDir(t *testing.T) {
	// A creatable path under a temp root is used as-is (no fallback).
	preferred := filepath.Join(t.TempDir(), "var")
	if dir, fellBack := WritableVarDir(preferred); dir != preferred || fellBack {
		t.Errorf("writable preferred: got (%q, %v), want (%q, false)", dir, fellBack, preferred)
	}

	// An uncreatable path (a child of a regular file) falls back to a writable dir.
	file := filepath.Join(t.TempDir(), "afile")
	if err := os.WriteFile(file, []byte("x"), 0o644); err != nil {
		t.Fatal(err)
	}
	blocked := filepath.Join(file, "var") // can't mkdir under a file
	dir, fellBack := WritableVarDir(blocked)
	if !fellBack {
		t.Errorf("expected fallback for %q, got %q", blocked, dir)
	}
	if !isWritableDir(dir) {
		t.Errorf("fallback dir %q is not writable", dir)
	}
}

func TestGenerateDeviceIDFormat(t *testing.T) {
	now := time.Date(2026, 6, 29, 15, 30, 45, 0, time.UTC)
	id := generateDeviceID("srv-web.example.com", now)
	want := "srv-web-2026-06-29-15-30-45"
	if id != want {
		t.Errorf("id = %q, expected %q", id, want)
	}
}

func TestLoadOrCreatePersists(t *testing.T) {
	dir := t.TempDir()
	now := time.Date(2026, 1, 2, 3, 4, 5, 0, time.UTC)

	id1, err := LoadOrCreateDeviceID(dir, "host", now)
	if err != nil {
		t.Fatalf("primeira chamada: %v", err)
	}
	// second call (different time) should reuse the same persisted ID
	id2, err := LoadOrCreateDeviceID(dir, "host", now.Add(time.Hour))
	if err != nil {
		t.Fatalf("segunda chamada: %v", err)
	}
	if id1 != id2 {
		t.Errorf("device id did not persist: %q != %q", id1, id2)
	}
}

func TestImportPerlDeviceID(t *testing.T) {
	dir := t.TempDir()
	// simulate a Storable .dump with the deviceid embedded in binary bytes
	perlID := "old-srv-2024-03-15-08-00-00"
	// in Storable, key and value are strings with a length prefix
	// (binary byte), so there is a non-textual separator before the value.
	dump := []byte{0x04, 0x08, 0x00}
	dump = append(dump, []byte("deviceid")...)
	dump = append(dump, byte(len(perlID))) // length prefix (non-\w)
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
		t.Errorf("device id = %q, expected to import %q from .dump", id, perlID)
	}
}
