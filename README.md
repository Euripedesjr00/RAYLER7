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
| Etapa 2 — extração de Excel (.xlsx, .xlsm, .xls), LibreOffice (.ods), CSV/TSV/TXT, Word (tabelas) e PDF digital | ✅ Implementado |
| Etapa 2 — PDF digitalizado: cada página sem texto é renderizada pelo Windows e lida por OCR | ✅ Implementado |
| Etapa 2 — botão **Selecionar todos os dados** (todas as páginas) e conferência de dúvidas | ✅ Implementado |
| Etapa 2 — geração da planilha preenchida (cópia; o modelo não é alterado) | ✅ Implementado |
| OCR de fotos (.jpg, .png, .bmp, .tif, .webp, .heic) com dois motores gratuitos (Tesseract + OCR do Windows) | ✅ Implementado |
| Etapa 3 — 7 modelos de aparência + manter original, prévia e **Confirmar formatação** | ✅ Implementado |
| Etapa 3 — exportação: Excel, LibreOffice (.ods), CSV (; e ,), TSV, TXT, JSON, DBF e pacotes para R, SPSS, Stata e SAS | ✅ Implementado |
| Mensagem final "Criado por Jhon" só depois de uma exportação concluída | ✅ Implementado |
| Instalador Windows por usuário (sem senha de administrador, sem instalar .NET à parte) | ✅ Implementado |
| Leitura de .doc e .rtf (salve como .docx) | ⏳ Pendente |
| Exportação em PDF e arquivos nativos .sav / .dta | ⏳ Pendente |
| Módulos científicos e estatísticos do prompt mestre | ⏳ Pendente |

Os pacotes de R, SPSS, Stata e SAS são um `.zip` com os dados em CSV UTF-8, dicionário de dados e
um script de importação que lê **todas as colunas como texto**: nenhum número é convertido sem você ver.

Ao exportar em Excel, o programa compara célula por célula a planilha preenchida com o arquivo
formatado; se qualquer valor ou fórmula mudar, a exportação é cancelada.

## Regra central

Cada valor guarda três camadas separadas: **original** (imutável), **interpretado** (só para
cálculo) e **corrigido** (somente com autorização, registrada no histórico). `12,50` continua
`12,50`; `001` continua `001`; separadores ambíguos como `1.234` viram dúvida para o usuário decidir.

## Baixar e instalar

**[⬇ Baixar a versão mais recente](https://github.com/Euripedesjr00/RAYLER7/releases/latest)** — arquivo `RAYLER7_Instalador_1.0.0.exe`.

Execute o instalador. A instalação é só para o seu usuário
(`%LOCALAPPDATA%\Programs\RAYLER7`), cria atalho no Menu Iniciar (e, se quiser, na área de trabalho)
e aparece em "Aplicativos instalados" para desinstalar. Desinstalar **não apaga** seus projetos.

O Windows SmartScreen pode avisar que o editor é desconhecido, porque o instalador ainda não tem
assinatura digital paga. Clique em "Mais informações" → "Executar assim mesmo".

## Compilar

Requisitos: Windows 10/11 x64 e .NET 10 SDK.

```
scripts\COMPILAR_E_TESTAR.cmd   (restaura, compila e roda os testes; resultado em logs\)
scripts\ABRIR_RAYLER7.cmd       (abre o programa)
scripts\GERAR_INSTALADOR.cmd    (gera artifacts\instalador\RAYLER7_Instalador_*.exe; precisa do Inno Setup 6)
scripts\TESTAR_INSTALADOR.cmd   (instala, abre, desinstala e reinstala em silêncio; resultado em logs\)
```

Os dados do programa ficam em `%LOCALAPPDATA%\RAYLER7`.
Para testar sem mexer nos dados reais: `scripts\ABRIR_RAYLER7_TESTE.cmd`.

## Estrutura

- `src/Rayler7.Core` — regras de negócio sem interface (preservação de valores, seleção, dúvidas, mapeamento)
- `src/Rayler7.Infrastructure` — planilhas, documentos, SQLite, segurança
- `src/Rayler7.App` — interface WPF (MVVM)
- `tests/` — testes automatizados (xUnit)
