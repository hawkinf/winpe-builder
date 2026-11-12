# AGENTS.md — instruções para QUALQUER IA neste repositório (Claude, Codex, Gemini, Copilot, Cursor…)

Projeto: **WinPE Builder** — Programa GUI em C# que baixa/obtém os arquivos essenciais do WinPE (x86/x64), injeta arquivos do usuário e gera a ISO.
Stack: C# .NET (WPF) + Windows ADK/WinPE add-on (copype, DISM, oscdimg)

Este arquivo é a fonte única de regras para agentes de IA. `CLAUDE.md`, `GEMINI.md`, `.github/copilot-instructions.md` e
`.cursorrules` apenas apontam para cá. O dono do projeto troca de IA livremente: **nenhuma IA pode ser a única que entende o
projeto**. Tudo o que importa tem de estar escrito no repositório.

## 1. ANTES de qualquer trabalho (obrigatório, nesta ordem)
1. Leia este arquivo inteiro.
2. Leia `docs/STATUS.md` — estado atual, pendências, decisões e log. É a fonte de continuidade entre IAs.
3. Leia `docs/ARCHITECTURE.md` (mapa do código) e os docs que o STATUS citar.
4. Rode `git status` e `git log --oneline -15` para ver o que mudou desde a última atualização do STATUS.
5. Se o STATUS divergir do código, **o código vale**: corrija o STATUS e siga.
6. Diga ao usuário, em 3–5 linhas, o que entendeu do projeto e qual é o próximo passo, ANTES de mexer em código.

## 2. DEPOIS de qualquer mudança (obrigatório — definição de "pronto")
Uma tarefa só está concluída quando, **no mesmo commit** da mudança:
1. `docs/STATUS.md` foi atualizado: "Estado atual", "Pendências" e **uma linha nova no Log** (data, IA/ferramenta, o que mudou, como validar);
2. decisões novas e o porquê foram registradas em "Decisões" (ou `docs/DECISIONS.md`);
3. se a estrutura mudou, `docs/ARCHITECTURE.md` foi ajustado;
4. build/testes do projeto passaram e o resultado real foi relatado (se algo falhou ou foi pulado, diga);
5. o commit foi enviado ao GitHub (`git push origin main`) — ver a exceção permanente em "Regras de trabalho".
Sem isso a tarefa está **incompleta**. O script `scripts/check-handoff.ps1` (hook de pré-commit e CI) bloqueia commits de código
sem atualização de `docs/STATUS.md`. Não contorne o bloqueio; só use `HANDOFF_SKIP=1` para mudanças realmente triviais
(typo, formatação) e diga isso na mensagem do commit.

## 3. Regras de trabalho
- Passos pequenos e verificáveis; commite ao fim de cada passo. Sem refatorações grandes não pedidas.
- Anti-regressão é prioridade: não quebre comportamento já aprovado; adicione teste para cada bug corrigido.
- Commits: `<AREA>: <type> - <descrição curta>` (type: feat|fix|refactor|test|chore|mig). Rodapé `Co-Authored-By:` com a IA que fez.
- Nunca commitar segredos (tokens, chaves, `.env`, keystores, dados reais de usuário). Nunca logar segredo.
- Dados reais do usuário são intocáveis: use cópias temporárias para testar.
- Ações que afetam o mundo externo (deploy, apagar, enviar dados a serviços) só com pedido explícito do usuário.
- **EXCEÇÃO PERMANENTE (regra do usuário, 2026-11-12): este repositório é público em https://github.com/hawkinf/winpe-builder e TODA alteração
  deve ser enviada ao GitHub**: depois do commit, rode `git push origin main` sem perguntar e confira `git status -sb` (sem "ahead") e o
  workflow `handoff-check`. Antes do push, garanta que nada pessoal vai junto (caminhos de usuário, e-mail, tokens, capturas com dados pessoais).
  A autorização cobre só `origin/main` deste repositório: nada de `--force`, reescrever histórico ou apagar o repositório.
- Se não souber algo, pergunte ou registre em "Pendências" — não invente.
- Não dependa de memória de conversa: o que não está no repositório não existe para a próxima IA.

## 4. Como o usuário troca de IA
O usuário pode abrir o projeto em qualquer IA a qualquer momento. Para isso funcionar, mantenha sempre: STATUS atualizado,
comandos de build/teste/execução documentados abaixo, e nada essencial só na sua cabeça.

## 5. Comandos do projeto (mantenha atualizado)
dotnet build src/WinPEBuilder.sln ; dotnet test

## 6. Mapa rápido
src/ (app C#), scripts/ (PowerShell/UIA PEBakery), config/, tools/, build/, output/
