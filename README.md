# WinPE Builder

Programa com interface gráfica (C#, .NET 10, WPF) que gera uma **ISO de WinPE** (32 ou 64 bits, boot BIOS + UEFI)
com os arquivos que você quiser dentro, acessíveis no WinPE. Ele também baixa e instala o que for preciso
(Windows ADK + WinPE add-on) direto dos servidores da Microsoft.

> Este repositório também contém o antigo "WinPE Test Lab" (PhoenixPE/PEBakery) em `scripts/`, `config/` e `tools/`.

## Como usar
1. Abra `WinPEBuilder.exe` (pede administrador: o DISM precisa para montar o `boot.wim`).
2. **Requisitos**: se faltar algo, use *Instalar ADK (64 bits)* ou *Instalar ADK Win10 2004 (32 bits)*.
3. Escolha **arquitetura** e **origem**:
   - *WinPE oficial do ADK* (recomendado; permite pacotes opcionais como PowerShell, .NET, WMI…);
   - *boot.wim de uma ISO do Windows* (útil para 32 bits sem o ADK antigo; WinPE básico).
4. Marque os **pacotes opcionais**, o **teclado** (padrão pt-BR) e, se quiser, a **pasta de arquivos**.
5. Escolha onde salvar a ISO e clique **Gerar ISO**. Depois, opcionalmente, **Gravar em pendrive** (formata o pendrive!).

## Seus arquivos dentro do WinPE
- **Na ISO** (recomendado): ficam em `\Arquivos` na mídia; não ocupam RAM. **Dentro da imagem**: ficam em `X:\Arquivos` (RAM, até 1 GB).
- Ao iniciar, o WinPE mapeia a pasta como unidade **`Z:`** e define a variável **`%ARQUIVOS%`**.
- Se existir `startup.cmd` na pasta, ele é executado automaticamente.

## Programas com ícone (desktop e menu Iniciar)
### Editor visual (recomendado)
No grupo **6** clique em **Abrir editor visual…**. Ele mostra a tela do WinPE como vai ficar (área de trabalho, barra de tarefas e menu Iniciar), **sem executar nada**:
- **Arraste** os ícones para onde quiser (grade de 80 px; tela de referência 1024x768). Soltar sobre outro ícone **troca de lugar**; soltar um programa **sobre uma pasta** coloca-o dentro dela.
- **Adicionar:** botões *Adicionar programa…* / *Adicionar pasta de programas…*, ou arraste `.exe` e pastas do Explorer direto para a tela. Arrastar um programa da lista para a tela o coloca na área de trabalho.
- **Duplo clique** num programa edita; numa pasta abre a janela dela (dá para arrastar de lá para fora). **Botão direito**: editar, colocar em pasta nova, tirar da pasta, remover. **Delete** remove, **F2** renomeia pasta.
- **Subpastas:** no campo *Pasta no desktop* use `/` (ex.: `Ferramentas/Discos`, até 4 níveis). Dentro da janela de uma pasta há subpastas (duplo clique abre, `..` volta); arraste programas e pastas sobre elas para reorganizar; botão direito cria subpasta, renomeia, sobe um nível ou desfaz.
- Clique em **Iniciar** para ver o menu por categoria exatamente como será. Na aba **Menu Iniciar** do painel direito dá para **reordenar à mão** categorias e programas (Subir/Descer ou arrastar; soltar um programa sobre outra categoria muda a categoria dele).
- **Gravar** aplica as mudanças; **Descartar** (ou fechar a janela e escolher "Não") joga tudo fora. As posições são salvas e usadas pelo launcher no WinPE.

### Pelos botões da lista
No grupo **6. Programas no WinPE** clique em *Adicionar…*, escolha o `.exe`, o nome, a **categoria** (vira um submenu no Iniciar) e onde
aparece (área de trabalho e/ou menu Iniciar). Os programas são copiados para `\Arquivos\Programas\<id>` na mídia (não usam RAM; com
"copiar a pasta inteira" os arquivos vizinhos vão junto, necessário para programas portáteis).
- Dentro do WinPE roda o **WinPE Launcher** (`WinPELauncher.exe`, WinForms/.NET Framework 4.8): ícones extraídos dos próprios `.exe`,
  barra de tarefas com os programas abertos e relógio, menu **Iniciar** por categoria, Prompt de comando, PowerShell, Reiniciar e Desligar.
- O launcher precisa do pacote **.NET Framework** do WinPE: é adicionado sozinho e só existe com a origem **ADK**. Com a origem
  *boot.wim de uma ISO* os programas são copiados (em `Z:\Programas`) mas o desktop/menu não aparecem.
- Testar o launcher no Windows comum: `WinPELauncher.exe --preview --manifest "C:\...\Arquivos\programs.json"` (janela normal; não desliga nada).
- Se o launcher falhar dentro do WinPE, o `startnet.cmd` continua e você tem o prompt de comando; o log fica em `X:\Windows\Temp\WinPELauncher.log`.

### Adicionar uma pasta com vários programas
*Adicionar pasta…* lista os `.exe` da pasta (até 3 níveis; desinstaladores e instaladores de runtime são ignorados). Para cada um você
marca **Incluir**, **Menu Iniciar** e/ou **Área de trabalho** e ajusta o nome; os botões do topo marcam todos de uma vez.
Opcionalmente marque *Colocar esta pasta na área de trabalho*: aparece um ícone de pasta que abre uma janela com todos os programas marcados.
A pasta é copiada **uma única vez** para a mídia, mesmo com vários programas dela. Em *Editar* dá para mudar o nome da pasta do desktop de qualquer programa.

## 32 bits (x86)
A Microsoft removeu o WinPE x86 do ADK moderno. A última versão com ele é o **ADK do Windows 10 2004**; o app instala esse ADK
numa pasta própria (`%LOCALAPPDATA%\WinPEBuilder\adk`). Alternativa: usar a origem *boot.wim de uma ISO* com uma ISO do Windows 32 bits.

## Desenvolvimento
```
dotnet build src/WinPEBuilder.sln
dotnet test  src/WinPEBuilder.sln
```
Executável: `src/WinPEBuilder.App/bin/Debug/net10.0-windows/WinPEBuilder.exe` (rode pelo Explorer ou de um terminal elevado;
`dotnet run` de terminal comum falha por causa do manifesto de administrador). Dados do app: `%LOCALAPPDATA%\WinPEBuilder`
(`settings.json`, `logs\`, `downloads\`, `work\`, `adk\`). Mapa do código: `docs/ARCHITECTURE.md`. Estado e pendências: `docs/STATUS.md`.

## Estrutura
- `src/` – solução C# (Core = lógica, App = GUI WPF, Launcher = desktop/menu que roda no WinPE (net48), Tests = xUnit)
- `scripts/` – PowerShell do Test Lab (UIA/PEBakery), `check-handoff.ps1` e `make-icon.ps1` (gera o ícone do app)
- `config/`, `tools/`, `build/`, `output/` – Test Lab (binários ignorados pelo git)
