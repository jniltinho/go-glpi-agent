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

	"go-fusioninventory-agent/internal/config"
	"go-fusioninventory-agent/internal/inventory"
	"go-fusioninventory-agent/internal/logger"
	"go-fusioninventory-agent/internal/version"
)

// Target implementa transport.Target enviando o inventário a um servidor GLPI,
// precedido pelo fluxo PROLOG.
type Target struct {
	url    string
	cfg    *config.Config
	log    *logger.Logger
	client *http.Client

	// PrologFreq é atualizado a partir da resposta PROLOG (em horas).
	PrologFreq int
}

// New cria um Target server a partir da configuração.
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

// Send executa PROLOG e, em seguida, envia o inventário.
func (t *Target) Send(ctx context.Context, inv *inventory.Inventory) error {
	if err := t.prolog(ctx, inv.DeviceID); err != nil {
		return fmt.Errorf("prolog: %w", err)
	}

	body, err := Serialize(inv)
	if err != nil {
		return fmt.Errorf("serialize: %w", err)
	}

	resp, err := t.post(ctx, body)
	if err != nil {
		return err
	}
	t.log.Info("inventário enviado para %s (%d bytes)", t.url, len(body))
	t.log.Debug("resposta do servidor: %d bytes", len(resp))
	return nil
}

// post envia um corpo XML comprimido com zlib ao servidor e retorna a resposta.
func (t *Target) post(ctx context.Context, xmlBody []byte) ([]byte, error) {
	var buf bytes.Buffer
	zw := zlib.NewWriter(&buf)
	if _, err := zw.Write(xmlBody); err != nil {
		return nil, err
	}
	if err := zw.Close(); err != nil {
		return nil, err
	}

	req, err := http.NewRequestWithContext(ctx, http.MethodPost, t.url, &buf)
	if err != nil {
		return nil, err
	}
	req.Header.Set("Content-Type", "application/x-compress-zlib")
	req.Header.Set("User-Agent", version.UserAgent())
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
		return nil, fmt.Errorf("POST %s: status %d", t.url, resp.StatusCode)
	}
	return decompress(data), nil
}

// decompress tenta descomprimir zlib; se falhar, retorna os dados como estão.
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
