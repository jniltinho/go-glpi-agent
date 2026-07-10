## MODIFIED Requirements

### Requirement: Detecção de servidor nativo vs legado
Em modo `--server`, o agente SHALL detectar se o `server` configurado é um GLPI 10+ nativo ou um servidor legado (plugin OCS/FusionInventory) e selecionar o protocolo correspondente. O protocolo nativo SHALL ser tentado primeiro. O agente SHALL NOT fazer fallback para o protocolo legado quando o servidor responder com um erro JSON nativo (corpo iniciando com `{`, ex.: `400 "JSON not well formed!"` ou `500` de validação de schema): nesse caso o erro real do servidor SHALL ser propagado ao chamador. O fallback legado SHALL ocorrer apenas quando o servidor não fala o protocolo nativo (erro de transporte, ou resposta não-JSON como HTML/404).

#### Scenario: Servidor GLPI 10+ nativo
- **WHEN** o servidor responde ao CONTACT como GLPI nativo
- **THEN** o agente usa protocolo JSON (CONTACT + inventário) com header `GLPI-Agent-ID`

#### Scenario: Servidor legado
- **WHEN** o CONTACT falha por transporte ou o servidor responde algo que não é JSON nativo (HTML, 404)
- **THEN** o agente faz fallback para o protocolo XML (PROLOG + INVENTORY)

#### Scenario: Erro JSON nativo não dispara fallback legado
- **WHEN** o servidor nativo responde ao CONTACT ou ao inventário com um corpo JSON de erro (ex.: `400 {"status":"error","message":"JSON not well formed!"}`)
- **THEN** o agente propaga o erro real do servidor e NÃO tenta o fluxo XML/PROLOG

### Requirement: Requisição CONTACT (protocolo nativo)
No protocolo nativo, antes do inventário, o agente SHALL enviar uma mensagem JSON com `action=contact` contendo `deviceid`, `name`, `version` (versão do agente), tasks instaladas/habilitadas (`installed-tasks`/`enabled-tasks`) e `tag`. Os campos `name` e `version` SHALL estar sempre presentes; sua ausência faz o GLPI 11 responder `400 "JSON not well formed!"`. O agente SHALL interpretar a resposta para decidir se envia o inventário neste ciclo (suporte a `lazy`).

#### Scenario: CONTACT aceito e inventário solicitado
- **WHEN** o servidor responde ao CONTACT confirmando suporte a inventário
- **THEN** o agente prossegue enviando o inventário em JSON

#### Scenario: CONTACT inclui version e name
- **WHEN** o agente monta a mensagem CONTACT
- **THEN** o JSON contém `version` (versão do agente) e `name` não vazios, além de `deviceid`, `installed-tasks` e `enabled-tasks`

#### Scenario: CONTACT indica que inventário não é necessário
- **WHEN** `lazy = 1` e a resposta do CONTACT indica que o inventário não é necessário neste ciclo
- **THEN** o agente pula o envio e aguarda o próximo ciclo

#### Scenario: CONTACT indica servidor sem inventário nativo
- **WHEN** a resposta indica suporte apenas ao plugin legado
- **THEN** o agente faz fallback para o fluxo XML/PROLOG

### Requirement: Serialização de inventário em JSON
No protocolo nativo, o agente SHALL serializar o inventário em JSON com `action=inventory`, `deviceid`, `itemtype` e `content` (as seções de inventário), conforme o esquema aceito pelo GLPI 10+. O envelope SHALL incluir apenas chaves de raiz previstas no schema (`deviceid`, `action`, `itemtype`, `tag`, `content`), e `content` SHALL conter apenas seções previstas no schema. A `tag` da entidade SHALL ser enviada como campo de raiz do envelope (irmã de `content`), e NÃO como `content.accountinfo` — essa chave não existe no schema nativo e é rejeitada com `500 "keys ignored: accountinfo"`. O mesmo modelo de dados interno alimenta tanto o serializador JSON quanto o XML.

#### Scenario: Inventário JSON aceito pelo GLPI 10+
- **WHEN** o JSON é enviado ao endpoint `/front/inventory.php` (ou URL configurada)
- **THEN** o GLPI aceita e cria/atualiza o computador sem erro de formato

#### Scenario: tag na raiz do envelope
- **WHEN** `tag` está configurada e o inventário é serializado em JSON nativo
- **THEN** o envelope inclui `tag` como campo de raiz e `content` NÃO contém a chave `accountinfo`

#### Scenario: Equivalência de campos com o XML
- **WHEN** os mesmos dados coletados são serializados em JSON e em XML
- **THEN** ambos contêm os mesmos campos/valores semânticos das seções implementadas (a `tag` aparece na raiz do JSON e em `ACCOUNTINFO/TAG` no XML legado)

### Requirement: Headers e compressão do protocolo nativo
O agente SHALL enviar `User-Agent: GLPI-Agent_v<versão>` e `GLPI-Agent-ID: <uuid>` no protocolo nativo. O corpo do protocolo nativo (CONTACT e inventário) SHALL ser enviado como `application/json` sem compressão por padrão, pois o endpoint nativo do GLPI 11 não infla um corpo zlib e responde `400 "JSON not well formed!"`. A compressão zlib SHALL ficar reservada ao fluxo legado (XML/PROLOG); o flag `no-compression` não altera o protocolo nativo, que já é enviado sem compressão.

#### Scenario: Envio nativo sem compressão
- **WHEN** o agente envia CONTACT ou inventário pelo protocolo nativo
- **THEN** o corpo é `application/json` não comprimido e o GLPI 11 o aceita

#### Scenario: Compressão restrita ao legado
- **WHEN** o agente faz fallback para o fluxo XML/PROLOG
- **THEN** o corpo XML é comprimido com zlib (`Content-Type: application/x-compress-zlib`), sem afetar o protocolo nativo
