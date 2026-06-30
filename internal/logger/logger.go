// Package logger provides logging with configurable levels and backends
// (stderr, file, syslog), mirroring the Perl agent's backends. The syslog
// backend is only available on non-Windows platforms; see logger_unix.go /
// logger_windows.go.
package logger

import (
	"fmt"
	"io"
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

// String returns the lowercase level name used as the line prefix; unknown
// values fall back to "info".
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

// syslogWriter is the subset of *syslog.Writer the logger uses. It is supplied
// by newSyslog, which returns a real writer on unix and nil on Windows.
type syslogWriter interface {
	Err(string) error
	Warning(string) error
	Info(string) error
	Debug(string) error
}

// Logger emits messages according to the configured level.
type Logger struct {
	level   Level
	out     io.Writer
	syslogW syslogWriter
}

// Options configures the creation of a Logger.
type Options struct {
	Backend     string // Stderr | File | Syslog
	LogFile     string
	LogFacility string
	Debug       bool
}

// New creates a logger according to the options. If opening the file fails, or
// the syslog backend is unavailable (e.g. on Windows), it falls back to stderr.
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
		w, err := newSyslog(opts.LogFacility)
		switch {
		case err != nil:
			fmt.Fprintf(os.Stderr, "[logger] failed to connect to syslog: %v; using stderr\n", err)
		case w != nil:
			l.syslogW = w
		}
		// w == nil with no error: platform has no syslog (Windows); use stderr.
	}
	return l
}

// logf formats and emits a message, dropping it when level is below the
// configured threshold. Routes to syslog by severity when configured,
// otherwise writes "[level] msg" to the output writer.
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

// Error logs at the error level; always emitted regardless of configured level.
func (l *Logger) Error(format string, args ...any) { l.logf(LevelError, format, args...) }

// Warning logs at the warning level.
func (l *Logger) Warning(format string, args ...any) { l.logf(LevelWarning, format, args...) }

// Info logs at the info level; suppressed unless the level is info or debug.
func (l *Logger) Info(format string, args ...any) { l.logf(LevelInfo, format, args...) }

// Debug logs at the debug level; emitted only when Debug was enabled in Options.
func (l *Logger) Debug(format string, args ...any) { l.logf(LevelDebug, format, args...) }
