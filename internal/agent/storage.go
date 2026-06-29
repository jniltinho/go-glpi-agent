package agent

import (
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"regexp"
	"strings"
	"time"
)

// stateFile é o nome do arquivo de estado persistente (JSON).
const stateFile = "FusionInventory-Agent.json"

// dumpFile é o storage do agente Perl (Storable). Lido apenas para migração
// do deviceid na primeira execução.
const dumpFile = "FusionInventory-Agent.dump"

// state é o conteúdo persistido em JSON.
type state struct {
	DeviceID string `json:"deviceid"`
}

// LoadOrCreateDeviceID retorna o device ID persistente. A ordem de resolução:
//  1. {vardir}/FusionInventory-Agent.json (estado do agente Go)
//  2. {vardir}/FusionInventory-Agent.dump (migração do agente Perl)
//  3. gera novo no formato {hostname}-{YYYY}-{MM}-{DD}-{HH}-{MM}-{SS}
//
// O ID resolvido é persistido em JSON para execuções futuras.
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
		return id, fmt.Errorf("persistir device id: %w", err)
	}
	return id, nil
}

func readJSONDeviceID(path string) string {
	b, err := os.ReadFile(path)
	if err != nil {
		return ""
	}
	var s state
	if json.Unmarshal(b, &s) != nil {
		return ""
	}
	return s.DeviceID
}

// deviceIDPattern casa "hostname-YYYY-MM-DD-HH-MM-SS" dentro do .dump binário.
var deviceIDPattern = regexp.MustCompile(`[\w.-]+-\d{4}-\d{2}-\d{2}-\d{2}-\d{2}-\d{2}`)

// readPerlDeviceID extrai o deviceid do arquivo Storable do Perl. O formato é
// binário, mas o deviceid é uma string ASCII facilmente localizável por regex.
func readPerlDeviceID(path string) string {
	b, err := os.ReadFile(path)
	if err != nil {
		return ""
	}
	return deviceIDPattern.FindString(string(b))
}

func saveDeviceID(path, id string) error {
	if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
		return err
	}
	b, err := json.MarshalIndent(state{DeviceID: id}, "", "  ")
	if err != nil {
		return err
	}
	return os.WriteFile(path, b, 0o644)
}

func generateDeviceID(hostname string, now time.Time) string {
	if i := strings.Index(hostname, "."); i >= 0 {
		hostname = hostname[:i]
	}
	if hostname == "" {
		hostname = "localhost"
	}
	return fmt.Sprintf("%s-%s", hostname, now.Format("2006-01-02-15-04-05"))
}
