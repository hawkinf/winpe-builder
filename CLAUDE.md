# CLAUDE.md — WinPE Builder

As regras deste projeto valem para QUALQUER IA e estão em **`AGENTS.md`** (leia primeiro, é obrigatório).
Estado atual, pendências e log: **`docs/STATUS.md`**. Mapa do código: **`docs/ARCHITECTURE.md`**.

Resumo das obrigações (detalhe em `AGENTS.md`):
1. Antes de trabalhar: ler `AGENTS.md`, `docs/STATUS.md`, `docs/ARCHITECTURE.md`, `git log`; resumir ao usuário o que entendeu.
2. Depois de mudar algo: atualizar `docs/STATUS.md` (estado, pendências, linha no Log) no MESMO commit.
3. Regras globais do usuário (`~/.claude/CLAUDE.md`) continuam valendo; em conflito, vale o mais específico deste projeto.

Stack: C# .NET (WPF) + Windows ADK/WinPE add-on (copype, DISM, oscdimg)

Comandos:
dotnet build src/WinPEBuilder.sln ; dotnet test
