# Word Compliance Validator

Valida documentos `.docx` contra o checklist **CL-001** (planilha Excel), por cliente. Insere comentários OpenXML nas violações encontradas.

**Stack:** C# 14 / .NET 10 (LTS) · Open XML SDK 3.5 · OpenAI SDK 2.14 (LLM opcional) · System.CommandLine 2.0 · Spectre.Console · xUnit v3 + Microsoft.Testing.Platform

---

## Arquitetura

```
CL-001.xlsx (checklist real)
    ↓ ExcelChecklistRepository
ChecklistEngine
    ↓ itera as 86 entradas do checklist
    ↓ encontra IRuleCheck registrada por ChecklistRef
    ↓ executa RunAsync(DocumentContext)
    ↓ captura violações e status (Passed / Failed / Skipped / Error)
DocumentReviewService
    ↓ agrega resultados
    ↓ CommentInserter → .docx com comentários
```

Cada regra automatizada tem uma implementação concreta de `IRuleCheck`, registrada em
`CheckRegistry`. Entradas sem check dedicado podem ser assumidas pelo **motor semântico
genérico** (`SemanticChecklistCheck`), que usa a própria coluna *Descrição* da planilha
como instrução de verificação — mediante allow-list explícita no profile do cliente.

> **A coluna `IA?` do CL-001 não limita o motor.** Ela é conservadora: várias regras
> marcadas `IA=Não` são perfeitamente decidíveis a partir do OOXML (tarjas do PS-024,
> formatação do corpo, painel de navegação, cobertura do índice). O `ChecklistEngine`
> procura primeiro um check registrado e só respeita a coluna quando invocado com
> `--only-ia-sim`. O que sobra — sobretudo o PS-005, que trata de fluxo Meridian,
> e-mails ao GQ e autoridade do aprovador — é genuinamente externo ao `.docx` e
> permanece `Skipped`.

---

## Estrutura do projeto

```
EstilizacaoWordComRegras.slnx   ← solução no formato novo (XML) do SDK 10
Directory.Build.props          ← net10.0, nullable, implicit usings — para todos os projetos
Directory.Packages.props       ← versões de pacote centralizadas (CPM)
global.json                    ← SDK 10 e dotnet test no Microsoft.Testing.Platform
nuget.config                   ← só nuget.org (não herda feeds privados da máquina)
src/
  Core/
    Abstractions/       IDocxStructureExtractor, ICommentInserter
    Checklist/          ChecklistEntry, ChecklistRef, ChecklistPadrao, IChecklistRepository
    Checks/             IRuleCheck, RuleCheckResult, CheckStatus, ChecklistEngine,
                        ISemanticChecker, SemanticEvaluation, SemanticFinding,
                        IEvidenceSelector, DocumentContext
    Models/             DocumentStructure, ExtractedTable*, Violation, Severity
    Profile/            ClientProfile, IClientProfileRepository
  Infrastructure/
    Checks/             CheckRegistry           ← registro central (usado pelo CLI e testes)
                        LogomarcasNoHeaderCheck, IniciaisDistintasCheck,
                        ConsistenciaIniciaisCheck,
                        QuadroCaracteristicasPreenchidoCheck,
                        IndiceAtualizadoCheck, PaginacaoAtualizadaCheck,
                        CabecalhosPadronizadosCheck, ReferenciasCruzadasCheck,
                        CoerenciaRevisoesCheck, ContinuidadeTituloConteudoCheck,
                        CodificacaoTecnicaCheck, LocalizacaoCodificacaoCheck,
                        CodificacaoClienteCheck, EvolucaoDocumentoCheck,
                        FolhaRostoCheck, FolhaRostoVsCaracteristicasCheck,
                        TarjaEmissaoCheck, FormatacaoCorpoCheck,
                        ElementosGraficosCheck, NumeracaoPaginasCheck,
                        PainelNavegacaoCheck, IndiceConteudoCheck,
                        IndicePaginaCheck, ApendiceAnexoCheck,
                        SemanticChecklistCheck  ← motor genérico
                        SemanticCheckFactory, DefaultEvidenceSelector,
                        QuadroCaracteristicas, DocumentoTexto, Codificacao (helpers)
    Excel/              ExcelChecklistRepository
    OpenAI/             OpenAiSemanticChecker, OpenAiSettings, ConsumoDeTokens
    OpenXml/            DocxStructureExtractor, CommentInserter
    Profile/            JsonClientProfileRepository
  Application/
    Services/           DocumentReviewService, DocumentReviewReport
  Cli/
    Program.cs          Comandos: review, dump-checklist, dump-evidencia
    appsettings.json                 ← rastreado, chave vazia; modelo, raciocínio e tarifas
    appsettings.Development.json     ← ignorado pelo git, guarda a chave local
tests/
  Core.Tests/           (7 testes)
  Infrastructure.Tests/ (138 testes)
    Fixtures/           DocxFixtureBuilder  ← gera .docx com defeito deliberado
templates/
  checklists/           CL-001-CL00100.xlsx  ← checklist real (86 linhas)
  RN799RL6496600.docx   ← documento de teste real (MRN)
profiles/
  exemplo.json          ← perfil MRN de exemplo
TODO_REGRAS.md          ← rastreamento por regra (status, implementação, heurísticas)
```

