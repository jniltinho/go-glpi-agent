// Comando go-fusioninventory-agent: agente de inventário em Go para Linux,
// compatível com o protocolo OCS/FusionInventory do GLPI.
package main

import (
	"context"
	"flag"
	"fmt"
	"os"
	"strings"

	"go-fusioninventory-agent/internal/agent"
	"go-fusioninventory-agent/internal/config"
	"go-fusioninventory-agent/internal/logger"
	"go-fusioninventory-agent/internal/version"
)

func main() {
	var (
		server     = flag.String("server", "", "URL do servidor GLPI")
		local      = flag.String("local", "", "diretório para gravar o inventário em XML")
		confFile   = flag.String("conf-file", config.DefaultConfFile, "caminho do agent.cfg")
		daemon     = flag.Bool("daemon", false, "executar em modo daemon (ciclos periódicos)")
		runOnce    = flag.Bool("run-once", false, "executar um único ciclo e sair")
		debug      = flag.Bool("debug", false, "habilitar logging de debug")
		force      = flag.Bool("force", false, "enviar inventário mesmo sem solicitação")
		noCategory = flag.String("no-category", "", "categorias a desabilitar (separadas por vírgula)")
		showVer    = flag.Bool("version", false, "imprimir a versão e sair")
	)
	flag.Parse()

	if *showVer {
		fmt.Printf("%s %s\n", version.Name, version.Version)
		return
	}

	// Carrega config do arquivo (se existir); senão usa defaults.
	cfg := config.Default()
	if _, err := os.Stat(*confFile); err == nil {
		loaded, lerr := config.Load(*confFile)
		if lerr != nil {
			fmt.Fprintf(os.Stderr, "erro ao ler %s: %v\n", *confFile, lerr)
			os.Exit(1)
		}
		cfg = loaded
	} else if isFlagSet("conf-file") {
		// usuário pediu um arquivo específico que não existe
		fmt.Fprintf(os.Stderr, "arquivo de configuração não encontrado: %s\n", *confFile)
		os.Exit(1)
	}

	// Flags sobrescrevem o arquivo de configuração.
	if isFlagSet("server") {
		cfg.Server = *server
	}
	if isFlagSet("local") {
		cfg.Local = *local
	}
	if isFlagSet("debug") {
		cfg.Debug = *debug
	}
	if isFlagSet("force") {
		cfg.Force = *force
	}
	if isFlagSet("no-category") {
		for _, c := range strings.Split(*noCategory, ",") {
			if c = strings.TrimSpace(c); c != "" {
				cfg.NoCategory = append(cfg.NoCategory, c)
			}
		}
	}

	log := logger.New(logger.Options{
		Backend:     cfg.Logger,
		LogFile:     cfg.LogFile,
		LogFacility: cfg.LogFacility,
		Debug:       cfg.Debug,
	})

	for _, k := range cfg.UnknownKeys {
		log.Debug("chave de configuração desconhecida ignorada: %s", k)
	}

	a, err := agent.New(cfg, log)
	if err != nil {
		log.Error("%v", err)
		os.Exit(1)
	}

	ctx := context.Background()
	if *daemon {
		if err := a.RunDaemon(ctx); err != nil {
			log.Error("daemon: %v", err)
			os.Exit(1)
		}
		return
	}

	// modo padrão e --run-once: um ciclo único
	_ = *runOnce
	if err := a.RunOnce(ctx); err != nil {
		log.Error("inventário: %v", err)
		os.Exit(1)
	}
}

// isFlagSet informa se uma flag foi explicitamente passada na linha de comando.
func isFlagSet(name string) bool {
	set := false
	flag.Visit(func(f *flag.Flag) {
		if f.Name == name {
			set = true
		}
	})
	return set
}
