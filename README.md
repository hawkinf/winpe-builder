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
- `src/` – solução C# (Core = lógica, App = GUI WPF, Tests = xUnit)
- `scripts/` – PowerShell do Test Lab (UIA/PEBakery) e `check-handoff.ps1`
- `config/`, `tools/`, `build/`, `output/` – Test Lab (binários ignorados pelo git)
