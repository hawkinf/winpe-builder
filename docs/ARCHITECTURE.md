# ARCHITECTURE — WinPE Builder

Mapa do código para quem chega sem contexto (qualquer IA ou pessoa). Mantenha curto e verdadeiro; ao mudar a estrutura, atualize aqui
no mesmo commit.

## Visão geral
Programa GUI em C# que baixa/obtém os arquivos essenciais do WinPE (x86/x64), injeta arquivos do usuário e gera a ISO.

## Estrutura de pastas
src/WinPEBuilder.sln
  WinPEBuilder.Core/   – lógica sem UI (net10.0-windows). Adk/: AdkInstall (caminhos/variáveis de ambiente do ADK), AdkLocator (acha ADK em registro/Program Files/pastas privadas)
  WinPEBuilder.App/    – GUI WPF (net10.0-windows); só apresentação, chama o Core
  WinPEBuilder.Tests/  – testes xUnit do Core
scripts/ (PowerShell/UIA PEBakery, check-handoff), config/, tools/, build/, output/ (binários ignorados pelo git)

## Fluxos principais
- (descreva em 3–6 linhas cada: entrada → processamento → saída)

## Dados e integrações
- Banco/armazenamento: (qual, onde, esquema)
- Serviços externos: (APIs, credenciais NÃO aqui — só onde são configuradas)

## Convenções
- Nomenclatura, camadas, padrões usados e o motivo.

## Armadilhas conhecidas
- (coisas que já custaram tempo a alguém)
- DandISetEnv.bat lê o registro (KitsRoot10) e roda WimMountAdkSetup; por isso NÃO o usamos: AdkInstall.BuildEnvironment() monta WinPERoot/OSCDImgRoot/DISMRoot/PATH e funciona também para ADK em pasta privada.
- O ADK atual (24H2/26100) não tem WinPE x86; só o ADK Windows 10 2004 (linkid 2120254 + add-on 2120253) tem.
- Core/Processes: CommandSpec (inclui Cmd() p/ .cmd com aspas corretas e Quote()) + ProcessRunner (saída na codepage OEM, stdin fechado p/ nunca travar em "Y/N"). Core/AppPaths: tudo em %LOCALAPPDATA%\WinPEBuilder (downloads, adk\<id>, work\<arch>).
- AdkInstaller só aceita instaladores com assinatura Microsoft válida; exit code 3010 = sucesso (reboot).
- App usa manifest requireAdministrator (DISM exige); `dotnet run` abre pedido de UAC.
- Core/Build/WinPeBuildService: pipeline em etapas (PlanSteps). Fonte ADK = copype; fonte ISO = Mount-DiskImage + cópia de boot/efi/bootmgr + `dism /Export-Image /SourceIndex:1 /Bootable`. Se algo falhar/cancelar com a imagem montada, o `finally` faz `/Unmount-Image /Discard`.
- Segurança: a pasta de trabalho só é apagada se estiver DENTRO de allowedWorkRoot (AppPaths.Root); caso contrário BuildException.
- Pacotes opcionais: OptionalComponents.All tem ordem = ordem de instalação; Resolve() inclui dependências.
- Armadilha: arquivos copiados de ISO são somente leitura — FileSystemHelper limpa o atributo (senão Directory.Delete falha).
- Arquivos do usuário: OnIso (padrão recomendado) copia p/ `media\Arquivos` e o startnet.cmd procura em C..Z o `Arquivos\.winpe-files` e faz `subst Z:`; InsideImage copia p/ `X:\Arquivos` (RAM, limite 1 GB). Letra Z (não P): em PCs reais P/outras já são comuns; subst falho é silenciado e %ARQUIVOS% sempre vale. `startup.cmd` na pasta roda automaticamente.
- ISO: `oscdimg -m -o -u2 -udfver102 -l<label> -bootdata:2#p0,e,b"<etfsboot.com>"#pEF,e,b"<efisys[_noprompt].bin>" <media> <iso>` (igual ao MakeWinPEMedia; UDF suporta arquivos >4 GB). BootFiles.Resolve procura em fwfiles → Deployment Tools\<arq>\Oscdimg → mídia da ISO de origem, sempre da arquitetura de destino.
- UsbMediaWriter chama `MakeWinPEMedia /UFD /F` (FORMATA o pendrive): só aceita discos removíveis prontos que não sejam o do Windows, revalida a letra na hora e exige o WinPE já gerado na pasta de trabalho. A GUI deve confirmar com letra+nome+tamanho.
- GUI (App): MainWindow = code-behind fino; toda lógica fica no Core. `RunOperationAsync` centraliza log em arquivo (%LOCALAPPDATA%\WinPEBuilder\logs), cancelamento e erros amigáveis (BuildException mostra a mensagem; outras exceções vão só pro log + mensagem genérica). Fechar a janela durante uma operação cancela, ESPERA o `finally` desmontar o WIM (/Discard) e só então fecha.
- UsbWindow exige digitar a letra do pendrive para habilitar "Formatar e gravar". Botão de pendrive só habilita após uma geração bem-sucedida na sessão.
- Teste de fumaça sem admin: `dotnet build src/WinPEBuilder.App -p:ApplicationManifest= -o <pasta fora do repo>` gera uma variante sem o manifesto requireAdministrator.
