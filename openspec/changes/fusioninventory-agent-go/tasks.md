## 1. Estrutura do Repositório

- [x] 1.1 Mover os projetos de referência em Perl para `base/` (`base/perl/`, `base/glpi-agent/`)
- [x] 1.2 Colocar o novo projeto Go na raiz do repositório
- [x] 1.3 Inicializar módulo Go: `go mod init go-fusioninventory-agent` (`go.mod` na raiz)
- [x] 1.4 Adicionar dependência `github.com/shirou/gopsutil/v3` via `go get` e commitar `go.mod` + `go.sum`
- [x] 1.5 Criar estrutura de pacotes na raiz: `cmd/fusioninventory-agent/`, `internal/config/`, `internal/logger/`, `internal/agent/`, `internal/inventory/`, `internal/collector/`, `internal/transport/`, `testdata/`
- [x] 1.6 Atualizar `.gitignore` (raiz) e adicionar CI Go em `.github/workflows/go.yml` (CircleCI Perl de referência em `base/perl/.circleci/`)

## 2. Protocolo nativo GLPI 10+ (P0 — bloqueante para `--server`)

> Caminho padrão para GLPI 10+. Sem isto o agente não funciona contra inventário nativo.
> Referência: `research-glpi-agent.md`, capability `glpi-native-protocol`.

- [x] 2.1 Gerar/persistir `agentid` (UUID v4) em `FusionInventory-Agent.json` e importar de `GLPI-Agent.dump` quando existir (`internal/agent/storage.go`)
- [x] 2.2 Implementar serializador JSON do inventário em `internal/transport/server/json.go` (mesmo modelo de `internal/inventory`)
- [x] 2.3 Implementar requisição CONTACT (`action=contact`) e parsing da resposta de capacidades (`internal/transport/server/contact.go`)
- [x] 2.4 Detectar nativo vs legado e selecionar protocolo/cliente automaticamente (em `client.go`: CONTACT JSON → nativo; senão fallback XML/PROLOG)
- [x] 2.5 Enviar headers `GLPI-Agent-ID` e `User-Agent: GLPI-Agent_v<ver>`; POST em `/front/inventory.php` (ou URL configurada)
- [~] 2.6 Negociação de compressão respeitando `no-compression` — **feito**: zlib (padrão) e none; **falta**: gzip
- [x] 2.7 Integrar fluxo nativo no `Target.Send()`: CONTACT → inventário JSON (pular PROLOG quando nativo)
- [x] 2.8 Validar inventário JSON aceito pelo GLPI 10+ (validado no GLPI 10 docker: computador `linux-desktop` criado com 2888 softwares, 23 portas de rede, CPU e SO; 0 violações no `inventory.schema.json`)

## 3. Transport legado XML + saída local (fallback e `--local`)

> XML/PROLOG para plugin FusionInventory/OCS e modo `--local`. Parte já implementada.

- [ ] 3.1 Capturar XMLs de referência do agente Perl em hosts de teste e salvar em `testdata/golden/` (Ubuntu desktop, RHEL/Rocky servidor)
- [x] 3.2 Implementar structs Go com tags `encoding/xml` em `internal/transport/server/xml.go`
- [x] 3.3 Implementar serialização XML em `internal/transport/server/serialize.go`
- [x] 3.4 Testes de estrutura XML em `internal/transport/server/serialize_test.go` (evoluir para comparação vs golden 3.1)
- [x] 3.5 Implementar requisição PROLOG em `internal/transport/server/prolog.go` (usado só no fallback legado)
- [x] 3.6 Implementar cliente HTTP com HTTPS, proxy, auth básica, compressão zlib e SSL (`internal/transport/server/client.go`)
- [x] 3.7 Implementar transport local (saída `<DEVICEID>.xml`) em `internal/transport/local/local.go`

## 4. Core do Agente

- [x] 4.1 Implementar parser de `agent.cfg` (formato INI + `include`) em `internal/config/` (chaves v1 em `design.md` D7)
- [x] 4.2 Implementar logger com backends stderr, file e syslog em `internal/logger/`
- [x] 4.3 Implementar device ID formato Perl com persistência JSON e importação de `FusionInventory-Agent.dump` (`internal/agent/storage.go`)
- [x] 4.4 Implementar parsing de flags em `cmd/fusioninventory-agent/main.go` (`--server`, `--local`, `--conf-file`, `--daemon`, `--debug`, `--force`, `--version`, `--run-once`, `--no-category`)
- [~] 4.5 Implementar modo execução única e daemon com loop, SIGTERM e `delaytime` — **falta**: `lazy`, `force`, aplicar `PROLOG_FREQ`/schedule do servidor no daemon
- [ ] 4.6 Implementar `lazy = 1`: pular envio quando servidor não solicitou inventário (comportamento Perl)
- [ ] 4.7 Implementar `--force` / `force = 1`: enviar inventário mesmo sem solicitação do servidor
- [ ] 4.8 Aplicar intervalo retornado pelo servidor (`PROLOG_FREQ` no legado; schedule do CONTACT no nativo) ao `delaytime` do daemon
- [ ] 4.9 Adicionar flags CLI `--user` e `--password` (sobrescrevem `agent.cfg`)

