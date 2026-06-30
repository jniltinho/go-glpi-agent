package agent

import (
	"crypto/rand"
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"regexp"
	"strings"
	"time"
)

// stateFile is the name of the persistent state file (JSON).
const stateFile = "FusionInventory-Agent.json"

// dumpFile is the Perl agent storage (Storable). Read only to migrate the
// deviceid on the first run.
const dumpFile = "FusionInventory-Agent.dump"

// glpiDumpFile is the GLPI Agent storage (Storable). It contains the deviceid
// and the agentid (UUID), read for in-place migration.
const glpiDumpFile = "GLPI-Agent.dump"

// state is the content persisted as JSON.
type state struct {
	DeviceID string `json:"deviceid"`
	AgentID  string `json:"agentid,omitempty"`
}

// LoadOrCreateDeviceID returns the persistent device ID. Resolution order:
//  1. {vardir}/FusionInventory-Agent.json (Go agent state)
//  2. {vardir}/FusionInventory-Agent.dump (Perl agent migration)
//  3. generates a new one in the format {hostname}-{YYYY}-{MM}-{DD}-{HH}-{MM}-{SS}
//
// The resolved ID is persisted as JSON for future runs.
func LoadOrCreateDeviceID(vardir, hostname string, now time.Time) (string, error) {
	jsonPath := filepath.Join(vardir, stateFile)

	if id := readJSONDeviceID(jsonPath); id != "" {
		return id, nil
	}

	if id := readPerlDeviceID(filepath.Join(vardir, dumpFile)); id != "" {
		_ = saveDeviceID(jsonPath, id)
		return id, nil
	}

	id := generateDeviceID(hostname, now)
	if err := saveDeviceID(jsonPath, id); err != nil {
		return id, fmt.Errorf("persist device id: %w", err)
	}
	return id, nil
}

// LoadOrCreateAgentID returns the persistent agentid (UUID v4), generating a new
// one on the first run. Resolution order:
//  1. {vardir}/FusionInventory-Agent.json
//  2. {vardir}/GLPI-Agent.dump (GLPI Agent migration)
//  3. generates a new UUID v4
//
// The agentid is distinct from the deviceid and sent in the GLPI-Agent-ID header.
func LoadOrCreateAgentID(vardir string) (string, error) {
	jsonPath := filepath.Join(vardir, stateFile)

	if id := readState(jsonPath).AgentID; id != "" {
		return id, nil
	}

	if id := uuidPattern.FindString(readFile(filepath.Join(vardir, glpiDumpFile))); id != "" {
		_ = saveState(jsonPath, func(s *state) { s.AgentID = id })
		return id, nil
	}

	id, err := generateUUID()
	if err != nil {
		return "", fmt.Errorf("generate agentid: %w", err)
	}
	if err := saveState(jsonPath, func(s *state) { s.AgentID = id }); err != nil {
		return id, fmt.Errorf("persist agentid: %w", err)
	}
	return id, nil
}

// WritableVarDir returns preferred when it exists (or can be created) and is
// writable; otherwise it falls back to a per-user cache directory, then the temp
// dir. This lets a manually-run agent — one not installed under the system prefix
// whose state dir is not writable — still persist its deviceid/agentid instead of
// regenerating them every run (and avoids a noisy mkdir-permission warning). The
// returned bool reports whether a fallback was used.
func WritableVarDir(preferred string) (dir string, fellBack bool) {
	if isWritableDir(preferred) {
		return preferred, false
	}
	if cache, err := os.UserCacheDir(); err == nil {
		if alt := filepath.Join(cache, "go-glpi-agent"); isWritableDir(alt) {
			return alt, true
		}
	}
	if alt := filepath.Join(os.TempDir(), "go-glpi-agent"); isWritableDir(alt) {
		return alt, true
	}
	return preferred, false // nothing writable; caller surfaces the persist error
}

// isWritableDir creates dir if needed and reports whether a file can be written
// in it, probing with a temporary file.
func isWritableDir(dir string) bool {
	if dir == "" {
		return false
	}
	if err := os.MkdirAll(dir, 0o755); err != nil {
		return false
	}
	f, err := os.CreateTemp(dir, ".probe-*")
	if err != nil {
		return false
	}
	name := f.Name()
	_ = f.Close()
	_ = os.Remove(name)
	return true
}

// readJSONDeviceID returns the deviceid stored in the JSON state, or "" when
// the file is missing or holds no deviceid.
func readJSONDeviceID(path string) string {
	return readState(path).DeviceID
}

// readState reads the JSON state; returns an empty state if missing/unreadable.
func readState(path string) state {
	var s state
	if b, err := os.ReadFile(path); err == nil {
		_ = json.Unmarshal(b, &s)
	}
	return s
}

// readFile returns the whole file as a string, or "" on any read error — used
// to scan binary .dump files for the agentid, where errors mean "not present".
func readFile(path string) string {
	b, _ := os.ReadFile(path)
	return string(b)
}

// uuidPattern matches a canonical UUID inside the GLPI Agent binary .dump.
var uuidPattern = regexp.MustCompile(`[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}`)

// generateUUID generates a UUID v4 via crypto/rand (no external dependency).
func generateUUID() (string, error) {
	var b [16]byte
	if _, err := rand.Read(b[:]); err != nil {
		return "", err
	}
	b[6] = (b[6] & 0x0f) | 0x40 // version 4
	b[8] = (b[8] & 0x3f) | 0x80 // RFC 4122 variant
	return fmt.Sprintf("%x-%x-%x-%x-%x", b[0:4], b[4:6], b[6:8], b[8:10], b[10:16]), nil
}

// deviceIDPattern matches "hostname-YYYY-MM-DD-HH-MM-SS" inside the binary .dump.
var deviceIDPattern = regexp.MustCompile(`[\w.-]+-\d{4}-\d{2}-\d{2}-\d{2}-\d{2}-\d{2}`)

// readPerlDeviceID extracts the deviceid from the Perl Storable file. The format
// is binary, but the deviceid is an ASCII string easily located by regex.
func readPerlDeviceID(path string) string {
	b, err := os.ReadFile(path)
	if err != nil {
		return ""
	}
	return deviceIDPattern.FindString(string(b))
}

// saveDeviceID persists id as the deviceid, leaving any existing agentid intact.
func saveDeviceID(path, id string) error {
	return saveState(path, func(s *state) { s.DeviceID = id })
}

// saveState reads the current state, applies mut and rewrites it — preserving the
// field that is not being changed (deviceid and agentid coexist in the same file).
func saveState(path string, mut func(*state)) error {
	if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
		return err
	}
	s := readState(path)
	mut(&s)
	b, err := json.MarshalIndent(s, "", "  ")
	if err != nil {
		return err
	}
	return os.WriteFile(path, b, 0o644)
}

// generateDeviceID builds a deviceid as "{shorthostname}-{YYYY-MM-DD-HH-MM-SS}",
// stripping the domain from the hostname and falling back to "localhost" when empty.
func generateDeviceID(hostname string, now time.Time) string {
	if i := strings.Index(hostname, "."); i >= 0 {
		hostname = hostname[:i]
	}
	if hostname == "" {
		hostname = "localhost"
	}
	return fmt.Sprintf("%s-%s", hostname, now.Format("2006-01-02-15-04-05"))
}
