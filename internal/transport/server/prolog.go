package server

import (
	"context"
	"encoding/xml"
	"strconv"
)

// prologRequest é o envelope <REQUEST> com QUERY=PROLOG.
type prologRequest struct {
	XMLName  xml.Name `xml:"REQUEST"`
	DeviceID string   `xml:"DEVICEID"`
	Query    string   `xml:"QUERY"`
	Token    string   `xml:"TOKEN,omitempty"`
}

// prologReply é a resposta do GLPI ao PROLOG. PROLOG_FREQ (horas) ajusta o
// intervalo do daemon.
type prologReply struct {
	XMLName    xml.Name `xml:"REPLY"`
	PrologFreq string   `xml:"PROLOG_FREQ"`
}

// prolog envia a requisição PROLOG ao servidor e, se a resposta contiver
// PROLOG_FREQ, atualiza t.PrologFreq.
func (t *Target) prolog(ctx context.Context, deviceID string) error {
	req := prologRequest{DeviceID: deviceID, Query: "PROLOG"}
	body, err := xml.Marshal(req)
	if err != nil {
		return err
	}
	body = append([]byte(xml.Header), body...)

	resp, err := t.post(ctx, body)
	if err != nil {
		return err
	}

	var reply prologReply
	if err := xml.Unmarshal(resp, &reply); err != nil {
		// resposta sem PROLOG_FREQ não é fatal
		t.log.Debug("PROLOG sem PROLOG_FREQ legível: %v", err)
		return nil
	}
	if reply.PrologFreq != "" {
		if freq, perr := strconv.Atoi(reply.PrologFreq); perr == nil && freq > 0 {
			t.PrologFreq = freq
			t.log.Debug("PROLOG_FREQ recebido: %d horas", freq)
		}
	}
	return nil
}