---

## Pré-requisitos

- .NET 10 SDK (10.0.100 ou superior; ver `global.json`)
- `OPENAI_API_KEY` — env var, ou `OpenAI:ApiKey` em `src/Cli/appsettings.Development.json`
  (ignorado pelo git). Necessário apenas para checks semânticos (LLM); sem ela o CLI roda os
  checks determinísticos e avisa `[info] LLM desabilitado`.
  **Nunca** coloque a chave em `src/Cli/appsettings.json` — esse arquivo é rastreado e vai
  dentro do pacote publicado pelo `publicar.ps1`.
- `OpenAI:Model` (padrão `gpt-6.1-sol`), `OpenAI:ReasoningEffort` (padrão `medium`) e
  `OpenAI:Precos` (tarifa por modelo, usada para estimar o custo) ficam em `appsettings.json`.

---

## Build & Testes

```bash
dotnet build
dotnet test
```

Os testes rodam no **xUnit v3** sobre o **Microsoft.Testing.Platform** (`global.json` →
`"test": { "runner": "Microsoft.Testing.Platform" }`). Opções úteis do `dotnet test` nesse modo:
`--coverlet` para cobertura (coverlet.MTP), e `-- --filter-method "*Tarja*"` para rodar só parte.
As asserções usam o **AwesomeAssertions** — fork Apache-2.0 do FluentAssertions, que a partir da
versão 8 passou a exigir licença paga para uso comercial; a API é a mesma.

Resultado esperado: **145 testes, todos passando** (Core 7 + Infrastructure 138).

A suíte tem duas metades complementares:
- `RealDocumentCheckTests` — documentos reais conformes **não** podem gerar alarme (precisão).
- `NegativeCorpusTests` — documentos gerados com defeito deliberado **têm** de ser detectados
  (recall). Sem essa metade, um check que nunca acusa nada passaria na outra.
- `CatalogCoverageTests` — toda entrada do CL-001 precisa de destino explícito: check
  dedicado, allow-list semântica ou declaração de revisão manual.
- `CliExitCodeTests` — executa o CLI **como processo** e confere o código de saída. É o único
  nível em que o exit code é observável, e por isso o único capaz de pegar uma regressão no
  gate de CI.

---

## CLI

> 👉 Veja **[USO.md](USO.md)** para um guia passo-a-passo de usabilidade
> (como rodar pela linha de comando, abrir o arquivo revisado no Word,
> interpretar comentários, etc.).

### Modo guiado — para quem não usa terminal

Sem argumentos (duplo clique no `Revisor.exe`) ou só com `.docx` (arquivos arrastados sobre ele),
e com uma pessoa do outro lado, o CLI vira uma conversa: tela de boas-vindas, pergunta o
documento (aceita arrastar o arquivo ou a pasta para a janela), o tipo de revisão (completa com
IA ou rápida) e o cliente; mostra o andamento ao vivo, o resultado em cartões ("a corrigir",
"a conferir", "regras ok", "não se aplicam") com os achados em português e, no fim, oferece abrir
o documento no Word. Com a saída redirecionada (scripts, CI) nada disso aparece — vale o comando
`review`. Implementação: `src/Cli/Terminal/ModoGuiado.cs` e `Apresentacao.cs` (Spectre.Console),
testada com `Spectre.Console.Testing` em `ModoGuiadoTests` simulando a digitação e as setas.

### `review` — revisar um documento

```bash
dotnet run --project src/Cli -- review \
  --doc      templates/RN799RL6496600.docx \
  --profile  profiles/exemplo.json \
  --out      output/revisado.docx
```

Flags opcionais:
- `--no-llm` — desativa checks semânticos (não precisa de `OPENAI_API_KEY`)
- `--verbose` — mostra também itens Skipped/Passed
- `--checklist` — caminho alternativo para o `.xlsx` (padrão: `templates/checklists/CL-001-CL00100.xlsx`)
- `--only-ia-sim` — executa apenas itens marcados `IA=Sim` na planilha (comportamento legado,
  útil para reproduzir baselines antigas)

