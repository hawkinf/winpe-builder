# STATUS — estado vivo de WinPE Builder

> **REGRA OBRIGATÓRIA (qualquer IA ou pessoa):** toda tarefa que altere este repositório DEVE terminar atualizando este arquivo
> ("Estado atual", "Pendências" e uma linha no "Log") **no mesmo commit** da mudança. Veja `AGENTS.md`.
> Se o estado aqui divergir do código, o código vale — corrija este arquivo.

Última atualização: 2026-11-11 (Claude Code).

## Objetivo
Programa GUI em C# que baixa/obtém os arquivos essenciais do WinPE (x86/x64), injeta arquivos do usuário e gera a ISO.

## Estado atual
| Módulo / área | Estado | Observações |
|---|---|---|
| Base WinPE Test Lab (PhoenixPE + scripts UIA em `scripts/`) | ✅ | pré-existente; coexiste com o app novo |
| WinPEBuilder (GUI C#) | ⏳ | planejado — ver Pendências |

Legenda: ✅ pronto · 🔄 em andamento · ⏳ não iniciado · ⚠️ com problema.

## Como rodar / validar
dotnet build src/WinPEBuilder.sln ; dotnet test

## Decisões de arquitetura (não reverter sem motivo)
- 2026-11-11 — app em C# (SDK .NET 10 instalado), código em `src/`. Fonte dos arquivos WinPE: Windows ADK + WinPE add-on (copype/oscdimg/DISM). O ADK local só tem amd64; x86 exige outra fonte (a decidir).

## Pendências / riscos conhecidos
- Definir fonte do WinPE x86 (ADK antigo com x86, ou boot.wim de ISO Windows 32-bit) — responsável: dono do projeto
- Implementar o WinPEBuilder em micro-passos (solução, detecção/instalação do ADK, injeção de arquivos, geração da ISO, GUI) — qualquer IA

## Como continuar (qualquer IA)
1. Leia `AGENTS.md`, este arquivo e `docs/ARCHITECTURE.md`.
2. `git status`, `git log --oneline -15`.
3. Pegue a primeira pendência acima (ou pergunte ao usuário) e trabalhe em passos pequenos.
4. Atualize este arquivo e commite junto com a mudança.

## Log (mais recente primeiro; acrescente uma linha por tarefa)
- 2026-11-11 — Claude Code — padrão de handoff instalado e STATUS preenchido (plano do WinPEBuilder definido). Validar: `pwsh scripts/check-handoff.ps1`.
