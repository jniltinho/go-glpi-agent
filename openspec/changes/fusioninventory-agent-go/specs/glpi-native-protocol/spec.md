## ADDED Requirements

> **Protocolo primário** para `--server` contra GLPI 10+. Origem: análise do
> GLPI Agent 1.19 em `research-glpi-agent.md`. O XML/PROLOG (`glpi-transport`)
> é fallback automático para servidores legados.

### Requirement: Agent ID (UUID) persistente
O agente SHALL gerar um UUID v4 (`agentid`) na primeira execução, distinto do `deviceid`, e persisti-lo junto do estado em `{vardir}/FusionInventory-Agent.json`. O agentid SHALL ser enviado no header HTTP `GLPI-Agent-ID` em toda requisição ao servidor nativo. Na migração, se `GLPI-Agent.dump` contiver um agentid, o agente SHALL importá-lo.

#### Scenario: Primeira execução gera agentid
- **WHEN** nenhum agentid persistido existe
- **THEN** gera um UUID v4, persiste, e o envia no header `GLPI-Agent-ID`

#### Scenario: Import do agentid do GLPI Agent
- **WHEN** `GLPI-Agent.dump` existe com um agentid UUID
- **THEN** o agente importa esse agentid em vez de gerar um novo

#### Scenario: agentid estável entre execuções
- **WHEN** o agente roda novamente
- **THEN** reutiliza o mesmo agentid persistido

---

### Requirement: Detecção de servidor nativo vs legado
Em modo `--server`, o agente SHALL detectar se o `server` configurado é um GLPI 10+ nativo ou um servidor legado (plugin OCS/FusionInventory) e selecionar o protocolo correspondente. O protocolo nativo SHALL ser tentado primeiro.

#### Scenario: Servidor GLPI 10+ nativo
- **WHEN** o servidor responde ao CONTACT como GLPI nativo
- **THEN** o agente usa protocolo JSON (CONTACT + inventário) com header `GLPI-Agent-ID`

#### Scenario: Servidor legado
- **WHEN** o CONTACT falha ou o servidor é o plugin FusionInventory/OCS
- **THEN** o agente faz fallback para o protocolo XML (PROLOG + INVENTORY)

---

### Requirement: Requisição CONTACT (protocolo nativo)
No protocolo nativo, antes do inventário, o agente SHALL enviar uma mensagem JSON com `action=contact` contendo `deviceid`, tasks instaladas/habilitadas e `tag`. O agente SHALL interpretar a resposta para decidir se envia o inventário neste ciclo (suporte a `lazy`).

#### Scenario: CONTACT aceito e inventário solicitado
- **WHEN** o servidor responde ao CONTACT confirmando suporte a inventário
- **THEN** o agente prossegue enviando o inventário em JSON

#### Scenario: CONTACT indica que inventário não é necessário
- **WHEN** `lazy = 1` e a resposta do CONTACT indica que o inventário não é necessário neste ciclo
- **THEN** o agente pula o envio e aguarda o próximo ciclo

#### Scenario: CONTACT indica servidor sem inventário nativo
- **WHEN** a resposta indica suporte apenas ao plugin legado
- **THEN** o agente faz fallback para o fluxo XML/PROLOG

---

### Requirement: Serialização de inventário em JSON
No protocolo nativo, o agente SHALL serializar o inventário em JSON com `action=inventory`, `deviceid` e `content` (as seções de inventário), conforme o esquema aceito pelo GLPI 10+. O mesmo modelo de dados interno alimenta tanto o serializador JSON quanto o XML.

#### Scenario: Inventário JSON aceito pelo GLPI 10+
- **WHEN** o JSON é enviado ao endpoint `/front/inventory.php` (ou URL configurada)
- **THEN** o GLPI aceita e cria/atualiza o computador sem erro de formato

#### Scenario: Equivalência de campos com o XML
- **WHEN** os mesmos dados coletados são serializados em JSON e em XML
- **THEN** ambos contêm os mesmos campos/valores semânticos das seções implementadas

---

### Requirement: Headers e compressão do protocolo nativo
O agente SHALL enviar `User-Agent: GLPI-Agent_v<versão>` e `GLPI-Agent-ID: <uuid>` no protocolo nativo. O corpo SHALL ser comprimido com zlib por padrão (Content-Type `application/x-compress-zlib`), com gzip ou sem compressão como alternativas, respeitando `no-compression`.

#### Scenario: Envio comprimido com zlib
- **WHEN** a compressão está habilitada (padrão)
- **THEN** o corpo JSON é comprimido com zlib e o header `Content-Type: application/x-compress-zlib` é enviado

#### Scenario: Sem compressão
- **WHEN** `no-compression = 1`
- **THEN** o corpo é enviado como `application/json` sem compressão