Exit code:
- `0` → sem violações de severidade `Error`
- `2` → há pelo menos uma violação `Error`
- `1` → erro de execução ou de linha de comando (arquivo inexistente, `.doc`, opção faltando ou
  desconhecida). Erros de digitação saem em uma frase em português ("Falta a opção obrigatória
  --doc.") com a indicação `Revisor review --help`, em vez da ajuda inteira em inglês.

Com a saída redirecionada (`> saida.txt`, pipeline de CI) o texto sai em **UTF-8**, qualquer que
seja a code page do console. No Windows PowerShell 5.1, que decodifica a saída de programas pela
code page, rode antes `[Console]::OutputEncoding = [Text.UTF8Encoding]::new()` para os acentos
chegarem certos num `| Out-File`; no `cmd` e no PowerShell 7.4+ com `>` não é preciso nada.

Com o LLM ativo, o fim da saída traz o **consumo por modelo**: chamadas, tokens de entrada
(e quanto saiu do cache), saída, raciocínio e o custo estimado pelas tarifas de `OpenAI:Precos`.

### `dump-checklist` — listar entradas do checklist (debug)

```bash
dotnet run --project src/Cli -- dump-checklist
# ou com arquivo alternativo:
dotnet run --project src/Cli -- dump-checklist --checklist outro.xlsx
```

### `dump-evidencia` — ver o que o LLM recebe (debug)

```bash
dotnet run --project src/Cli -- dump-evidencia --doc templates/RN799RL6496600.docx --profile profiles/exemplo.json
```

Imprime o pacote de evidências que o motor semântico envia em toda chamada. É o primeiro
passo para calibrar um falso positivo do LLM: quase sempre o problema está no recorte, não no
modelo.

---

## Perfil de cliente (`profiles/<cliente>.json`)

> 👉 Veja **[PROFILES.md](PROFILES.md)** para o guia completo explicando
> o que é um profile, para que ele serve, todos os parâmetros disponíveis
> e como criar/calibrar um para um novo cliente.

Exemplo completo (`profiles/exemplo.json`):

```json
{
  "cliente": "MRN (exemplo)",
  "versao": "1.0",
  "parameters": {
    "logomarcas.minimo": "2",
    "quadroCaracteristicas.aliases": "Características do Documento|Características Técnicas|Quadro de Características|Características",
    "iniciais.rotuloElaborador": "Elaborado por|Emissor|Elaborador",
    "iniciais.rotuloVerificador": "Verificado por|Verificador|Verificador Técnico",
    "iniciais.rotuloAprovador": "Aprovado por|Aprovador",
    "codificacao.pdaRegex": "^[A-Z]{2}-\\d{3}-[A-Z]{2}-\\d{5}$",
    "codificacao.clienteRegex": "^[A-Z0-9]{2,4}-[A-Z]{2,4}-\\d{2}-\\d{2}-\\d{3}-[A-Z]{2}$",
    "codificacao.aliasesRotulo": "Codificação PdA|Codificação|Código do Documento|Documento|Código",
    "revisao.aliasesRotulo": "Revisão|Rev.|Rev",
    "quadro.camposObrigatorios": "Codificação|Título|Revisão|Data|Elaborado por|Verificado por|Aprovado por",
    "quadro.aliases.Elaborado por": "Elaborado por|Elaborador|Emissor",
    "quadro.aliases.Verificado por": "Verificado por|Verificador|Verificador Técnico",
    "quadro.aliases.Aprovado por": "Aprovado por|Aprovador",
    "folhaRosto.paragrafosIniciais": "60",
    "folhaRosto.contratante": "",
    "folhaRosto.titulo": ""
  }
}
```

Todos os valores aceitam múltiplos aliases separados por `|`.

### Parâmetros de profile

| Chave | Descrição | Default |
|---|---|---|
| `logomarcas.minimo` | Mínimo de imagens no cabeçalho | `2` |
| `quadroCaracteristicas.aliases` | Termos que identificam o quadro Características | `Características do Documento|...` |
| `iniciais.rotuloElaborador` / `rotuloVerificador` / `rotuloAprovador` | Rótulos do quadro para extrair iniciais | ver exemplo |
| `codificacao.pdaRegex` | Regex da codificação PdA **sem** o sufixo de revisão (15 caracteres, ex.: `RN-816-RL-67456`) | obrigatório p/ regras de codificação |
| `codificacao.clienteRegex` | Regex da codificação do Cliente sem o sufixo de revisão (ex.: `QD5-PDA-26-04-095-RT`) | opcional |
| `codificacao.aliasesRotulo` | Rótulos do quadro que contêm a codificação | `Codificação PdA|Codificação|...` |
| `revisao.aliasesRotulo` | Rótulos do campo de revisão | `Revisão|Rev.|Rev` |
| `quadro.camposObrigatorios` | Lista de campos que devem estar preenchidos no quadro | ver exemplo |
| `quadro.aliases.<campo>` | Aliases por campo individual do quadro | defaults internos |
| `folhaRosto.paragrafosIniciais` | Limite da folha de rosto **só** em documento sem título nem índice (com eles, a capa vai até o primeiro título de nível 1) | `600` |
| `folhaRosto.contratante` | Nome da empresa contratante a procurar na folha de rosto | (opcional) |
| `folhaRosto.titulo` | Título esperado do documento | (opcional) |
| `referenciasCruzadas.marcasErro` | Marcas que indicam erro em referência cruzada (separadas por `\|`) | `Erro! Indicador não definido\|Erro! Fonte de referência\|...` |
| `indice.marcasErro` | Marcas que indicam falha de atualização do índice | `Erro! Indicador não definido\|Erro! Nenhuma entrada\|...` |
| `corpo.fonte` / `corpo.tamanho` | Fonte e corpo esperados no texto | `Times New Roman` / `12` |
| `corpo.alinhamento` | Alinhamento esperado (`both`, `left`, …). Só cobrado se declarado | (não cobrado) |
| `corpo.margemSuperior/Inferior/Esquerda/Direita` | Margens em twips. Só cobradas se declaradas | (não cobradas) |
| `corpo.toleranciaPercentual` | % de parágrafos divergentes tolerado antes de acusar | `10` |
| `elementosGraficos.toleranciaPercentual` | % de figuras/legendas fora do padrão tolerado | `20` |
| `tarja.comentariosCliente` | Textos aceitos como tarja de etapa 0A–0Z | `Emissão para Comentários do Cliente\|...` |
| `tarja.naoValidoExecucao` | Textos aceitos como tarja de emissão final | `Não é Válido para Execução\|...` |
| `tarja.exigeNaoValidoExecucao` | `true` para cobrar a tarja em revisões 00+ (depende do tipo de projeto) | `false` |
| `numeracao.exigirDireita` | `true` para exigir numeração alinhada à direita no cabeçalho | `false` |
| `painelNavegacao.comprimentoMaximoTitulo` | Acima disso um "título" é tratado como corpo mal marcado | `200` |
| `semantico.regrasHabilitadas` | Refs que o motor semântico pode assumir (`\|`-separado, ou `*`) | (vazio = motor desligado) |
| `semantico.modelo.default` | Modelo das regras semânticas deste cliente | `OpenAI:Model` (`gpt-6.1-sol`) |
| `semantico.modelo.<Ref>` | Override de modelo por regra (ex.: `gpt-6-luna`) | usa o default |

Os regexes de codificação descrevem o código **sem** sufixo de revisão; a busca aceita sozinha
`-00`/`-0A` (PdA) e `-1`/`-12` (Cliente) e exige fronteira dos dois lados. O sufixo é usado
por `PS-002:4.3.3 (letra i)` para conferir a revisão vigente.

**Validação:** parâmetros marcados como regex (`codificacao.pdaRegex`,
`codificacao.clienteRegex`) e numéricos (`logomarcas.minimo`,
`folhaRosto.paragrafosIniciais`) são validados no load do profile;
profile inválido provoca exceção imediata em vez de falhar silenciosamente.

---

## Checks implementados

**44 entradas do CL-001** com `IRuleCheck` dedicado — **43** quando o LLM está desligado, já
que `PS-002:4.3.2:Cliente` exige avaliação semântica — mais **18** elegíveis ao motor
semântico no `profiles/exemplo.json`. Total: 62 de 86.

As 24 restantes dependem de sistemas externos ao `.docx`: 22 entradas do PS-005 (fluxo
Meridian, e-mails ao GQ, autoridade do aprovador) e as 2 de `4.3.3 (letra b)`, que exigem a
tabela oficial de iniciais do PL-011.

> **Cabeçalhos considerados.** O extrator entrega aos checks só os cabeçalhos e rodapés que o
> Word **exibe**: o `first` só em seção com "primeira página diferente" (`w:titlePg`), o `even`
> só com "pares e ímpares diferentes" (`w:evenAndOddHeaders`), com a herança entre seções. Os
> demais ficam em `DocumentStructure.HiddenHeaderFooters` e só geram aviso. Antes todos eram
> tratados como visíveis — e o RN-816 era reprovado por uma tarja que nenhuma página mostra.

### PS-002 — Edição de Documentos Técnicos

| Check | Ref | Descrição |
|---|---|---|
| `LogomarcasNoHeaderCheck` | `PS-002:4.1:Cliente` | Ao menos `logomarcas.minimo` imagens num mesmo cabeçalho exibido |
| `FolhaRostoCheck` | `PS-002:4.3.1:Pda` | Capa (até o 1º título) contém empresa, título, mês/ano e codificação PdA |
| `FolhaRostoVsCaracteristicasCheck` (LLM) | `PS-002:4.3.2:Cliente` | Coerência semântica folha × quadro, sobre o pacote de evidências compartilhado |
| `ContinuidadeTituloConteudoCheck` × 2 | `PS-002:4.3.3 (a):Cliente/Pda` | Quebra depois do título (erro) e título no pé da página na última paginação (`lastRenderedPageBreak`, aviso) |
| `IniciaisDistintasCheck` × 2 | `PS-002:4.3.3 (c):Cliente/Pda` | Elaborador ∩ verificador = ∅ na revisão vigente do histórico |
| `ConsistenciaIniciaisCheck` × 2 | `PS-002:4.3.3 (d):Cliente/Pda` | Iniciais da revisão vigente constam na folha de rosto |
| `QuadroCaracteristicasPreenchidoCheck` (item `4.3.3 (letra e)`) | `PS-002:4.3.3 (e):Cliente` | Campos obrigatórios preenchidos (revisão vigente, "Rótulo: valor", formulário) |
| `PaginacaoAtualizadaCheck` × 2 (item `4.3.3 (letra f)`) | `PS-002:4.3.3 (f):Cliente/Pda` | Paginação atualizada |
| `CabecalhosPadronizadosCheck` × 2 | `PS-002:4.3.3 (g):Cliente/Pda` | Cabeçalhos exibidos uniformes entre seções |
| `ReferenciasCruzadasCheck` × 2 | `PS-002:4.3.3 (h):Cliente/Pda` | Marcas "Erro! …" (PT/EN) e REF/PAGEREF/NOTEREF para indicador inexistente |
| `CoerenciaRevisoesCheck` × 2 | `PS-002:4.3.3 (i):Cliente/Pda` | Sufixo das codificações × revisão vigente; emissões ao Cliente × revisões PdA por data |
| `IndiceAtualizadoCheck` | `PS-002:4.3.5.1:Cliente` | TOC presente e sem marcas de erro |
| `PaginacaoAtualizadaCheck` (item `4.3.6.6`) | `PS-002:4.3.6.6:Cliente` | Numeração das páginas (padrão Cliente) |
| `QuadroCaracteristicasPreenchidoCheck` (item `4.7`) | `PS-002:4.7:Cliente` | Quadro Características completo |

#### Regras que o CL-001 marcava `IA=Não` e passaram a ser automatizadas

| Check | Ref | Sinal usado |
|---|---|---|
| `IndicePaginaCheck` | `PS-002:4.3.4.1:Pda` | Contratante identificado na página do índice |
| `IndiceConteudoCheck` × 2 | `PS-002:4.3.4.3/4.3.4.4:Pda` | Títulos ≤ nível 2 e elementos gráficos presentes no índice |
| `PainelNavegacaoCheck` × 2 | `PS-002:4.3.4.6:Pda` · `4.3.5.2:Cliente` | Hierarquia de outline levels sem saltos |
| `FormatacaoCorpoCheck` × 2 | `PS-002:4.3.6.1:Pda` · `4.3.6.2:Cliente` | Fonte/tamanho/alinhamento/margens efetivos |
| `ElementosGraficosCheck` × 2 | `PS-002:4.3.6.3:Pda` · `4.3.6.4:Cliente` | Centralização e posição das legendas |
| `NumeracaoPaginasCheck` | `PS-002:4.3.6.5:Pda` | Campo `PAGE` em cabeçalho, ausente na capa |
| `ApendiceAnexoCheck` × 2 | `PS-002:4.3.8:Pda/Cliente` | Apêndices referenciados no quadro Características |

### PS-024 — Emissão de Documentos Técnicos

| Check | Ref | Sinal usado |
|---|---|---|
| `TarjaEmissaoCheck` × 6 | `PS-024:4.1.2/4.1.3/4.1.4:Pda/Cliente` | Tarja de emissão condicionada à revisão do quadro |

### PS-018 — Codificação de Documentos

| Check | Ref | Descrição |
|---|---|---|
| `CodificacaoTecnicaCheck` | `PS-018:4.3:Cliente` | Nome do arquivo e as duas codificações (PdA e Cliente) no quadro |
| `LocalizacaoCodificacaoCheck` | `PS-018:4.3.2 (a):Cliente` | Coerência entre arquivo, folha de rosto (sem cabeçalhos) e quadro |
| `CodificacaoClienteCheck` | `PS-018:4.7:Cliente` | Codificação do Cliente presente no quadro |
| `EvolucaoDocumentoCheck` | `PS-018:4.8:Cliente` | Histórico em sequência crescente (0A → … → 00 → 01), no padrão e com datas que não regridem |

Todos registrados em `CheckRegistry.Deterministicos()` — a mesma lista que o CLI executa e
que `CatalogCoverageTests` audita.

---

## Uso de LLM (semantic checker)

Modelo: **`gpt-6.1-sol`** com `reasoning_effort` **medium**, para todas as regras
(`OpenAI:Model` e `OpenAI:ReasoningEffort`). O profile ainda aceita `semantico.modelo.<Ref>`
para trocar o modelo de uma regra específica, mas o `exemplo.json` não usa.

**Custo medido** (outubro/2026, `profiles/exemplo.json`, 20 chamadas por documento):

| Documento | Entrada | em cache | Saída | Custo | Tempo |
|---|---:|---:|---:|---:|---:|
| `RN799RL6496600.docx` | 160.574 | 93% | 3.851 | US$ 0,080 | 34 s |
| `RN-816-RL-67456-00.docx` | 171.745 | 93% | 3.682 | US$ 0,081 | 33 s |

O que segura o custo é o **cache de prompt**. O pacote de evidências (~8 mil tokens) é igual
para todas as regras do documento e vai numa mensagem de sistema *antes* do item do checklist;
o cache implícito da OpenAI marca o fim desse bloco como fronteira, e as 19 chamadas seguintes
leem o pacote a 0,05× da tarifa. A primeira chamada de cada documento vai sozinha e as demais
esperam (`OpenAiSemanticChecker.CompletarAsync`), porque seis chamadas simultâneas sobre o cache
frio pagariam seis gravações — e no `gpt-6.1-sol` gravar custa 1,25× a entrada. Com o item
antes da evidência, como era, nenhuma chamada acertava o cache.

### Checks dedicados que usam LLM

| Regra | LLM | Comportamento |
|---|---|---|
| `PS-002:4.3.2:Cliente` | **obrigatório** | Compatibilidade folha de rosto × quadro Características, sobre o mesmo pacote de evidências do motor genérico (e no mesmo cache). |
| `PS-002:4.3.1:Pda` | **opcional / fallback** | Quando habilitado, confirma semanticamente que o texto extraído realmente contém empresa + título + data + codificação. |
| `PS-002:4.3.3 (g):Cliente` | **opcional / override** | Quando habilitado e a heurística detectou divergência de cabeçalhos, o LLM pode aprovar caso as diferenças sejam apenas de formatação. |

### Motor semântico genérico

`SemanticChecklistCheck` assume qualquer entrada do checklist listada em
`semantico.regrasHabilitadas`, usando a coluna *Descrição* da planilha como instrução e um
pacote de evidências (`DefaultEvidenceSelector`) como conteúdo. É o que permite cobrir dezenas
de itens sem uma classe C# por regra.

O contrato distingue três estados — `conforme`, `nao_aplicavel` e `nao_conforme`. O estado
**não-aplicável** é essencial: muitos itens do CL-001 são condicionais ("quando aplicável",
"no caso de DCE…"), e tratá-los como reprovação geraria falso positivo em massa. Cada achado
traz um trecho-âncora, resolvido para o `ParagraphId` real do documento, de modo que o
comentário caia no parágrafo certo.

A habilitação é por allow-list explícita, nunca automática — rodar o PS-005 custaria tokens
para produzir "não aplicável" em todos os itens.

Quando o LLM está desativado (`--no-llm` ou sem `OPENAI_API_KEY`), essas regras
caem no caminho regex-only / são puladas com nota explicativa.

---

## Validação contra documentos reais

Última execução com `profiles/exemplo.json`:

| Documento | LLM | passou | falhou | pulado | erro |
|---|---|---:|---:|---:|---:|
| `templates/RN799RL6496600.docx` | não | 27 | 0 | 59 | 0 |
| `templates/RN-816-RL-67456-00.docx` | não | 31 | 2 | 53 | 0 |
| `templates/RN799RL6496600.docx` | `gpt-6.1-sol` medium | 35 | 2 | 49 | 0 |
| `templates/RN-816-RL-67456-00.docx` | `gpt-6.1-sol` medium | 37 | 4 | 45 | 0 |

Sem LLM, o RN799 não produz falha. O RN-816 tem um defeito real que nenhuma versão anterior
via: **quatro campos de referência cruzada vazios**, logo depois de "A Figura 77", "Figura 88",
"Figura 91" e "Figura 92", apontam para indicadores que não existem no arquivo. As referências
visíveis estão certas; os campos-fantasma não mostram nada hoje, mas ao atualizar os campos o Word
escreve "Erro! Indicador não definido" no meio da frase (`RealDocumentCheckTests.DefeitosConhecidos`).

Com LLM aparecem achados semânticos genuínos: erros de concordância, regência e crase, erros
de digitação ("Avalição", "gos" por "dos") e a assinatura do aprovador em branco no quadro.

Saíram desta lista dois falsos positivos que as versões anteriores reportavam:
- **Tarja "Emissão para Comentários do Cliente" no RN-816 (erro em `PS-024:4.1.2`, mais quatro
  achados do LLM sobre "status de emissão incoerente").** A tarja só existe em cabeçalhos
  `first`/`even` que o Word não exibe — agora é um aviso de conteúdo latente.
- **"Divergência de revisão" folha de rosto × quadro (`PS-002:4.3.2`), nos dois documentos.**
  Revisões do Cliente (0, 1, 2) e da PdA (0A, 0B, 00) são sistemas diferentes que se
  correspondem por data; o check montava a própria evidência com os 40 primeiros parágrafos,
  que nem chegavam à folha de rosto. Hoje usa o pacote compartilhado, que traz as convenções de
  leitura, e a correspondência por data é conferida de forma determinística em `4.3.3 (letra i)`.

> ⚠️ **Precisão do motor semântico.** Nem todo achado do LLM é correto. Numa das execuções,
> `PS-002:4.7:Pda` acusou "numeração de páginas dentro do quadro Características" — o modelo
> leu a folha-índice (que lista página por revisão) como se fosse paginação do quadro.
> Achados semânticos são um ponto de partida para revisão humana, não um veredito.
> Regras com sinal determinístico devem continuar ganhando um `IRuleCheck` dedicado.

O tempo total de uma revisão com LLM fica em ~35 s: os checks rodam concorrentemente
(`ChecklistEngine`, `maxParalelismo` = 6) e o pacote de evidências é montado uma vez por
documento, não uma vez por regra. Sem LLM, o RN-816 (328 MB, quase tudo imagem) leva ~2,5 s.

Arquivo de saída (`output/revisado.docx`):
- Comentários OpenXML inseridos para cada violação detectada.
- Nada mais é alterado: o `settings.xml` fica como estava. O `UpdateFieldsOnOpen=true`
  que o revisor gravava ali fazia o Word abrir perguntando "Deseja atualizar os campos?"
  em todo documento revisado; quem quiser recalcular usa Ctrl+A e F9.

---

## Como adicionar um novo `IRuleCheck`

1. Crie `src/Infrastructure/Checks/MinhaRegraCheck.cs`:

```csharp
using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Core.Models;

public sealed class MinhaRegraCheck : IRuleCheck
{
    public ChecklistRef Ref { get; } = new("PS-002", "4.x.y", ChecklistPadrao.Cliente);

    public Task<RuleCheckResult> RunAsync(DocumentContext ctx, CancellationToken ct = default)
    {
        var violations = new List<Violation>();
        // ... lógica usando ctx.Structure e ctx.Profile ...
        var status = violations.Count == 0 ? CheckStatus.Passed : CheckStatus.Failed;
        return Task.FromResult(new RuleCheckResult(Ref, status, violations));
    }
}
```

2. Registre em `src/Infrastructure/Checks/CheckRegistry.cs`. O `Ref` precisa existir no
   CL-001 — `CatalogCoverageTests` falha se você registrar um check órfão.
3. Adicione **os dois lados** do teste em `tests/Infrastructure.Tests/Checks/`: o caso
   positivo (documento conforme passa) e o negativo (`NegativeCorpusTests`, com
   `DocxFixtureBuilder`, provando que o defeito é detectado).
4. Se a regra estava na allow-list semântica do profile, remova-a de lá — um check dedicado
   é mais barato e mais auditável que uma chamada de LLM.

Para regras que se aplicam a múltiplos padrões (Cliente e PdA), parametrize o
construtor com `ChecklistPadrao` e registre duas instâncias — veja
`IniciaisDistintasCheck` ou `ContinuidadeTituloConteudoCheck` como referência.

### Convenção de severidade e status

- `Severity.Error` → o check retorna `Failed` e o CLI sai com código `2`.
- `Severity.Warning` → o check retorna `Skipped`, mas a violação **ainda vira comentário**
  no `.docx`. É o canal para indícios que merecem olhar humano sem reprovar o documento.
- Sem evidência suficiente → `Skipped` com `Note` explicando por quê. Nunca invente uma
  reprovação a partir de ausência de dado.

---

## Modelo de dados relevante

```csharp
record Violation(string RuleId, Severity Severity, string Message, ViolationLocation? Location);
enum Severity { Info, Warning, Error }

record RuleCheckResult(ChecklistRef Ref, CheckStatus Status,
                       IReadOnlyList<Violation> Violations, string? Note = null);
enum CheckStatus { Passed, Failed, Skipped, Error }

class DocumentContext(string SourcePath, DocumentStructure Structure, ClientProfile Profile);

record DocumentStructure(
    string FileName,
    IReadOnlyDictionary<string, ExtractedStyle> Styles,
    IReadOnlyList<ExtractedHeaderFooter> Headers,      // + ParagraphAlignments, SectionIndex
    IReadOnlyList<ExtractedHeaderFooter> Footers,
    IReadOnlyList<ExtractedSection> Sections,
    IReadOnlyList<ExtractedParagraph> Paragraphs,
    IReadOnlyList<ExtractedTable> Tables,
    bool HasPendingTrackChanges,
    bool HasOpenComments,
    bool HasUpdatedToc,
    IReadOnlyList<string> BodyFieldCodes,              // Campos OOXML do body (TOC, REF, ...)
    ExtractedDocumentProperties? Properties);          // Propriedades do pacote OOXML

record ExtractedParagraph(
    string ParagraphId, string? StyleId, string Text, bool EndsWithPageBreak,
    string? Alignment,          // valor bruto do OOXML: "left"/"center"/"right"/"both"
    int? OutlineLevel,          // 0 = Título 1; null em parágrafos de corpo
    int? NumberingId,
    string? EffectiveFont,      // formatação direta do run resolvida sobre o estilo
    double? EffectiveFontSize,
    int ImageCount, int SectionIndex, bool IsInTable,
    IReadOnlyList<string> FieldCodes);                 // PAGEREF identifica entradas de índice
```

Detalhes importantes da extração:

- **`ExtractedParagraph.EffectiveFont/EffectiveFontSize`** — resolvidos percorrendo a cadeia
  `w:basedOn` e os `docDefaults`, com a formatação direta do run tendo precedência sobre o
  estilo, ponderada pelo comprimento do texto de cada run. Sem isso, um documento com estilo
  "Normal/Arial" mas runs marcados Times New Roman seria julgado pelo estilo, não pelo que o
  leitor vê.
- **`ExtractedParagraph.OutlineLevel`** — vem de `w:outlineLvl` (próprio ou herdado). Os
  documentos reais usam estilos de título customizados (`Ttulo1MRN`, `PDA-T1`, `Estilo1`),
  então o nível **não** pode ser inferido do nome do estilo.
- **`ExtractedParagraph.FieldCodes`** — entradas de índice carregam `PAGEREF`. É o que as
  distingue de parágrafos de corpo com texto parecido; os documentos de referência **não**
  usam os estilos `TOC1`/`TOC2` do Word.
- **Valores enumerados** são lidos via `InnerText`, nunca via `ToString()`: a partir do
  OpenXML SDK 3.x esses tipos são structs e `ToString()` devolve `"JustificationValues { }"`.
  `DocxStructureExtractorTests` guarda essa regressão.

- **`ExtractedHeaderFooter.Kind`** — `"default"`, `"first"` (cabeçalho de capa)
  ou `"even"` (páginas pares) resolvido a partir de
  `HeaderReference/FooterReference` no XML do documento.
- **`ExtractedHeaderFooter.FieldCodes`** — nomes dos campos OOXML presentes
  (`"PAGE"`, `"NUMPAGES"`, `"TOC"`, etc.) — usado pelas regras de paginação
  para detectar campos sem texto extraído.
- **`ExtractedParagraph.EndsWithPageBreak`** — `true` quando o parágrafo
  contém `<w:br w:type="page"/>` ou tem `PageBreakBefore`. Usado pela regra
  de continuidade título/conteúdo.

---

## Roadmap (próximos refinamentos sugeridos)

Veja `TODO_REGRAS.md` para o estado detalhado regra a regra. Próximas melhorias
técnicas pendentes:

1. **Convenção aviso × não conformidade** — vários checks devolvem só `Warning` quando o
   requisito está ausente (sem índice, sem numeração, elementos gráficos fora do padrão) e o
   item conta como "não aplicável". Decidir, regra a regra, o que deve reprovar.
2. **Requisitos decidíveis ainda não cobertos** — quadro "ao final" (4.3.6.6), legendas em
   Times 12 e tabelas centralizadas (4.3.6.3), ordem das entradas do índice (4.3.4.4),
   citação de documentos com 15/18 caracteres (PS-018 4.4), "DOCUMENTO CANCELADO" (PS-024 4.3).
3. **Gravação com menos memória** — o `CommentInserter` abre a cópia em modo de edição; no
   RN-816 (328 MB) o pico chega a ~450 MB. Copiar as entradas do zip cruas e reescrever só
   document/comments/settings resolveria.
4. **Mais documentos reais** — `templates/` com outros clientes e com revisões 0A–0Z, para
   exercitar a tarja de comentários de verdade.

---

## Segurança

- `OPENAI_API_KEY` **nunca** deve ser commitada. Use variável de ambiente ou
  `src/Cli/appsettings.Development.json` (ignorado pelo `.gitignore`).
- `appsettings.json` é rastreado pelo Git e contém apenas chave vazia como placeholder.
- `appsettings.Development.json` é copiado para o diretório de saída pelo `.csproj`
  (`CopyToOutputDirectory`) — sem isso a chave não seria encontrada em runtime, porque
  `Program.cs` lê a configuração de `AppContext.BaseDirectory`.
