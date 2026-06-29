package server

import (
	"context"
	"encoding/xml"
	"strconv"
)

// prologRequest is the <REQUEST> envelope with QUERY=PROLOG.
type prologRequest struct {
	XMLName  xml.Name `xml:"REQUEST"`
	DeviceID string   `xml:"DEVICEID"`
	Query    string   `xml:"QUERY"`
	Token    string   `xml:"TOKEN,omitempty"`
}

// prologReply is the GLPI reply to PROLOG. PROLOG_FREQ (hours) tunes the daemon
// interval.
type prologReply struct {
	XMLName    xml.Name `xml:"REPLY"`
	PrologFreq string   `xml:"PROLOG_FREQ"`
}

// prolog sends the PROLOG request and, if the reply contains PROLOG_FREQ,
// updates t.PrologFreq.
func (t *Target) prolog(ctx context.Context, deviceID string) error {
	req := prologRequest{DeviceID: deviceID, Query: "PROLOG"}
	body, err := xml.Marshal(req)
	if err != nil {
		return err
	}
	body = append([]byte(xml.Header), body...)

	resp, err := t.postXML(ctx, body)
	if err != nil {
		return err
	}

	var reply prologReply
	if err := xml.Unmarshal(resp, &reply); err != nil {
		// a reply without a readable PROLOG_FREQ is not fatal
		t.log.Debug("PROLOG without a readable PROLOG_FREQ: %v", err)
		return nil
	}
	if reply.PrologFreq != "" {
		if freq, perr := strconv.Atoi(reply.PrologFreq); perr == nil && freq > 0 {
			t.PrologFreq = freq
			t.log.Debug("received PROLOG_FREQ: %d hours", freq)
		}
	}
	return nil
}
