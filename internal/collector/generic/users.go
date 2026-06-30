//go:build !windows

package generic

import (
	"context"
	"strings"

	"go-glpi-agent/internal/collector"
	"go-glpi-agent/internal/config"
	"go-glpi-agent/internal/inventory"
	"go-glpi-agent/internal/sysutil"
)

// usersCollector collects three sections like the Perl agent:
//   - LOCAL_USERS  (/etc/passwd)
//   - LOCAL_GROUPS (/etc/group)
//   - USERS        (who: logged-in sessions) + LASTLOGGEDUSER (last)
type usersCollector struct{}

// init registers the users collector with the collector registry.
func init() { collector.Register(usersCollector{}) }

// Name returns the collector identifier.
func (usersCollector) Name() string { return "generic/users" }

// Category returns the inventory category controlled by --no-category.
func (usersCollector) Category() string { return "local_user" }

// IsEnabled always returns true; users are collected on every host.
func (usersCollector) IsEnabled(cfg *config.Config) bool { return true }

// Collect gathers local users, local groups, and logged-in/last users in turn;
// each helper degrades silently when a file or tool is unavailable.
func (usersCollector) Collect(ctx context.Context, inv *inventory.Inventory) error {
	collectLocalUsers(inv)
	collectLocalGroups(inv)
	collectLoggedUsers(ctx, inv)
	return nil
}

// collectLocalUsers parses /etc/passwd, skipping comments and lines with fewer
// than the seven expected colon-separated fields.
func collectLocalUsers(inv *inventory.Inventory) {
	content := sysutil.ReadFileTrim("/etc/passwd")
	for _, line := range sysutil.SplitLines(content) {
		if strings.HasPrefix(line, "#") {
			continue
		}
		f := strings.Split(line, ":")
		if len(f) < 7 {
			continue
		}
		inv.AddLocalUser(inventory.LocalUser{
			Login: f[0],
			ID:    f[2],
			Name:  f[4],
			Home:  f[5],
			Shell: f[6],
		})
	}
}

// collectLocalGroups parses /etc/group, reporting only groups that list at
// least one member, mirroring the Perl agent's behavior.
func collectLocalGroups(inv *inventory.Inventory) {
	content := sysutil.ReadFileTrim("/etc/group")
	for _, line := range sysutil.SplitLines(content) {
		if strings.HasPrefix(line, "#") {
			continue
		}
		f := strings.Split(line, ":")
		if len(f) < 4 {
			continue
		}
		// the Perl agent only reports groups that have members
		if f[3] == "" {
			continue
		}
		inv.AddLocalGroup(inventory.LocalGroup{
			ID:     f[2],
			Name:   f[0],
			Member: strings.Split(f[3], ","),
		})
	}
}

// collectLoggedUsers records currently logged-in sessions via `who`
// (deduplicated by login) and the most recent real login via `last -R`,
// skipping pseudo records. Both tools are optional.
func collectLoggedUsers(ctx context.Context, inv *inventory.Inventory) {
	if sysutil.CommandExists("who") {
		out, err := sysutil.RunContext(ctx, "who")
		if err == nil {
			seen := map[string]bool{}
			for _, line := range sysutil.SplitLines(out) {
				f := strings.Fields(line)
				if len(f) == 0 || seen[f[0]] {
					continue
				}
				seen[f[0]] = true
				inv.AddUser(inventory.User{Login: f[0]})
			}
		}
	}

	// last logged-in user via `last`, skipping wtmp pseudo-records.
	if sysutil.CommandExists("last") {
		out, err := sysutil.RunContext(ctx, "last", "-R")
		if err == nil {
			for _, line := range sysutil.SplitLines(out) {
				f := strings.Fields(line)
				if len(f) == 0 || pseudoLastUser[f[0]] {
					continue
				}
				inv.SetHardware(func(h *inventory.Hardware) {
					h.LastLoggedUser = f[0]
				})
				break
			}
		}
	}
}

// pseudo-users that `last` emits and that are not real logins.
var pseudoLastUser = map[string]bool{
	"reboot": true, "shutdown": true, "runlevel": true, "wtmp": true, "": true,
}