## 5. Motor de Inventário

- [x] 5.1 Definir interface `Collector` e registry em `internal/collector/collector.go`
- [x] 5.2 Implementar execução concorrente com timeout por coletor (default 180s de `backend-collect-timeout`)
- [x] 5.3 Implementar struct `Inventory` thread-safe em `internal/inventory/model.go`
- [~] 5.4 Suporte a `no-category` — **falta**: granularidade `local_user` / `local_group` / `user` separadas (hoje um único coletor)

## 6. Coletores Generic

- [x] 6.1 Hostname e FQDN (`internal/collector/generic/hostname.go`)
- [x] 6.2 Timezone (`internal/collector/generic/timezone.go`)
- [~] 6.3 Usuários (`internal/collector/generic/users.go`) — **falta**: `DATELASTLOGGEDUSER` via `last`; split de categorias `no-category`
- [x] 6.4 Processos via `gopsutil/process` com `scan-processes` (`internal/collector/generic/processes.go`)

## 7. Coletores Linux — Hardware

- [x] 7.1 CPU (`internal/collector/linux/cpu.go`)
- [x] 7.2 RAM + slots dmidecode (`internal/collector/linux/memory.go`)
- [x] 7.3 BIOS/DMI (`internal/collector/linux/bios.go`)
- [x] 7.4 Discos (`internal/collector/linux/drives.go`)
- [x] 7.5 LVM (`internal/collector/linux/lvm.go`)
- [x] 7.6 USB (`internal/collector/linux/usb.go`)

## 8. Coletores Linux — Rede e SO

- [x] 8.1 Interfaces de rede (`internal/collector/linux/networks.go`)
- [x] 8.2 Gateway padrão (`internal/collector/linux/networks.go`)
- [x] 8.3 SO/distro (`internal/collector/linux/os.go`)

## 9. Coletores Linux — Software

- [x] 9.1 dpkg (`internal/collector/linux/software_dpkg.go`)
- [x] 9.2 RPM (`internal/collector/linux/software_rpm.go`)
- [x] 9.3 Pacman (`internal/collector/linux/software_pacman.go`)
- [x] 9.4 Detecção automática do gerenciador de pacotes

## 10. Empacotamento e Distribuição

- [x] 10.1 Criar `Makefile` com targets `build`, `test`, `install`, `package-deb`, `package-rpm`
- [x] 10.2 Criar unit systemd oneshot + timer em `contrib/fusioninventory-agent.service` e `.timer`
- [x] 10.3 Criar `Dockerfile` para testes de integração
- [x] 10.4 Documentar lacunas v1 vs Perl no `README.md`
- [x] 10.5 Criar `nfpm.yaml` para `package-deb`/`package-rpm` funcionarem de fato
- [x] 10.6 Incluir `.timer` no target `install` do Makefile

## 11. Validação

- [x] 11.1 Comparar XML Go vs Perl no mesmo host (Ubuntu 24.04)
- [x] 11.1b Infraestrutura `test/` (GLPI docker-compose, Vagrant Rocky 9 + Debian 12)
- [x] 11.2 Validar inventário **JSON** aceito pelo GLPI 10+ nativo (validado contra GLPI 10 em docker; ver 2.8)
- [ ] 11.3 Validar fallback XML/PROLOG contra plugin FusionInventory (se disponível no ambiente de teste)
- [~] 11.4 Testes unitários por coletor com mocks/fixtures (serialize/config/storage feitos; coletores pendentes)
- [x] 11.5 Migração device ID: import de `FusionInventory-Agent.dump`
- [ ] 11.6 Migração agentid: import de `GLPI-Agent.dump`
- [x] 11.7 `agent.cfg` existente (`tag`, `lazy`, SSL, `include`) lido sem erro

## 12. Melhorias incrementais (pós-v1 mínimo)

- [ ] 12.1 (P2) Opções SSL extras: `ssl-fingerprint`, mTLS (`ssl-cert-file`/`ssl-key-file`)
- [ ] 12.2 (P2) Rotação de log (`logfile-maxsize`)
- [ ] 12.3 (P3) OAuth2 GLPI 11: `oauth-client-id/secret`, Bearer com auto-refresh