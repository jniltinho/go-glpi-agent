package server

import (
	"bytes"
	"compress/zlib"
	"context"
	"crypto/tls"
	"crypto/x509"
	"fmt"
	"io"
	"net/http"
	"net/url"
	"os"
	"time"

	"go-glpi-agent/internal/config"
	"go-glpi-agent/internal/inventory"
	"go-glpi-agent/internal/logger"
	"go-glpi-agent/internal/version"
)

// Target implements transport.Target, sending the inventory to a GLPI server.
// It tries the native JSON protocol (GLPI 10+) first and falls back to the
// legacy XML/PROLOG flow for servers running the OCS/FusionInventory plugin.
type Target struct {
	url     string
	cfg     *config.Config
	log     *logger.Logger
	client  *http.Client
	agentID string // sent as the GLPI-Agent-ID header (set per Send)

	// PrologFreq is updated from the PROLOG reply (in hours).
	PrologFreq int
}

// New creates a server Target from the configuration.
func New(cfg *config.Config, log *logger.Logger) (*Target, error) {
	tlsCfg, err := buildTLSConfig(cfg)
	if err != nil {
		return nil, err
	}

	tr := &http.Transport{TLSClientConfig: tlsCfg}
	if cfg.Proxy != "" {
		if pu, perr := url.Parse(cfg.Proxy); perr == nil {
			tr.Proxy = http.ProxyURL(pu)
		}
	} else {
		tr.Proxy = http.ProxyFromEnvironment
	}

	timeout := time.Duration(cfg.Timeout) * time.Second
	if timeout <= 0 {
		timeout = 180 * time.Second
	}

	return &Target{
		url:    cfg.Server,
		cfg:    cfg,
		log:    log,
		client: &http.Client{Transport: tr, Timeout: timeout},
	}, nil
}

func buildTLSConfig(cfg *config.Config) (*tls.Config, error) {
	tlsCfg := &tls.Config{InsecureSkipVerify: cfg.NoSSLCheck}
	if cfg.CACertFile == "" && cfg.CACertDir == "" {
		return tlsCfg, nil
	}
	pool, err := x509.SystemCertPool()
	if err != nil || pool == nil {
		pool = x509.NewCertPool()
	}
	if cfg.CACertFile != "" {
		pem, rerr := os.ReadFile(cfg.CACertFile)
		if rerr != nil {
			return nil, fmt.Errorf("ca-cert-file: %w", rerr)
		}
		pool.AppendCertsFromPEM(pem)
	}
	if cfg.CACertDir != "" {
		entries, _ := os.ReadDir(cfg.CACertDir)
		for _, e := range entries {
			if e.IsDir() {
				continue
			}
			if pem, rerr := os.ReadFile(cfg.CACertDir + "/" + e.Name()); rerr == nil {
				pool.AppendCertsFromPEM(pem)
			}
		}
	}
	tlsCfg.RootCAs = pool
	return tlsCfg, nil
}

// Send delivers the inventory. It probes the server with a native CONTACT
// request: if the server answers as a GLPI 10+ native server, the inventory is
// sent as JSON; otherwise it falls back to the legacy PROLOG + XML flow.
func (t *Target) Send(ctx context.Context, inv *inventory.Inventory) error {
	t.agentID = inv.AgentID

	native, wantInventory, err := t.contact(ctx, inv)
	if err != nil {
		t.log.Debug("contact failed (%v); falling back to legacy XML/PROLOG", err)
		return t.sendLegacy(ctx, inv)
	}
	if !native {
		t.log.Info("server does not speak the native protocol; using legacy XML/PROLOG")
		return t.sendLegacy(ctx, inv)
	}
	if !wantInventory && t.cfg.Lazy && !t.cfg.Force {
		t.log.Info("server did not request an inventory (lazy); skipping this cycle")
		return nil
	}
	return t.sendNative(ctx, inv)
}

