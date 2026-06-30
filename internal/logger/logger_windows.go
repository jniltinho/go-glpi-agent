//go:build windows

package logger

// newSyslog returns no syslog backend on Windows: the platform has no syslog
// daemon, so selecting "logger = Syslog" transparently falls back to stderr (or
// the configured file). Returning (nil, nil) signals "no backend, no error".
//
// A Windows Event Log backend can be added here later without touching the
// shared logger.
func newSyslog(string) (syslogWriter, error) { return nil, nil }
