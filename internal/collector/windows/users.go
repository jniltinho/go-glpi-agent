//go:build windows

package windows

import (
	"context"
	"runtime"
	"strings"

	"go-glpi-agent/internal/collector"
	"go-glpi-agent/internal/config"
	"go-glpi-agent/internal/inventory"
)

// usersCollector collects local users and groups via WMI, plus the currently
// logged-in user from Win32_ComputerSystem.UserName.
type usersCollector struct{}

// init registers the users collector with the collector registry.
func init() { collector.Register(usersCollector{}) }

// Name returns the collector's registry name.
func (usersCollector) Name() string { return "windows/users" }

// Category returns the inventory section this collector fills.
func (usersCollector) Category() string { return "local_user" }

// IsEnabled reports whether the collector should run; it is Windows-only.
func (usersCollector) IsEnabled(cfg *config.Config) bool { return runtime.GOOS == "windows" }

type win32UserAccount struct {
	Name     string
	SID      string
	FullName string
}

type win32Group struct {
	Name string
	SID  string
}

type win32ComputerSystemUser struct {
	UserName string // "DOMAIN\\user", empty when no interactive session
}

// Collect adds local users, local groups, and the logged-in user. Each query
// degrades silently so one failure does not drop the others.
func (usersCollector) Collect(ctx context.Context, inv *inventory.Inventory) error {
	var users []win32UserAccount
	if err := queryWMI("SELECT Name, SID, FullName FROM Win32_UserAccount WHERE LocalAccount = TRUE", &users); err == nil {
		for _, u := range users {
			inv.AddLocalUser(inventory.LocalUser{
				Login: u.Name,
				ID:    u.SID,
				Name:  u.FullName,
			})
		}
	}

	var groups []win32Group
	if err := queryWMI("SELECT Name, SID FROM Win32_Group WHERE LocalAccount = TRUE", &groups); err == nil {
		for _, g := range groups {
			inv.AddLocalGroup(inventory.LocalGroup{
				ID:   g.SID,
				Name: g.Name,
			})
		}
	}

	var cs []win32ComputerSystemUser
	if err := queryWMI("SELECT UserName FROM Win32_ComputerSystem", &cs); err == nil && len(cs) > 0 {
		if login := loginName(cs[0].UserName); login != "" {
			inv.SetHardware(func(h *inventory.Hardware) { h.LastLoggedUser = login })
		}
	}
	return nil
}

// loginName strips the "DOMAIN\" prefix from a Win32_ComputerSystem.UserName.
func loginName(userName string) string {
	userName = strings.TrimSpace(userName)
	if i := strings.LastIndexByte(userName, '\\'); i >= 0 {
		return userName[i+1:]
	}
	return userName
}