// sendNative serializes the inventory as JSON and posts it to the GLPI native
// endpoint with the GLPI-Agent-ID header.
func (t *Target) sendNative(ctx context.Context, inv *inventory.Inventory) error {
	body, err := BuildInventoryJSON(inv)
	if err != nil {
		return fmt.Errorf("json serialize: %w", err)
	}
	// Debug aid: GFI_DUMP_JSON=<file> writes the raw inventory JSON to disk so it
	// can be validated against GLPI's inventory.schema.json offline.
	if path := os.Getenv("GFI_DUMP_JSON"); path != "" {
		_ = os.WriteFile(path, body, 0o644)
	}
	resp, err := t.postJSON(ctx, body)
	if err != nil {
		return err
	}
	t.log.Info("native JSON inventory sent to %s (%d bytes)", t.url, len(body))
	t.log.Debug("server reply: %d bytes", len(resp))
	return nil
}

// sendLegacy runs the legacy flow: PROLOG followed by the compressed XML
// inventory.
func (t *Target) sendLegacy(ctx context.Context, inv *inventory.Inventory) error {
	if err := t.prolog(ctx, inv.DeviceID); err != nil {
		return fmt.Errorf("prolog: %w", err)
	}
	body, err := Serialize(inv)
	if err != nil {
		return fmt.Errorf("serialize: %w", err)
	}
	resp, err := t.postXML(ctx, body)
	if err != nil {
		return err
	}
	t.log.Info("legacy XML inventory sent to %s (%d bytes)", t.url, len(body))
	t.log.Debug("server reply: %d bytes", len(resp))
	return nil
}

// postXML posts a zlib-compressed XML body (legacy protocol).
func (t *Target) postXML(ctx context.Context, xmlBody []byte) ([]byte, error) {
	return t.post(ctx, xmlBody, "application/x-compress-zlib", version.UserAgent(), true)
}

// postJSON posts a JSON body using the native protocol, honoring the
// no-compression setting. The default is zlib (application/x-compress-zlib).
func (t *Target) postJSON(ctx context.Context, jsonBody []byte) ([]byte, error) {
	if t.cfg.NoCompression {
		return t.post(ctx, jsonBody, "application/json", version.GLPIUserAgent(), false)
	}
	return t.post(ctx, jsonBody, "application/x-compress-zlib", version.GLPIUserAgent(), true)
}

// post is the low-level HTTP POST. When compress is true the body is wrapped in
// zlib. It always sets the GLPI-Agent-ID header when an agentID is known, and
// transparently decompresses a zlib reply.
func (t *Target) post(ctx context.Context, body []byte, contentType, userAgent string, compress bool) ([]byte, error) {
	payload := body
	if compress {
		var buf bytes.Buffer
		zw := zlib.NewWriter(&buf)
		if _, err := zw.Write(body); err != nil {
			return nil, err
		}
		if err := zw.Close(); err != nil {
			return nil, err
		}
		payload = buf.Bytes()
	}

	req, err := http.NewRequestWithContext(ctx, http.MethodPost, t.url, bytes.NewReader(payload))
	if err != nil {
		return nil, err
	}
	req.Header.Set("Content-Type", contentType)
	req.Header.Set("User-Agent", userAgent)
	if t.agentID != "" {
		req.Header.Set("GLPI-Agent-ID", t.agentID)
	}
	if t.cfg.User != "" {
		req.SetBasicAuth(t.cfg.User, t.cfg.Password)
	}

	resp, err := t.client.Do(req)
	if err != nil {
		return nil, fmt.Errorf("POST %s: %w", t.url, err)
	}
	defer resp.Body.Close()

	data, _ := io.ReadAll(resp.Body)
	if resp.StatusCode != http.StatusOK {
		snippet := decompress(data)
		if len(snippet) > 512 {
			snippet = snippet[:512]
		}
		return nil, fmt.Errorf("POST %s: status %d: %s", t.url, resp.StatusCode, snippet)
	}
	return decompress(data), nil
}

// decompress tries to inflate a zlib body; if it is not zlib, the data is
// returned unchanged.
func decompress(data []byte) []byte {
	zr, err := zlib.NewReader(bytes.NewReader(data))
	if err != nil {
		return data
	}
	defer zr.Close()
	out, err := io.ReadAll(zr)
	if err != nil {
		return data
	}
	return out
}
