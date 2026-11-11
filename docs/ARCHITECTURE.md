# ARCHITECTURE — WinPE Builder

Mapa do código para quem chega sem contexto (qualquer IA ou pessoa). Mantenha curto e verdadeiro; ao mudar a estrutura, atualize aqui
no mesmo commit.

## Visão geral
Programa GUI em C# que baixa/obtém os arquivos essenciais do WinPE (x86/x64), injeta arquivos do usuário e gera a ISO.

## Estrutura de pastas
src/WinPEBuilder.sln
  WinPEBuilder.Core/   – lógica sem UI (net10.0): arquitetura, detecção/instalação do ADK, montagem do WinPE, ISO
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
