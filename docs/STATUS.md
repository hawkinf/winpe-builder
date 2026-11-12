# STATUS — estado vivo de WinPE Builder

> **REGRA OBRIGATÓRIA (qualquer IA ou pessoa):** toda tarefa que altere este repositório DEVE terminar atualizando este arquivo
> ("Estado atual", "Pendências" e uma linha no "Log") **no mesmo commit** da mudança. Veja `AGENTS.md`.
> Se o estado aqui divergir do código, o código vale — corrija este arquivo.

Última atualização: 2026-11-12 (Claude Code).

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
| Programas no WinPE — passo A1: modelo + manifesto + cópia no build (`Core/Programs`) | ✅ | 74 testes ok; ainda SEM tela no app e SEM launcher |
| Programas no WinPE — A2 tela "Programas" (ProgramWindow + grupo 6, persistência em settings.json) | ✅ | janela renderizada e conferida por captura; fluxo clicar-adicionar não automatizado |
| Programas no WinPE — A3 launcher (`src/WinPEBuilder.Launcher`, WinForms net48: desktop, barra, Iniciar por categoria) | ✅ | testado no Windows comum com `--preview` (ícones extraídos OK); DENTRO do WinPE real NÃO testado |
| Programas no WinPE — "Adicionar pasta" (vários .exe, Iniciar/Desktop por programa, pasta na área de trabalho, cópia única) | ✅ | 116 testes ok; janela conferida (harness) e launcher conferido com pasta+janela; ISO real NÃO gerada |
| Programas no WinPE — EDITOR VISUAL (preview do desktop/Iniciar, arrastar, adicionar/remover, gravar/descartar, layout no launcher) | ✅ | 145 testes ok (modelo); arrasto com mouse real testado num harness; launcher real respeita as posições; ISO real NÃO gerada |
| Programas no WinPE — A4 injeção (exe em System32, WinPE-NetFx automático, startnet inicia o launcher) | ✅ | 101 testes ok com runner simulado; boot real NÃO testado |
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
- Launcher no WinPE real: JÁ confirmado no QEMU (ícones, barra e relógio aparecem). Falta clicar nos programas, Reiniciar/Desligar, papel de parede/subpastas e testar em Hyper-V/VirtualBox/VMware/hardware real — responsável: dono do projeto
- Launcher: se o WinForms não subir no WinPE real, plano B = trocar por HTA/PowerShell ou usar winpeshl.ini; o log fica em X:\Windows\Temp\WinPELauncher.log
- Ideias futuras: injeção de drivers, vários idiomas, subpastas aninhadas na janela de pasta do desktop, reordenar ícones — qualquer IA
- Gravação em pendrive nunca foi executada de verdade (formata o disco): testar primeiro com um pendrive vazio — responsável: dono do projeto
- Validar uma geração REAL (x64 via ADK) com o app elevado; só foi testado com runner simulado — responsável: dono do projeto
- Instalação real do ADK (x64 e x86 em pasta privada via /installpath) ainda não foi exercitada; o instalador pode recusar o ADK 2004 se houver ADK mais novo — fallback: boot.wim de ISO 32 bits (passo 4)

## Como continuar (qualquer IA)
1. Leia `AGENTS.md`, este arquivo e `docs/ARCHITECTURE.md`.
2. `git status`, `git log --oneline -15`.
3. Pegue a primeira pendência acima (ou pergunte ao usuário) e trabalhe em passos pequenos.
4. Atualize este arquivo e commite junto com a mudança.

