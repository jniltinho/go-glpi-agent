package config

import (
	"os"
	"path/filepath"
	"testing"
)

func TestLoadBasic(t *testing.T) {
	dir := t.TempDir()
	path := filepath.Join(dir, "agent.cfg")
	content := `
# comentário
server = http://glpi.example/plugins/fusioninventory/
delaytime = 7200
backend-collect-timeout = 60
no-category = printer,video
scan-processes = 1
tag = "minha-entidade"
no-ssl-check = 1
unknown-key = qualquer
no-httpd = 1
`
	if err := os.WriteFile(path, []byte(content), 0o644); err != nil {
		t.Fatal(err)
	}

	cfg, err := Load(path)
	if err != nil {
		t.Fatalf("Load: %v", err)
	}

	if cfg.Server != "http://glpi.example/plugins/fusioninventory/" {
		t.Errorf("Server = %q", cfg.Server)
	}
	if cfg.DelayTime != 7200 {
		t.Errorf("DelayTime = %d, expected 7200", cfg.DelayTime)
	}
	if cfg.BackendCollectTimeout != 60 {
		t.Errorf("BackendCollectTimeout = %d, expected 60", cfg.BackendCollectTimeout)
	}
	if !cfg.ScanProcesses {
		t.Error("ScanProcesses deveria ser true")
	}
	if cfg.Tag != "minha-entidade" {
		t.Errorf("Tag = %q (quotes not stripped?)", cfg.Tag)
	}
	if !cfg.NoSSLCheck {
		t.Error("NoSSLCheck deveria ser true")
	}
	if !cfg.CategoryDisabled("printer") || !cfg.CategoryDisabled("VIDEO") {
		t.Error("no-category not applied (should be case-insensitive)")
	}
	// unknown-key should be recorded; no-httpd should be silently ignored
	foundUnknown := false
	for _, k := range cfg.UnknownKeys {
		if k == "unknown-key" {
			foundUnknown = true
		}
		if k == "no-httpd" {
			t.Error("no-httpd should not be in UnknownKeys")
		}
	}
	if !foundUnknown {
		t.Error("unknown-key deveria estar em UnknownKeys")
	}
}

func TestLoadInclude(t *testing.T) {
	dir := t.TempDir()
	confd := filepath.Join(dir, "conf.d")
	if err := os.MkdirAll(confd, 0o755); err != nil {
		t.Fatal(err)
	}
	os.WriteFile(filepath.Join(dir, "agent.cfg"), []byte(`include "conf.d/"`+"\n"), 0o644)
	os.WriteFile(filepath.Join(confd, "extra.cfg"), []byte("local = /tmp/inv\n"), 0o644)

	cfg, err := Load(filepath.Join(dir, "agent.cfg"))
	if err != nil {
		t.Fatalf("Load: %v", err)
	}
	if cfg.Local != "/tmp/inv" {
		t.Errorf("Local = %q, expected /tmp/inv (include did not work)", cfg.Local)
	}
}

func TestDefaults(t *testing.T) {
	cfg := Default()
	if cfg.BackendCollectTimeout != 180 {
		t.Errorf("default timeout = %d, expected 180", cfg.BackendCollectTimeout)
	}
	if cfg.VarDir != "/var/lib/fusioninventory/agent" {
		t.Errorf("default VarDir = %q", cfg.VarDir)
	}
}
