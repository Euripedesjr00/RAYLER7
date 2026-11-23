# 🇧🇷 RAYLER7 🇮🇱

**Criado por Jhon** — *“Desisti nunca foi uma opção!”*

Programa desktop para **Windows 10 e 11 (64 bits)** que preenche uma planilha-modelo com dados
extraídos de outros arquivos, **sem nunca alterar silenciosamente os valores originais**.
Funciona 100% local, sem internet e sem serviços pagos de IA.

## Situação atual (o que funciona de verdade)

| Etapa | Situação |
|---|---|
| Login local (Argon2id, bloqueio progressivo, sem senha universal) | ✅ Implementado |
| Projetos salvos localmente e "Continuar projeto" | ✅ Implementado |
| Etapa 1 — planilha-modelo (.xlsx, .xlsm, .csv, .tsv, .txt), instruções e mapeamento | ✅ Implementado |
| Etapa 2 — extração de Excel, CSV/TXT, Word (tabelas) e PDF com texto digital | ✅ Implementado |
| Etapa 2 — botão **Selecionar todos os dados** (todas as páginas) e conferência de dúvidas | ✅ Implementado |
| Etapa 2 — geração da planilha preenchida (cópia; o modelo não é alterado) | ✅ Implementado |
| OCR de fotos e PDFs digitalizados (Tesseract + OCR do Windows, gratuitos) | ⏳ Pendente |
| Etapa 3 — personalização visual e exportação em outros formatos | ⏳ Pendente |
| Instalador Windows (Inno Setup) | ⏳ Pendente |
| Módulos científicos e estatísticos do prompt mestre | ⏳ Pendente |

## Regra central

Cada valor guarda três camadas separadas: **original** (imutável), **interpretado** (só para
cálculo) e **corrigido** (somente com autorização, registrada no histórico). `12,50` continua
`12,50`; `001` continua `001`; separadores ambíguos como `1.234` viram dúvida para o usuário decidir.

## Compilar

Requisitos: Windows 10/11 x64 e .NET 10 SDK.

```
scripts\COMPILAR_E_TESTAR.cmd   (restaura, compila e roda os testes; resultado em logs\)
scripts\ABRIR_RAYLER7.cmd       (abre o programa)
```

Os dados do programa ficam em `%LOCALAPPDATA%\RAYLER7`.
Para testar sem mexer nos dados reais: `scripts\ABRIR_RAYLER7_TESTE.cmd`.

## Estrutura

- `src/Rayler7.Core` — regras de negócio sem interface (preservação de valores, seleção, dúvidas, mapeamento)
- `src/Rayler7.Infrastructure` — planilhas, documentos, SQLite, segurança
- `src/Rayler7.App` — interface WPF (MVVM)
- `tests/` — testes automatizados (xUnit)
