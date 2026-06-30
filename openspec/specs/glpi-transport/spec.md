# glpi-transport Specification

## Purpose

Protocolo de fallback para servidores legados (plugin FusionInventory/OCS) e saída
local: requisição PROLOG, serialização XML compatível, envio HTTP/HTTPS, saída em
arquivo e autenticação HTTP básica.

## Requirements

> **Protocolo de fallback** para servidores com plugin FusionInventory/OCS e para
> saída local (`--local`). Não é o caminho padrão em `--server` contra GLPI 10+ —
> ver capability `glpi-native-protocol`.

### Requirement: Requisição PROLOG (apenas fallback legado)
Quando o servidor for detectado como legado (plugin OCS/FusionInventory), o agente SHALL enviar PROLOG antes do inventário XML. A requisição SHALL conter `<QUERY>PROLOG</QUERY>` e o `<DEVICEID>` persistente. Se a resposta contiver `PROLOG_FREQ`, o agente SHALL ajustar o `delaytime` do daemon (`PROLOG_FREQ * 3600` segundos). Em servidores GLPI 10+ nativos, PROLOG SHALL NOT ser enviado.

#### Scenario: PROLOG em servidor legado
- **WHEN** o fallback legado é acionado e o servidor retorna HTTP 200 com PROLOG_FREQ
- **THEN** o agente atualiza o delay do daemon e prossegue com o inventário XML

#### Scenario: PROLOG com falha de rede
- **WHEN** o servidor legado não responde ao PROLOG
- **THEN** o agente loga erro e retorna código não-zero em modo `--run-once`; em daemon, aguarda próximo ciclo

### Requirement: Serialização XML compatível com protocolo OCS/FusionInventory
O agente SHALL serializar o inventário em XML semanticamente equivalente ao gerado pelo agente Perl. O XML SHALL incluir o envelope `<REQUEST>` com `<DEVICEID>`, `<QUERY>INVENTORY</QUERY>`, `<TOKEN>`, e `<CONTENT>` contendo todas as seções de inventário coletadas. Se `tag` estiver configurado no `agent.cfg`, SHALL ser incluído no XML.

#### Scenario: XML aceito pelo plugin FusionInventory
- **WHEN** o inventário XML é enviado ao plugin FusionInventory (fallback legado)
- **THEN** o servidor aceita e processa o inventário sem erros de formato

#### Scenario: Compatibilidade de campos com agente Perl
- **WHEN** os mesmos dados de hardware são coletados pelo agente Go e pelo agente Perl
- **THEN** o XML gerado pelo Go contém os mesmos campos e valores que o Perl geraria para os coletores implementados

#### Scenario: Tag de entidade
- **WHEN** `tag = entity123` está no `agent.cfg`
- **THEN** o campo TAG é incluído no XML de inventário

### Requirement: Envio HTTP/HTTPS para servidor (fallback legado)
No fallback legado, o agente SHALL enviar o inventário XML via HTTP POST para a URL configurada em `server`. SHALL suportar HTTPS com validação de certificado configurável (`no-ssl-check`, `ca-cert-file`, `ca-cert-dir`). SHALL suportar proxy via `proxy` no `agent.cfg` ou variáveis de ambiente `http_proxy`/`https_proxy`. Timeout HTTP configurável via `timeout` (padrão 180s). User-Agent SHALL ser `FusionInventory-Agent_v<versão>`.

#### Scenario: Envio bem-sucedido
- **WHEN** o servidor retorna HTTP 200 com resposta XML válida
- **THEN** o agente loga sucesso e termina sem erro

#### Scenario: Falha de conexão
- **WHEN** o servidor não está acessível
- **THEN** o agente loga o erro com URL e código HTTP, e retorna erro não-zero em modo `--run-once`

#### Scenario: SSL com certificado customizado
- **WHEN** `ca-cert-file = /etc/ssl/certs/glpi-ca.pem` está configurado
- **THEN** o cliente HTTP usa esse CA para validar o certificado do servidor

#### Scenario: Compressão zlib
- **WHEN** compressão está habilitada (padrão, equivalente ao Perl)
- **THEN** o corpo do POST é comprimido com zlib e header `Content-type: application/x-compress-zlib` é enviado

### Requirement: Saída local em arquivo XML
O agente SHALL suportar `--local <path>` para salvar o XML de inventário em arquivo. SHALL criar o diretório se não existir. O arquivo SHALL ser nomeado `<DEVICEID>.xml`.

#### Scenario: Saída local
- **WHEN** `--local /tmp/inventory` é passado
- **THEN** cria `/tmp/inventory/<DEVICEID>.xml` com o inventário completo

### Requirement: Autenticação HTTP básica
O agente SHALL suportar autenticação HTTP básica via `user` e `password` no `agent.cfg` ou flags `--user`/`--password`.

#### Scenario: Servidor com autenticação
- **WHEN** `user = admin` e `password = secret` estão configurados
- **THEN** o header `Authorization: Basic <base64>` é incluído no POST
