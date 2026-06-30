//go:build freebsd

package generic

import "go-glpi-agent/internal/sysutil"

// osTimezoneName returns the IANA timezone name on FreeBSD, which stores it in
// /var/db/zoneinfo (a one-line text file written by tzsetup). FreeBSD has no
// /etc/timezone and /etc/localtime is a plain copy, not a symlink, so the
// generic resolution falls through to here.
func osTimezoneName() string {
	return sysutil.ReadFileTrim("/var/db/zoneinfo")
}
