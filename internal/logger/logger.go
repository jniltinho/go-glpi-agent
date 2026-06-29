// Package logger fornece logging com níveis e backends configuráveis
// (stderr, arquivo, syslog), espelhando os backends do agente Perl.
package logger

import (
	"fmt"
	"io"
	"log/syslog"
	"os"
	"strings"
)

// Level representa a severidade da mensagem.
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

// Logger emite mensagens conforme o nível configurado.
type Logger struct {
	level   Level
	out     io.Writer
	syslogW *syslog.Writer
}

// Options configura a criação de um Logger.
type Options struct {
	Backend     string // Stderr | File | Syslog
	LogFile     string
	LogFacility string
	Debug       bool
}

// New cria um logger conforme as opções. Em caso de falha ao abrir
// arquivo/syslog, faz fallback para stderr.
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
				fmt.Fprintf(os.Stderr, "[logger] falha ao abrir %s: %v; usando stderr\n", opts.LogFile, err)
			}
		}
	case "syslog":
		if w, err := syslog.New(syslog.LOG_INFO|facility(opts.LogFacility), "fusioninventory-agent"); err == nil {
			l.syslogW = w
		} else {
			fmt.Fprintf(os.Stderr, "[logger] falha ao conectar syslog: %v; usando stderr\n", err)
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
