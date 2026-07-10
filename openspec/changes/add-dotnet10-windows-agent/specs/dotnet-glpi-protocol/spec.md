## ADDED Requirements

### Requirement: Native GLPI CONTACT negotiation
The client SHALL initiate communication with a GLPI target using a native JSON CONTACT message containing the device ID, agent name/version, tag when configured, and only the inventory task. It SHALL validate the server response before deciding whether and when to send inventory.

#### Scenario: Native server requests inventory
- **WHEN** a GLPI 10 or 11 server returns a valid native CONTACT response requesting inventory
- **THEN** the client submits one native inventory JSON message using the negotiated request context

### Requirement: Persistent protocol identity headers
Native requests SHALL include the persisted agent UUID in `GLPI-Agent-ID`, a valid agent user agent, and a correlation/request ID when supported. Invalid or missing persistent agent identity MUST stop the native request before network transmission.

#### Scenario: Send with persistent agent ID
- **WHEN** the client submits native CONTACT and inventory messages in separate process runs
- **THEN** both runs use the same valid `GLPI-Agent-ID` value

### Requirement: Schema-valid native inventory
The native serializer SHALL generate the GLPI inventory envelope and normalize content types, required fields, dates, architecture, enumerations, booleans, integers, and empty sections according to the exact schema extracted from each supported GLPI container version.

#### Scenario: Validate Windows inventory against GLPI schema
- **WHEN** a representative Windows snapshot is serialized for a supported GLPI version
- **THEN** the generated JSON passes that version's `inventory.schema.json` without modification

### Requirement: Pending response handling
The client SHALL honor valid native `pending` responses by polling with the server request ID after a bounded expiration delay. It MUST cap total polls and elapsed time, support cancellation, and avoid resending the inventory body during status polling.

#### Scenario: CONTACT is temporarily pending
- **WHEN** the server returns `pending` twice and then a valid inventory request
- **THEN** the client performs bounded request-ID polls, waits the permitted delay, and sends exactly one inventory body

### Requirement: Conservative legacy fallback
The client SHALL fall back from native JSON to legacy XML only for explicit protocol incompatibility or a recognizable legacy server response. It MUST NOT downgrade after TLS validation, authentication/authorization, rate-limit, timeout, malformed-success, or server-health errors.

#### Scenario: Legacy endpoint rejects native protocol
- **WHEN** the target explicitly reports that the native protocol is unsupported and is recognized as a legacy inventory endpoint
- **THEN** the client performs the legacy PROLOG/XML exchange

#### Scenario: Native authentication fails
- **WHEN** the native endpoint returns an authentication or authorization failure
- **THEN** the run fails with an actionable authentication error and sends no legacy inventory

### Requirement: Legacy PROLOG and inventory XML
The legacy serializer and transport SHALL generate OCS/FusionInventory-compatible PROLOG and inventory XML from the same typed snapshot, preserve the device ID and tag, support required compression/content types, and validate server responses.

#### Scenario: Submit to a legacy test endpoint
- **WHEN** legacy negotiation succeeds and the server requests an inventory
- **THEN** the client sends a compressed XML inventory containing the same stable identity and core categories as the native snapshot

### Requirement: Secure configurable HTTP transport
The transport SHALL support HTTP proxy, standard authentication, optional OAuth2 client credentials for GLPI 11 when enabled, system and custom CA trust, optional client certificates, request timeouts, zlib/gzip/no compression, and an explicit insecure-TLS opt-in warning. Credentials and private key material MUST never appear in logs or local inventory output.

#### Scenario: Use a private CA
- **WHEN** the server certificate chains to a configured private CA and hostname validation succeeds
- **THEN** native communication succeeds without disabling certificate validation

#### Scenario: Reject an untrusted server
- **WHEN** certificate validation fails and insecure TLS is not explicitly enabled
- **THEN** the client sends no inventory, does not attempt legacy downgrade, and reports the trust failure

### Requirement: Bounded retry and submission semantics
Retries SHALL be bounded, cancellation-aware, and limited to safe contact/poll operations or failures known to occur before inventory acceptance. The client MUST avoid blind inventory-body retries that could create duplicate processing and SHALL return categorized transport errors.

#### Scenario: Connection drops after inventory upload
- **WHEN** the client cannot determine whether the server accepted the uploaded inventory
- **THEN** it reports an indeterminate submission and does not blindly resend the inventory in the same cycle

