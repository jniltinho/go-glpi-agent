// Package config loads the agent configuration from an agent.cfg file in the
// INI format compatible with the Perl agent, with overrides from command-line
// flags.
package config

import (
	"bufio"
	"os"
	"path/filepath"
	"strconv"
	"strings"
)

// DefaultConfFile is the default path of the configuration file.
const DefaultConfFile = "/opt/go-glpi-agent/agent.cfg"

// Config holds the parameters supported in v1. Unmapped fields from agent.cfg
// are ignored (with a debug warning) — see design.md D7.
type Config struct {
	// Target
	Server string
	Local  string

	// Scheduling / daemon
	DelayTime int  // seconds between cycles (default 3600)
	Lazy      bool // does not contact the server before the schedule
	Force     bool // sends even without a request

	// Collection
	BackendCollectTimeout int      // timeout per collector, seconds (default 180)
	NoCategory            []string // disabled categories
	ScanProcesses         bool

	// Identification
	Tag    string
	VarDir string // persistence directory

	// HTTP
	Timeout       int // HTTP timeout, seconds (default 180)
	User          string
	Password      string
	Proxy         string
	NoCompression bool // sends the body uncompressed (plain application/json)

	// TLS
	NoSSLCheck bool
	CACertFile string
	CACertDir  string

	// Logging
	Logger      string // Stderr | File | Syslog
	LogFile     string
	LogFacility string // default LOG_USER
	Debug       bool

	// unknown keys found (for a debug warning)
	UnknownKeys []string
}

// Default returns a configuration with the Perl agent defaults.
func Default() *Config {
	return &Config{
		DelayTime:             3600,
		BackendCollectTimeout: 180,
		Timeout:               180,
		VarDir:                "/opt/go-glpi-agent/var",
		Logger:                "Stderr",
		LogFacility:           "LOG_USER",
	}
}

// keys recognized but with no effect in v1 (ignored without warning) — design.md D7.
var ignoredKeys = map[string]bool{
	"html": true, "scan-homedirs": true, "scan-profiles": true,
	"additional-content": true, "no-httpd": true, "no-task": true,
	"tasks": true, "no-p2p": true, "color": true,
	"conf-reload-interval": true, "httpd-port": true, "httpd-trust": true,
	"httpd-ip": true,
}

// Load reads the configuration file and applies it over the defaults. It supports
// the `include "dir/"` directive to load .cfg files from a directory.
func Load(path string) (*Config, error) {
	cfg := Default()
	if err := cfg.loadFile(path); err != nil {
		return nil, err
	}
	return cfg, nil
}

func (c *Config) loadFile(path string) error {
	f, err := os.Open(path)
	if err != nil {
		return err
	}
	defer f.Close()

	baseDir := filepath.Dir(path)
	scanner := bufio.NewScanner(f)
	for scanner.Scan() {
		line := strings.TrimSpace(scanner.Text())
		if line == "" || strings.HasPrefix(line, "#") {
			continue
		}

		// include "conf.d/" directive
		if strings.HasPrefix(line, "include") {
			rest := strings.TrimSpace(strings.TrimPrefix(line, "include"))
			rest = strings.Trim(rest, `"'`)
			c.loadInclude(baseDir, rest)
			continue
		}

		key, value, ok := splitKV(line)
		if !ok {
			continue
		}
		c.applyKey(key, value)
	}
	return scanner.Err()
}

// loadInclude loads all .cfg files from a directory (relative to baseDir).
func (c *Config) loadInclude(baseDir, pattern string) {
	dir := pattern
	if !filepath.IsAbs(dir) {
		dir = filepath.Join(baseDir, pattern)
	}
	entries, err := os.ReadDir(dir)
	if err != nil {
		return
	}
	for _, e := range entries {
		if e.IsDir() || !strings.HasSuffix(e.Name(), ".cfg") {
			continue
		}
		_ = c.loadFile(filepath.Join(dir, e.Name()))
	}
}

func splitKV(line string) (key, value string, ok bool) {
	idx := strings.Index(line, "=")
	if idx < 0 {
		return "", "", false
	}
	key = strings.TrimSpace(line[:idx])
	value = strings.TrimSpace(line[idx+1:])
	value = strings.Trim(value, `"'`)
	return key, value, key != ""
}

func (c *Config) applyKey(key, value string) {
	switch key {
	case "server":
		c.Server = value
	case "local":
		c.Local = value
	case "delaytime":
		c.DelayTime = atoiDefault(value, c.DelayTime)
	case "lazy":
		c.Lazy = isTrue(value)
	case "force":
		c.Force = isTrue(value)
	case "backend-collect-timeout":
		c.BackendCollectTimeout = atoiDefault(value, c.BackendCollectTimeout)
	case "no-category":
		c.NoCategory = appendCSV(c.NoCategory, value)
	case "scan-processes":
		c.ScanProcesses = isTrue(value)
	case "tag":
		c.Tag = value
	case "vardir":
		c.VarDir = value
	case "timeout":
		c.Timeout = atoiDefault(value, c.Timeout)
	case "user":
		c.User = value
	case "password":
		c.Password = value
	case "proxy":
		c.Proxy = value
	case "no-compression":
		c.NoCompression = isTrue(value)
	case "no-ssl-check":
		c.NoSSLCheck = isTrue(value)
	case "ca-cert-file":
		c.CACertFile = value
	case "ca-cert-dir":
		c.CACertDir = value
	case "logger":
		c.Logger = value
	case "logfile":
		c.LogFile = value
	case "logfacility":
		c.LogFacility = value
	case "debug":
		c.Debug = isTrue(value)
	default:
		if !ignoredKeys[key] {
			c.UnknownKeys = append(c.UnknownKeys, key)
		}
	}
}

// HasCategory reports whether a category is disabled via no-category.
func (c *Config) CategoryDisabled(cat string) bool {
	for _, v := range c.NoCategory {
		if strings.EqualFold(v, cat) {
			return true
		}
	}
	return false
}

func atoiDefault(s string, def int) int {
	if n, err := strconv.Atoi(strings.TrimSpace(s)); err == nil {
		return n
	}
	return def
}

func isTrue(s string) bool {
	switch strings.ToLower(strings.TrimSpace(s)) {
	case "1", "true", "yes", "on":
		return true
	}
	return false
}

func appendCSV(dst []string, value string) []string {
	for _, part := range strings.Split(value, ",") {
		if p := strings.TrimSpace(part); p != "" {
			dst = append(dst, p)
		}
	}
	return dst
}
