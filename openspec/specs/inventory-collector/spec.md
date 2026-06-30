# inventory-collector Specification

## Purpose

Motor de inventário: interface `Collector` plugável, execução concorrente com
timeout por coletor e desabilitação de categorias via configuração.

## Requirements

### Requirement: Interface Collector plugável
O motor de inventário SHALL definir uma interface `Collector` com métodos `Name() string`, `IsEnabled(cfg *config.Config) bool`, e `Collect(ctx context.Context, inv *Inventory) error`. Todo coletor SHALL implementar essa interface.

#### Scenario: Coletor desabilitado é ignorado
- **WHEN** `IsEnabled()` retorna `false` para um coletor
- **THEN** o motor não chama `Collect()` e não loga erro

#### Scenario: Coletor habilitado é executado
- **WHEN** `IsEnabled()` retorna `true`
- **THEN** o motor chama `Collect()` e inclui os dados no inventário

### Requirement: Execução concorrente com timeout
O motor SHALL executar todos os coletores habilitados em paralelo via goroutines. Cada coletor SHALL ter um timeout individual configurável via `backend-collect-timeout` do `agent.cfg` (padrão **180s**, igual ao Perl). Um coletor que excede o timeout SHALL ser cancelado e um aviso SHALL ser logado; os demais coletores continuam normalmente.

#### Scenario: Coletor lento não bloqueia os demais
- **WHEN** um coletor demora mais que `backend-collect-timeout`
- **THEN** é cancelado via context, os outros coletores concluem normalmente, e o inventário é enviado com os dados disponíveis

#### Scenario: Erro em coletor não cancela os demais
- **WHEN** um coletor retorna erro
- **THEN** o erro é logado como warning, os outros coletores continuam, e o inventário é enviado

### Requirement: Desabilitar categorias via configuração
O agente SHALL suportar `--no-category <categoria>` (e `no-category` no `agent.cfg`) para excluir coletores. Categorias válidas no v1: `cpu`, `memory`, `storage`, `drive`, `network`, `software`, `bios`, `usb`, `process`, `user`, `local_user`, `local_group`, `lvm`, `hostname`, `timezone`, `os`, `slot`, `controller`, `printer`, `monitor`, `video`, `firewall`.

Categorias sem coletor implementado no v1 SHALL ser aceitas silenciosamente (sem erro). As categorias `local_user`, `local_group` e `user` SHALL ser independentes — desabilitar uma não SHALL desabilitar as outras.

#### Scenario: Categoria excluída
- **WHEN** `--no-category software` é passado
- **THEN** nenhum coletor de software é executado e a seção SOFTWARE é omitida do inventário

#### Scenario: Categoria local_group excluída
- **WHEN** `--no-category local_group` é passado
- **THEN** LOCAL_GROUPS é omitido, mas LOCAL_USERS e USERS continuam sendo coletados

#### Scenario: Categoria v2 ignorada
- **WHEN** `--no-category video` é passado (coletor v2)
- **THEN** o agente aceita a flag sem erro (coletor já não existe no v1)
