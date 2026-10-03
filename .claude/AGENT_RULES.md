# AGENT_RULES — WinPE Test Lab
Seguir o `~/.claude/CLAUDE.md` global. Um micro-passo por resposta, commit ao fim, anti-regressão.
- Build via PhoenixPE/PEBakery sobre `O:\isos\Win10_22H2_x64_uanttended.iso`; boot UEFI + BIOS.
- Binários de terceiros nunca vão para o git; baixar só com confirmação do usuário.
- Scripts em PowerShell (sem `Write-Host` de debug esquecido); validar com PSScriptAnalyzer se disponível.
