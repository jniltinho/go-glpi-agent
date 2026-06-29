package generic

import (
	"context"
	"strings"

	"go-fusioninventory-agent/internal/collector"
	"go-fusioninventory-agent/internal/config"
	"go-fusioninventory-agent/internal/inventory"
	"go-fusioninventory-agent/internal/sysutil"
)

// usersCollector coleta três seções como o agente Perl:
//   - LOCAL_USERS  (/etc/passwd)
//   - LOCAL_GROUPS (/etc/group)
//   - USERS        (who: sessões logadas) + LASTLOGGEDUSER (last)
type usersCollector struct{}

func init() { collector.Register(usersCollector{}) }

func (usersCollector) Name() string                      { return "generic/users" }
func (usersCollector) Category() string                  { return "local_user" }
func (usersCollector) IsEnabled(cfg *config.Config) bool { return true }

func (usersCollector) Collect(ctx context.Context, inv *inventory.Inventory) error {
	collectLocalUsers(inv)
	collectLocalGroups(inv)
	collectLoggedUsers(ctx, inv)
	return nil
}

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
		// o agente Perl só reporta grupos que possuem membros
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

	// último usuário logado via `last`, pulando pseudo-registros do wtmp.
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

// pseudo-usuários que o `last` emite e não são logins reais.
var pseudoLastUser = map[string]bool{
	"reboot": true, "shutdown": true, "runlevel": true, "wtmp": true, "": true,
}
