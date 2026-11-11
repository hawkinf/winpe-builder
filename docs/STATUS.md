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
| WinPEBuilder: solução (Core/App/Tests) | ✅ | passo 1; build 0 avisos, 3 testes ok |
| WinPEBuilder: detecção do ADK (`Core/Adk`) | ✅ | passo 2; 8 testes ok; painel de status na GUI |
| WinPEBuilder: instalação do ADK (`AdkInstaller`, botões + progresso na GUI) | ✅ | passo 3; 17 testes ok; download+instalação real NÃO testados (mexe no sistema) |
| WinPEBuilder: montagem do WinPE (`Core/Build`: copype ou boot.wim de ISO, pacotes, teclado, unmount) | ✅ | passo 4; 28 testes ok com runner simulado; execução real com DISM exige admin e NÃO foi feita |
| WinPEBuilder: injeção de arquivos (`StartnetScript`, modos OnIso/InsideImage) | ✅ | passo 5; 35 testes ok; lógica do startnet.cmd executada de verdade com subst; boot real no WinPE NÃO testado |
| WinPEBuilder: ISO (`BootFiles` + oscdimg) e gravação em pendrive (`UsbMediaWriter`) | ✅ | passo 6; 48 testes ok; oscdimg REAL validado (ISO com El Torito BIOS+UEFI); pendrive (MakeWinPEMedia /UFD, formata!) NÃO testado |
| WinPEBuilder: ícone + propriedades do arquivo (`app.ico`, `scripts/make-icon.ps1`) | ✅ | 1.0.0, Hawk Informática; ícone no .exe e nas janelas |
| WinPEBuilder: GUI completa (config, build, log em arquivo, cancelar, pendrive com confirmação digitada, settings.json) | ✅ | passo 7; 52 testes ok; janela abre e renderiza (screenshot), fechar grava settings; fluxo de geração real via GUI NÃO exercitado (exige admin) |

Legenda: ✅ pronto · 🔄 em andamento · ⏳ não iniciado · ⚠️ com problema.

## Como rodar / validar
dotnet build src/WinPEBuilder.sln ; dotnet test src/WinPEBuilder.sln

## Decisões de arquitetura (não reverter sem motivo)
- 2026-11-11 — app em C# (SDK .NET 10 instalado), código em `src/`. Fonte dos arquivos WinPE: Windows ADK + WinPE add-on (copype/oscdimg/DISM). O ADK local só tem amd64; x86 exige outra fonte (a decidir).
- 2026-11-11 — usuário escolheu opção C para x86: ADK antigo como padrão + boot.wim de ISO Windows 32-bit como alternativa.

## Pendências / riscos conhecidos
- Definir fonte do WinPE x86 (ADK antigo com x86, ou boot.wim de ISO Windows 32-bit) — responsável: dono do projeto
- Executar o app elevado e gerar uma ISO x64 de verdade (copype+DISM+oscdimg) e iniciá-la numa VM — responsável: dono do projeto
- Testar 32 bits: instalação do ADK 2004 em pasta privada e/ou origem boot.wim de ISO x86 — responsável: dono do projeto
- Ideias futuras: injeção de drivers, vários idiomas, ícone/tema, publicar binário (self-contained) — qualquer IA
- Gravação em pendrive nunca foi executada de verdade (formata o disco): testar primeiro com um pendrive vazio — responsável: dono do projeto
- Validar uma geração REAL (x64 via ADK) com o app elevado; só foi testado com runner simulado — responsável: dono do projeto
- Instalação real do ADK (x64 e x86 em pasta privada via /installpath) ainda não foi exercitada; o instalador pode recusar o ADK 2004 se houver ADK mais novo — fallback: boot.wim de ISO 32 bits (passo 4)

## Como continuar (qualquer IA)
1. Leia `AGENTS.md`, este arquivo e `docs/ARCHITECTURE.md`.
2. `git status`, `git log --oneline -15`.
3. Pegue a primeira pendência acima (ou pergunte ao usuário) e trabalhe em passos pequenos.
4. Atualize este arquivo e commite junto com a mudança.

## Log (mais recente primeiro; acrescente uma linha por tarefa)
- 2026-11-11 — Claude Code — ícone do app (scripts/make-icon.ps1 gera app.ico 16..256), ApplicationIcon + metadados de versão no csproj, Icon nas janelas. Validar: dotnet build; Propriedades > Detalhes do WinPEBuilder.exe.
- 2026-11-11 — Claude Code — passo 7: GUI completa (MainWindow/UsbWindow), UserSettings, LogFile, fechamento seguro durante operação (cancela e desmonta antes de sair), README. Validar: dotnet test src/WinPEBuilder.sln (52 ok) e abrir WinPEBuilder.exe.
- 2026-11-11 — Claude Code — passo 6: BootFiles (etfsboot/efisys, prefere noprompt), etapa oscdimg (-m -o -u2 -udfver102 -bootdata BIOS+UEFI), fwfiles preenchido, UsbMediaWriter. Validar: dotnet test src/WinPEBuilder.sln (48 ok); oscdimg real rodado sobre a mídia do ADK e El Torito conferido.
- 2026-11-11 — Claude Code — passo 5: StartnetScript (mapeia a pasta como Z: e %ARQUIVOS%, roda startup.cmd), cópia p/ mediaArquivos (OnIso) ou mountArquivos (InsideImage, máx 1 GB), travas de pasta sobreposta. Validar: dotnet test src/WinPEBuilder.sln (35 ok).
- 2026-11-11 — Claude Code — passo 4: Core/Build (BuildOptions, OptionalComponents, WinPeBuildService: copype/ISO→boot.wim, mount, pacotes, locale, scratch, unmount commit/discard). Validar: dotnet test src/WinPEBuilder.sln (28 ok).
- 2026-11-11 — Claude Code — passo 3: Processes/ (CommandSpec, ProcessRunner), AdkRelease/AdkCatalog, AdkInstaller (download + verificação Authenticode Microsoft + instalação silenciosa), AppPaths, manifesto requireAdministrator, GUI com botões/progresso/log. Validar: dotnet test src/WinPEBuilder.sln (17 ok).
- 2026-11-11 — Claude Code — passo 2: AdkInstall/AdkLocator (Core/Adk), painel de status na GUI; Core/Tests agora net10.0-windows. Validar: dotnet test src/WinPEBuilder.sln (8 ok).
- 2026-11-11 — Claude Code — passo 1: solução src/WinPEBuilder.sln (Core/App/Tests, TreatWarningsAsErrors). Validar: `dotnet build src/WinPEBuilder.sln ; dotnet test src/WinPEBuilder.sln`.
- 2026-11-11 — Claude Code — padrão de handoff instalado e STATUS preenchido (plano do WinPEBuilder definido). Validar: `pwsh scripts/check-handoff.ps1`.
