# ARCHITECTURE — WinPE Builder

Mapa do código para quem chega sem contexto (qualquer IA ou pessoa). Mantenha curto e verdadeiro; ao mudar a estrutura, atualize aqui
no mesmo commit.

## Visão geral
Programa GUI em C# que baixa/obtém os arquivos essenciais do WinPE (x86/x64), injeta arquivos do usuário e gera a ISO.

## Estrutura de pastas
src/ (app C#), scripts/ (PowerShell/UIA PEBakery), config/, tools/, build/, output/

## Fluxos principais
- (descreva em 3–6 linhas cada: entrada → processamento → saída)

## Dados e integrações
- Banco/armazenamento: (qual, onde, esquema)
- Serviços externos: (APIs, credenciais NÃO aqui — só onde são configuradas)

## Convenções
- Nomenclatura, camadas, padrões usados e o motivo.

## Armadilhas conhecidas
- (coisas que já custaram tempo a alguém)
