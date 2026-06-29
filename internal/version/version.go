// Package version centraliza a versão do agente e a string de User-Agent
// usada na comunicação com o GLPI.
package version

// Version é a versão do agente Go. Sobrescrita no build via -ldflags.
var Version = "0.1.0-dev"

// Name é o nome do agente.
const Name = "go-fusioninventory-agent"

// UserAgent retorna a string enviada como VERSIONCLIENT no XML e como
// header User-Agent nas requisições HTTP. O GLPI espera o prefixo
// "FusionInventory-Agent" para reconhecer o cliente.
func UserAgent() string {
	return "FusionInventory-Agent_v" + Version + " (" + Name + ")"
}
