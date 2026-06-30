//go:build !windows

package logger

import (
	"log/syslog"
	"strings"
)

// newSyslog connects to the local syslog daemon. Available on every Unix-like
// platform (Linux, macOS, the BSDs); Windows has its own stub in logger_windows.go.
func newSyslog(facilityName string) (syslogWriter, error) {
	w, err := syslog.New(syslog.LOG_INFO|facility(facilityName), "go-glpi-agent")
	if err != nil {
		return nil, err
	}
	return w, nil
}

// facility maps a logfacility name (with or without the LOG_ prefix) to a
// syslog priority, defaulting to LOG_USER for empty or unknown names.
func facility(name string) syslog.Priority {
	switch strings.ToUpper(strings.TrimPrefix(name, "LOG_")) {
	case "DAEMON":
		return syslog.LOG_DAEMON
	case "USER", "":
		return syslog.LOG_USER
	case "LOCAL0":
		return syslog.LOG_LOCAL0
	case "LOCAL1":
		return syslog.LOG_LOCAL1
	default:
		return syslog.LOG_USER
	}
}
