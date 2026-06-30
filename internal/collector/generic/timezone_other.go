//go:build !windows

package generic

// osTimezoneName has no extra OS source on Unix-like platforms; the IANA name
// comes from /etc/timezone or the /etc/localtime symlink.
func osTimezoneName() string { return "" }
