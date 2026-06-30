// Package logger provides logging with configurable levels and backends
// (stderr, file, syslog), mirroring the Perl agent's backends.
package logger

import (
	"fmt"
	"io"
	"log/syslog"
	"os"
	"strings"
)

// Level represents the message severity.
type Level int

const (
	LevelError Level = iota
	LevelWarning
	LevelInfo
	LevelDebug
)

func (l Level) String() string {
	switch l {
	case LevelError:
		return "error"
	case LevelWarning:
		return "warning"
	case LevelInfo:
		return "info"
	case LevelDebug:
		return "debug"
	default:
		return "info"
	}
}

// Logger emits messages according to the configured level.
type Logger struct {
	level   Level
	out     io.Writer
	syslogW *syslog.Writer
}

// Options configures the creation of a Logger.
type Options struct {
	Backend     string // Stderr | File | Syslog
	LogFile     string
	LogFacility string
	Debug       bool
}

// New creates a logger according to the options. If opening the
// file/syslog fails, it falls back to stderr.
func New(opts Options) *Logger {
	level := LevelInfo
	if opts.Debug {
		level = LevelDebug
	}
	l := &Logger{level: level, out: os.Stderr}

	switch strings.ToLower(opts.Backend) {
	case "file":
		if opts.LogFile != "" {
			if f, err := os.OpenFile(opts.LogFile, os.O_CREATE|os.O_WRONLY|os.O_APPEND, 0o644); err == nil {
				l.out = f
			} else {
				fmt.Fprintf(os.Stderr, "[logger] failed to open %s: %v; using stderr\n", opts.LogFile, err)
			}
		}
	case "syslog":
		if w, err := syslog.New(syslog.LOG_INFO|facility(opts.LogFacility), "go-glpi-agent"); err == nil {
			l.syslogW = w
		} else {
			fmt.Fprintf(os.Stderr, "[logger] failed to connect to syslog: %v; using stderr\n", err)
		}
	}
	return l
}

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

func (l *Logger) logf(level Level, format string, args ...any) {
	if level > l.level {
		return
	}
	msg := fmt.Sprintf(format, args...)
	if l.syslogW != nil {
		switch level {
		case LevelError:
			_ = l.syslogW.Err(msg)
		case LevelWarning:
			_ = l.syslogW.Warning(msg)
		case LevelDebug:
			_ = l.syslogW.Debug(msg)
		default:
			_ = l.syslogW.Info(msg)
		}
		return
	}
	fmt.Fprintf(l.out, "[%s] %s\n", level.String(), msg)
}

func (l *Logger) Error(format string, args ...any)   { l.logf(LevelError, format, args...) }
func (l *Logger) Warning(format string, args ...any) { l.logf(LevelWarning, format, args...) }
func (l *Logger) Info(format string, args ...any)    { l.logf(LevelInfo, format, args...) }
func (l *Logger) Debug(format string, args ...any)   { l.logf(LevelDebug, format, args...) }
