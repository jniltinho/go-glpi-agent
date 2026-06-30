// Package sysutil gathers helpers to run external commands and read
// files from /proc, /sys and /etc with graceful error handling.
package sysutil

import (
	"bufio"
	"context"
	"os"
	"os/exec"
	"strings"
)

// CommandExists reports whether a binary is available in PATH.
func CommandExists(name string) bool {
	_, err := exec.LookPath(name)
	return err == nil
}

// RunContext runs a command honoring the context (timeout/cancel) and
// returns stdout. Errors are returned to the caller to decide.
func RunContext(ctx context.Context, name string, args ...string) (string, error) {
	cmd := exec.CommandContext(ctx, name, args...)
	out, err := cmd.Output()
	return string(out), err
}

// RunLines runs a command and returns stdout split into non-empty lines.
func RunLines(ctx context.Context, name string, args ...string) ([]string, error) {
	out, err := RunContext(ctx, name, args...)
	if err != nil {
		return nil, err
	}
	return SplitLines(out), nil
}

// SplitLines splits text into lines, discarding trailing empty lines.
func SplitLines(s string) []string {
	var lines []string
	sc := bufio.NewScanner(strings.NewReader(s))
	sc.Buffer(make([]byte, 0, 64*1024), 4*1024*1024)
	for sc.Scan() {
		lines = append(lines, sc.Text())
	}
	return lines
}

// ReadFileTrim reads a file and returns its content with surrounding whitespace trimmed.
// Returns an empty string if the file does not exist or cannot be read.
func ReadFileTrim(path string) string {
	b, err := os.ReadFile(path)
	if err != nil {
		return ""
	}
	return strings.TrimSpace(string(b))
}

// FileExists reports whether a path exists.
func FileExists(path string) bool {
	_, err := os.Stat(path)
	return err == nil
}

// junkValues are placeholder identity strings that mean "no real value". They
// are reported by many BIOSes, VMs and WMI providers and must not be treated as
// real data — mirrors what dmidecode/glpi-agent filter out. Shared by the Linux
// DMI collector and the Windows WMI collectors.
var junkValues = map[string]bool{
	"none": true, "n/a": true, "na": true, "not specified": true,
	"not available": true, "not applicable": true, "default string": true,
	"to be filled by o.e.m.": true, "to be filled by oem": true,
	"system serial number": true, "system product name": true,
	"system manufacturer": true, "system version": true, "system name": true,
	"chassis serial number": true, "base board serial number": true,
	"no asset tag": true, "asset tag": true, "empty": true, "unknown": true,
	"oem": true, "invalid": true, "fill by oem": true,
}

// CleanDMI returns "" for placeholder/junk identity values (including all-zero
// strings like "0" or "0000"), otherwise the trimmed value. Used for DMI fields
// on Linux and the equivalent WMI string properties on Windows.
func CleanDMI(s string) string {
	s = strings.TrimSpace(s)
	if s == "" {
		return ""
	}
	if junkValues[strings.ToLower(s)] {
		return ""
	}
	if strings.Trim(s, "0") == "" { // "0", "00", "0000000000", ...
		return ""
	}
	return s
}
