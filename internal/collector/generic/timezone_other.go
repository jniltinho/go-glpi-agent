//go:build !windows && !freebsd

package generic

// osTimezoneName has no extra OS source on Linux and most Unix platforms; the
// IANA name comes from /etc/timezone or the /etc/localtime symlink. FreeBSD has
// its own source (see timezone_freebsd.go).
func osTimezoneName() string { return "" }
