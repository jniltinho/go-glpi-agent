# agent-core Specification

## Purpose

Núcleo do agente: leitura da configuração compatível com `agent.cfg`, device ID
persistente, logging com backends configuráveis, modo daemon e flags de linha de
comando — a base que orquestra os ciclos de inventário.

## Requirements

### Requirement: Leitura de configuração compatível com agent.cfg
O agente SHALL ler o arquivo `agent.cfg` no formato INI usado pelo agente Perl existente. Chaves desconhecidas SHALL ser ignoradas silenciosamente (com aviso em modo debug). O path padrão SHALL ser `/etc/fusioninventory/agent.cfg`, sobrescrito por `--conf-file`. O agente SHALL suportar a diretiva `include` para carregar arquivos de `conf.d/`.

#### Scenario: Arquivo de configuração encontrado
- **WHEN** o agente inicia com `--conf-file /etc/fusioninventory/agent.cfg`
- **THEN** lê as chaves `server`, `local`, `delaytime`, `lazy`, `force`, `tag`, `backend-collect-timeout`, `timeout`, `user`, `password`, `proxy`, `no-ssl-check`, `ca-cert-file`, `ca-cert-dir`, `logger`, `logfile`, `vardir` e aplica ao runtime

#### Scenario: Arquivo ausente com flag obrigatória
- **WHEN** o path do `--conf-file` não existe
- **THEN** o agente termina com erro descritivo e código de saída não-zero

#### Scenario: Include de conf.d
- **WHEN** o `agent.cfg` contém `include "conf.d/"`
- **THEN** carrega todos os arquivos `.cfg` do diretório e mescla parâmetros (arquivos posteriores sobrescrevem)

#### Scenario: Chave desconhecida no config
- **WHEN** o `agent.cfg` contém `no-httpd = 1` (fora de escopo v1)
- **THEN** o agente inicia normalmente sem erro

### Requirement: Device ID persistente compatível com Perl
O agente SHALL gerar um device ID no formato `{hostname}-{YYYY}-{MM}-{DD}-{HH}-{MM}-{SS}` na primeira execução e persistir em disco. Execuções subsequentes SHALL reutilizar o mesmo ID. O path de persistência SHALL ser `{vardir}/FusionInventory-Agent.json`, onde `vardir` padrão é `/var/lib/fusioninventory/agent`.

Na primeira execução, se `{vardir}/FusionInventory-Agent.dump` (storage Perl Storable) existir, o agente SHALL importar o `deviceid` existente em vez de gerar um novo. Se `{vardir}/GLPI-Agent.dump` existir, o agente SHALL também importar o `agentid` (UUID) quando presente.

#### Scenario: Primeira execução sem storage Perl
- **WHEN** nenhum arquivo de state existe
- **THEN** gera device ID no formato `hostname-2026-06-29-15-30-45`, salva em JSON, usa no inventário

#### Scenario: Migração de storage Perl
- **WHEN** `FusionInventory-Agent.dump` existe com deviceid `srv-web-2024-01-15-10-00-00`
- **THEN** importa esse deviceid para o JSON e não gera novo ID

#### Scenario: Execuções subsequentes
- **WHEN** `FusionInventory-Agent.json` já existe com deviceid válido
- **THEN** reutiliza o mesmo deviceid sem regenerar

### Requirement: Logger com backends configuráveis
O agente SHALL suportar logging para `stderr`, arquivo e `syslog`. O nível de log SHALL ser configurável: `debug`, `info`, `warning`, `error`. O backend padrão SHALL ser `stderr`.

#### Scenario: Log para arquivo
- **WHEN** configurado com `logfile = /var/log/fusioninventory.log`
- **THEN** todas as mensagens de log são escritas no arquivo especificado

#### Scenario: Log para syslog
- **WHEN** configurado com `logger = Syslog`
- **THEN** mensagens são enviadas via syslog com facility configurada em `logfacility` (padrão `LOG_USER`)

### Requirement: Modo daemon
O agente SHALL operar em loop quando invocado com `--daemon`, executando ciclos de inventário conforme `delaytime` (padrão 3600s). O intervalo MAY ser ajustado pela resposta do servidor (schedule do CONTACT no nativo; `PROLOG_FREQ` no legado). Em modo daemon com target server, SHALL respeitar `lazy = 1` para pular envio quando o servidor não solicitou inventário.

#### Scenario: Execução única (cron-friendly)
- **WHEN** invocado sem `--daemon`
- **THEN** executa um ciclo de inventário e termina com código 0

#### Scenario: Modo daemon
- **WHEN** invocado com `--daemon`
- **THEN** entra em loop, dorme `delaytime` segundos entre ciclos, responde a SIGTERM com shutdown gracioso

#### Scenario: Lazy mode (GLPI 10+ nativo)
- **WHEN** `lazy = 1` e a resposta do CONTACT indica que inventário não é necessário
- **THEN** o agente pula o envio e aguarda o próximo ciclo

#### Scenario: Lazy mode (fallback legado)
- **WHEN** `lazy = 1` e o schedule local ainda não expirou
- **THEN** o agente pula o envio e aguarda o próximo ciclo

### Requirement: Flags de linha de comando
O agente SHALL aceitar as seguintes flags: `--server <url>`, `--local <path>`, `--conf-file <path>`, `--daemon`, `--debug`, `--no-category <cat>`, `--force`, `--version`, `--run-once`, `--user <user>`, `--password <pass>`. Flags de linha de comando SHALL sobrescrever valores do `agent.cfg`.

#### Scenario: --version
- **WHEN** invocado com `--version`
- **THEN** imprime a versão do agente e termina com código 0

#### Scenario: --force
- **WHEN** invocado com `--force`
- **THEN** envia o inventário ao servidor mesmo que não tenha sido solicitado (ignora schedule)