## Log (mais recente primeiro; acrescente uma linha por tarefa)
- 2026-11-12 — Claude Code — programas de 32 bits no WinPE de 64 bits: DIAGNÓSTICO no WinPE real (QEMU, ISO do usuário): os 5 programas x86 (HDSentinel, Advanced IP Scanner, cpuburner, FurMark, GPU-Z) falham com "side-by-side configuration is incorrect"; os x64 (DiskInfo64, DiskMark64) rodam. Causa: o WinPE amd64 não tem WOW64 completo (sem wow64.dll, sem comctl32 x86 no WinSxS). Correção: PeInfo (lê o cabeçalho PE; compartilhado Core+launcher), ProgramCompatibility, BuildOptions.CompatibilityWarnings (aviso no log e confirmação antes de gerar), coluna Arq. e ⚠ no editor/lista, aviso ao adicionar, linhas x86 desmarcadas na importação de pasta, e o launcher explica o motivo real. Validar: dotnet test (253 ok). PENDENTE (ideia): opção para adicionar WOW64 ao WinPE x64 (copiar de uma ISO do Windows).
- 2026-11-12 — Claude Code — launcher vira o SHELL do WinPE via winpeshl.ini ([LaunchApps]: startnet.cmd → WinPELauncher.exe → cmd.exe de reserva): some o prompt da tela e fechar janelas não reinicia o PC (o WinPE reinicia quando o shell termina). Comportamento do winpeshl (executa a lista em sequência, esperando cada app) VERIFICADO no WinPE real via QEMU; o reinício ao terminar o último app não foi testado. startup.cmd do usuário passa a rodar em janela própria (start) quando há launcher. Validar: dotnet test (233 ok); gerar ISO com programas e ver que não há prompt na tela.
- 2026-11-12 — Claude Code — regra permanente do usuário registrada no AGENTS.md: toda alteração é commitada E enviada ao GitHub (`git push origin main`, repositório público, sem `--force`). Validar: `git status -sb` sem "ahead" após cada tarefa.
- 2026-11-12 — Claude Code — diagnóstico "ISO não inicia na VM": a ISO gerada pelo app (log build-20261112-111415) INICIA no QEMU em BIOS e UEFI até o desktop do launcher (1 GB de RAM já basta); com CPU `qemu64` o guest reinicia em loop (WinPE 24H2 exige POPCNT/SSE4.2); bootx64.efi assinado pela PCA 2011. Conclusão: problema é a config da VM do usuário. README ganhou a seção "Testando numa máquina virtual" e a tabela de verificação foi atualizada. Validar: reproduzir com `qemu-system-x86_64 -cpu qemu64` (loop) vs `-cpu max` (boot).
- 2026-11-12 — Claude Code — README público reescrito (recursos, editor visual, como usar/compilar, estado e limitações, avisos) + capturas em docs/img (dados de exemplo, sem informação pessoal); repositório publicado no GitHub (público). Sem LICENSE ainda (decisão do autor). Validar: abrir README no GitHub; dotnet test src/WinPEBuilder.sln (230 ok).
- 2026-11-12 — Claude Code — papel de parede: WallpaperSettings/WallpaperMode (Core, validação: cor #RRGGBB, jpg/png/bmp, 25 MB), WallpaperGeometry compartilhada (launcher+App+testes), manifesto `wallpaper`, cópia para Arquivos\wallpaper.ext, launcher com ícones próprios (DesktopIcon, fundo transparente) e posicionamento por PlaceIcons, App: WallpaperRenderer + WallpaperWindow + botão no editor (gravar/descartar junto). Validar: dotnet test src/WinPEBuilder.sln (230 ok); editor > Papel de parede…; `WinPELauncher.exe --preview --manifest <programs.json com wallpaper>`. Imagem real no WinPE (GDI+) NÃO testada.
- 2026-11-12 — Claude Code — subpastas nas pastas da área de trabalho: FolderPath (caminho "A/B", até 4 níveis), DesktopLayoutHelper.FolderContents/AllFolderPaths, DesignerModel (RenameFolder/DissolveFolder/MoveFolder/MoveUpOneLevel/MoveToFolderPath/RemoveFolder), janela de pasta navegável no editor e no launcher (FolderForm com ".." e título com caminho). Validar: dotnet test src/WinPEBuilder.sln (195 ok); no app: programa com pasta "Ferr/Discos" e abrir a pasta no editor. Arrastar subpastas/programas na janela da pasta com mouse real NÃO foi verificado (lógica coberta por testes; abrir/navegar foi conferido por código).
- 2026-11-12 — Claude Code — ordem manual do menu Iniciar: StartMenuModel.Build(order)/Flatten, DesignerModel.MoveMenuEntry/DropMenuEntry, UserSettings.StartMenuOrder, campo menuOrder no manifesto, launcher ordena igual, aba "Menu Iniciar" (árvore + Subir/Descer + arrastar) no editor. Validar: dotnet test src/WinPEBuilder.sln (161 ok). Arrastar na árvore com mouse real NÃO foi verificado (Subir/Descer sim).
- 2026-11-12 — Claude Code — editor visual: DesktopLayout/DesignerModel (Core), campo layout no manifesto + posições no launcher, DesignerWindow/ShellIcon/InputWindow (App), botão "Abrir editor visual…". Validar: dotnet test src/WinPEBuilder.sln (145 ok); abrir o app > grupo 6 > Abrir editor visual; `WinPELauncher.exe --preview --manifest <programs.json com layout>`.
- 2026-11-12 — Claude Code — "Adicionar pasta": ProgramScanner, SourceRoot/DesktopFolder, cópia única por pasta (StageFolderName), campo folder no manifesto, FolderImportWindow, ícone/janela de pasta no launcher. Validar: dotnet test src/WinPEBuilder.sln (116 ok); `WinPELauncher.exe --preview --manifest <programs.json com "folder">` e duplo clique na pasta.
- 2026-11-12 — Claude Code — correção do publish (NETSDK1005/1047): GlobalPropertiesToRemove no ProjectReference do launcher, RuntimeIdentifiers no Launcher, alvo PublishLauncher (launcher\ na pasta publicada, também em single-file). Validar: `dotnet publish src/WinPEBuilder.App -c Release -r win-x64 -o <pasta>` e conferir `<pasta>\launcher\WinPELauncher.exe` (testado x64, x86, self-contained+single-file; Launcher sozinho também publica).
- 2026-11-12 — Claude Code — passos A2+A3+A4 (programas com ícone): tela Programas (ProgramWindow, UserSettings.Programs), projeto Launcher net48 (desktop/barra/Iniciar), injeção na imagem (BuildOptions.InstallLauncher/EffectiveComponents, StartnetScript launcher, CopyLauncher no App). Validar: dotnet test src/WinPEBuilder.sln (101 ok) e `WinPELauncher.exe --preview --manifest <programs.json>`.
- 2026-11-11 — Claude Code — correção de docs: barras invertidas perdidas em linhas do STATUS (passo 5 e A1/A2–A4).
- 2026-11-11 — Claude Code — passo A1 (programas no WinPE): Core/Programs (ProgramEntry+validação+IsProtectedFolder, ProgramStaging: cópia p/ Arquivos\Programas\{id} e programs.json v1), BuildOptions.Programs/EffectiveFilesMode (programas sozinhos => OnIso), travas (pasta protegida, sobreposição, 1 GB na imagem). Validar: dotnet test src/WinPEBuilder.sln (74 ok).
- 2026-11-11 — Claude Code — ícone do app (scripts/make-icon.ps1 gera app.ico 16..256), ApplicationIcon + metadados de versão no csproj, Icon nas janelas. Validar: dotnet build; Propriedades > Detalhes do WinPEBuilder.exe.
- 2026-11-11 — Claude Code — passo 7: GUI completa (MainWindow/UsbWindow), UserSettings, LogFile, fechamento seguro durante operação (cancela e desmonta antes de sair), README. Validar: dotnet test src/WinPEBuilder.sln (52 ok) e abrir WinPEBuilder.exe.
- 2026-11-11 — Claude Code — passo 6: BootFiles (etfsboot/efisys, prefere noprompt), etapa oscdimg (-m -o -u2 -udfver102 -bootdata BIOS+UEFI), fwfiles preenchido, UsbMediaWriter. Validar: dotnet test src/WinPEBuilder.sln (48 ok); oscdimg real rodado sobre a mídia do ADK e El Torito conferido.
- 2026-11-11 — Claude Code — passo 5: StartnetScript (mapeia a pasta como Z: e %ARQUIVOS%, roda startup.cmd), cópia p/ media\Arquivos (OnIso) ou mount\Arquivos (InsideImage, máx 1 GB), travas de pasta sobreposta. Validar: dotnet test src/WinPEBuilder.sln (35 ok).
- 2026-11-11 — Claude Code — passo 4: Core/Build (BuildOptions, OptionalComponents, WinPeBuildService: copype/ISO→boot.wim, mount, pacotes, locale, scratch, unmount commit/discard). Validar: dotnet test src/WinPEBuilder.sln (28 ok).
- 2026-11-11 — Claude Code — passo 3: Processes/ (CommandSpec, ProcessRunner), AdkRelease/AdkCatalog, AdkInstaller (download + verificação Authenticode Microsoft + instalação silenciosa), AppPaths, manifesto requireAdministrator, GUI com botões/progresso/log. Validar: dotnet test src/WinPEBuilder.sln (17 ok).
- 2026-11-11 — Claude Code — passo 2: AdkInstall/AdkLocator (Core/Adk), painel de status na GUI; Core/Tests agora net10.0-windows. Validar: dotnet test src/WinPEBuilder.sln (8 ok).
- 2026-11-11 — Claude Code — passo 1: solução src/WinPEBuilder.sln (Core/App/Tests, TreatWarningsAsErrors). Validar: `dotnet build src/WinPEBuilder.sln ; dotnet test src/WinPEBuilder.sln`.
- 2026-11-11 — Claude Code — padrão de handoff instalado e STATUS preenchido (plano do WinPEBuilder definido). Validar: `pwsh scripts/check-handoff.ps1`.
