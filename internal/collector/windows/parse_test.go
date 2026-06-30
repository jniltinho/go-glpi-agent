package windows

import "testing"

func TestUninstallEntryToSoftware(t *testing.T) {
	// Skipped: no display name, and system components.
	if _, ok := (uninstallEntry{DisplayVersion: "1.0"}).toSoftware("registry"); ok {
		t.Error("entry with no DisplayName should be skipped")
	}
	if _, ok := (uninstallEntry{DisplayName: "Hidden", SystemComponent: 1}).toSoftware("registry"); ok {
		t.Error("SystemComponent=1 should be skipped")
	}

	e := uninstallEntry{
		DisplayName: "7-Zip", DisplayVersion: "23.01", Publisher: "Igor Pavlov",
		InstallDate: "20250711", EstimatedSizeKB: 5000,
	}
	sw, ok := e.toSoftware("registry-wow64")
	if !ok {
		t.Fatal("valid entry should map")
	}
	if sw.Name != "7-Zip" || sw.Version != "23.01" || sw.Publisher != "Igor Pavlov" {
		t.Errorf("mapping = %+v", sw)
	}
	if sw.From != "registry-wow64" {
		t.Errorf("From = %q", sw.From)
	}
	if sw.InstallDate != "2025-07-11" {
		t.Errorf("InstallDate = %q, expected 2025-07-11", sw.InstallDate)
	}
	if sw.FileSize != 5000*1024 {
		t.Errorf("FileSize = %d, expected %d (KB->bytes)", sw.FileSize, 5000*1024)
	}
}

func TestDashDate(t *testing.T) {
	cases := map[string]string{
		"20250711":   "2025-07-11",
		"":           "",
		"2025-07-11": "2025-07-11", // already dashed: left untouched
		"July 2025":  "July 2025",  // non-numeric: left untouched
	}
	for in, want := range cases {
		if got := dashDate(in); got != want {
			t.Errorf("dashDate(%q) = %q, expected %q", in, got, want)
		}
	}
}

func TestCIMDate(t *testing.T) {
	if got := cimDate("20210510000000.000000+000"); got != "2021-05-10" {
		t.Errorf("cimDate = %q, expected 2021-05-10", got)
	}
	if got := cimDate("xx"); got != "" {
		t.Errorf("cimDate(short) = %q, expected empty", got)
	}
}

func TestParseUSBID(t *testing.T) {
	vid, pid, serial := parseUSBID(`USB\VID_046D&PID_C52B\5&1A2B3C4D&0&2`)
	if vid != "046d" || pid != "c52b" {
		t.Errorf("vid/pid = %q/%q, expected 046d/c52b", vid, pid)
	}
	if serial != "" {
		t.Errorf("serial = %q, expected empty for an enumerated path", serial)
	}

	// Instance id with a real serial (no '&' in the last segment).
	vid, pid, serial = parseUSBID(`USB\VID_0781&PID_5567\4C530001234567890123`)
	if vid != "0781" || pid != "5567" || serial != "4C530001234567890123" {
		t.Errorf("got %q/%q/%q", vid, pid, serial)
	}

	// PID followed by a backslash directly.
	vid, pid, _ = parseUSBID(`USB\VID_8087&PID_0029`)
	if vid != "8087" || pid != "0029" {
		t.Errorf("vid/pid = %q/%q, expected 8087/0029", vid, pid)
	}
}
