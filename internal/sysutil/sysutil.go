// Package sysutil reúne helpers para executar comandos externos e ler
// arquivos de /proc, /sys e /etc com tratamento gracioso de erros.
package sysutil

import (
	"bufio"
	"context"
	"os"
	"os/exec"
	"strings"
)

// CommandExists informa se um binário está disponível no PATH.
func CommandExists(name string) bool {
	_, err := exec.LookPath(name)
	return err == nil
}

// RunContext executa um comando respeitando o contexto (timeout/cancel) e
// retorna stdout. Erros são retornados ao chamador para decisão.
func RunContext(ctx context.Context, name string, args ...string) (string, error) {
	cmd := exec.CommandContext(ctx, name, args...)
	out, err := cmd.Output()
	return string(out), err
}

// RunLines executa um comando e retorna stdout dividido em linhas não vazias.
func RunLines(ctx context.Context, name string, args ...string) ([]string, error) {
	out, err := RunContext(ctx, name, args...)
	if err != nil {
		return nil, err
	}
	return SplitLines(out), nil
}

// SplitLines divide texto em linhas, descartando linhas vazias no fim.
func SplitLines(s string) []string {
	var lines []string
	sc := bufio.NewScanner(strings.NewReader(s))
	sc.Buffer(make([]byte, 0, 64*1024), 4*1024*1024)
	for sc.Scan() {
		lines = append(lines, sc.Text())
	}
	return lines
}

// ReadFileTrim lê um arquivo e retorna seu conteúdo sem espaços nas pontas.
// Retorna string vazia se o arquivo não existir ou não puder ser lido.
func ReadFileTrim(path string) string {
	b, err := os.ReadFile(path)
	if err != nil {
		return ""
	}
	return strings.TrimSpace(string(b))
}

// FileExists informa se um caminho existe.
func FileExists(path string) bool {
	_, err := os.Stat(path)
	return err == nil
}
