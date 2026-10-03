# WinPE Test Lab

WinPE (UEFI + BIOS) com shell gráfico (PhoenixPE) para testes de hardware de PCs e notebooks.
Uso pessoal.

## Fonte
- ISO: `O:\isos\Win10_22H2_x64_uanttended.iso` (Windows 10 22H2 x64)
- Base: PhoenixPE (PEBakery) — a baixar

## Estrutura
- `build/`   – PhoenixPE/PEBakery e área de trabalho do build (ignorado pelo git)
- `tools/`   – programas portáteis de teste (ignorado pelo git; ver `tools/README.md`)
- `config/`  – lista de programas, projetos PEBakery e ajustes
- `scripts/` – scripts de build, relatório e mídia
- `output/`  – ISO/WIM gerados (ignorado pelo git)
