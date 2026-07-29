# Word Compliance Validator

Valida documentos `.docx` contra o checklist **CL-001** (planilha Excel), por cliente. Insere comentários OpenXML nas violações encontradas.

**Stack:** C# / .NET 8 · Open XML SDK · OpenAI (LLM opcional) · Excel (OOXML)

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
EstilizacaoWordComRegras.sln
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
                        QuadroCaracteristicas, DocumentoTexto (helpers)
    Excel/              ExcelChecklistRepository
    OpenAI/             OpenAiSemanticChecker, OpenAiSettings
    OpenXml/            DocxStructureExtractor, CommentInserter
    Profile/            JsonClientProfileRepository
  Application/
    Services/           DocumentReviewService, DocumentReviewReport
  Cli/
    Program.cs          Comandos: review, dump-checklist
    appsettings.json                 ← rastreado, chave vazia
    appsettings.Development.json     ← ignorado pelo git, guarda a chave local
tests/
  Core.Tests/           (7 testes)
  Infrastructure.Tests/ (86 testes)
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

- .NET 8 SDK
- `OPENAI_API_KEY` — env var, ou `OpenAI:ApiKey` em `src/Cli/appsettings.Development.json`
  (ignorado pelo git). Necessário apenas para checks semânticos (LLM); sem ela o CLI roda os
  checks determinísticos e avisa `[info] LLM desabilitado`.
  **Nunca** coloque a chave em `src/Cli/appsettings.json` — esse arquivo é rastreado.

---

## Build & Testes

```bash
dotnet build EstilizacaoWordComRegras.sln
dotnet test  EstilizacaoWordComRegras.sln
```

Resultado esperado: **93 testes, todos passando** (Core 7 + Infrastructure 86).

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

### `dump-checklist` — listar entradas do checklist (debug)

```bash
dotnet run --project src/Cli -- dump-checklist
# ou com arquivo alternativo:
dotnet run --project src/Cli -- dump-checklist --checklist outro.xlsx
```

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
    "codificacao.pdaRegex": "^[A-Z]{2}\\d{1,3}-PDA-\\d{2}-\\d{2}-\\d{3}-[A-Z]{2}$",
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
| `codificacao.pdaRegex` | Regex que define o padrão PdA | obrigatório p/ regras de codificação |
| `codificacao.clienteRegex` | Regex que define o padrão Cliente | opcional |
| `codificacao.aliasesRotulo` | Rótulos do quadro que contêm a codificação | `Codificação PdA|Codificação|...` |
| `revisao.aliasesRotulo` | Rótulos do campo de revisão | `Revisão|Rev.|Rev` |
| `quadro.camposObrigatorios` | Lista de campos que devem estar preenchidos no quadro | ver exemplo |
| `quadro.aliases.<campo>` | Aliases por campo individual do quadro | defaults internos |
| `folhaRosto.paragrafosIniciais` | Quantos parágrafos iniciais considerar como folha de rosto | `60` |
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
| `semantico.modelo.default` | Modelo padrão das regras semânticas | `gpt-5.6-sol` |
| `semantico.modelo.<Ref>` | Override de modelo por regra (ex.: `gpt-5.6-luna`) | usa o default |

**Validação:** parâmetros marcados como regex (`codificacao.pdaRegex`,
`codificacao.clienteRegex`) e numéricos (`logomarcas.minimo`,
`folhaRosto.paragrafosIniciais`) são validados no load do profile;
profile inválido provoca exceção imediata em vez de falhar silenciosamente.

---

## Checks implementados

**39 entradas do CL-001** com `IRuleCheck` dedicado — **38** quando o LLM está desligado, já
que `PS-002:4.3.2:Cliente` exige avaliação semântica — mais **23** elegíveis ao motor
semântico no `profiles/exemplo.json`. Total: 62 de 86.

As 24 restantes dependem de sistemas externos ao `.docx`: 22 entradas do PS-005 (fluxo
Meridian, e-mails ao GQ, autoridade do aprovador) e as 2 de `4.3.3 (letra b)`, que exigem a
tabela oficial de iniciais do PL-011.

### PS-002 — Edição de Documentos Técnicos

| Check | Ref | Descrição |
|---|---|---|
| `LogomarcasNoHeaderCheck` | `PS-002:4.1:Cliente` | Verifica se há `logomarcas.minimo` imagens no cabeçalho |
| `FolhaRostoCheck` | `PS-002:4.3.1:Pda` | Folha de rosto contém empresa, título, data, codificação |
| `FolhaRostoVsCaracteristicasCheck` (LLM) | `PS-002:4.3.2:Cliente` | Coerência semântica folha × quadro |
| `ContinuidadeTituloConteudoCheck` × 2 | `PS-002:4.3.3 (a):Cliente/Pda` | Continuidade título/conteúdo (heurístico) |
| `IniciaisDistintasCheck` × 2 | `PS-002:4.3.3 (c):Cliente/Pda` | Elaborador ≠ Verificador |
| `ConsistenciaIniciaisCheck` × 2 | `PS-002:4.3.3 (d):Cliente/Pda` | Iniciais consistentes folha × quadro |
| `QuadroCaracteristicasPreenchidoCheck` (item `4.3.3 (letra e)`) | `PS-002:4.3.3 (e):Cliente` | Campos obrigatórios preenchidos |
| `PaginacaoAtualizadaCheck` (item `4.3.3 (letra f)`) | `PS-002:4.3.3 (f):Cliente` | Paginação atualizada |
| `CabecalhosPadronizadosCheck` | `PS-002:4.3.3 (g):Cliente` | Cabeçalhos uniformes entre seções |
| `ReferenciasCruzadasCheck` | `PS-002:4.3.3 (h):Cliente` | Sem marcas "Erro! Indicador não definido" |
| `CoerenciaRevisoesCheck` | `PS-002:4.3.3 (i):Cliente` | Revisão do quadro consta na folha de rosto |
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
| `CodificacaoTecnicaCheck` | `PS-018:4.3:Cliente` | Codificação PdA no nome do arquivo e no quadro |
| `LocalizacaoCodificacaoCheck` | `PS-018:4.3.2 (a):Cliente` | Coerência entre arquivo, folha de rosto e quadro |
| `CodificacaoClienteCheck` | `PS-018:4.7:Cliente` | Codificação do Cliente presente no quadro |
| `EvolucaoDocumentoCheck` | `PS-018:4.8:Cliente` | Revisão segue padrão (0A–0Z, 00, 01–99) |

Todos registrados em `CheckRegistry.Deterministicos()` — a mesma lista que o CLI executa e
que `CatalogCoverageTests` audita.

---

## Uso de LLM (semantic checker)

Modelo padrão: **`gpt-5.6-sol`**. A família GPT-5.6 tem três níveis (Sol, Terra, Luna) e o
profile permite escolher por regra — rodar Sol em 20+ regras por documento é caro, e várias
delas são checagem simples de presença de texto.

### Checks dedicados que usam LLM

| Regra | LLM | Comportamento |
|---|---|---|
| `PS-002:4.3.2:Cliente` | **obrigatório** | Avaliação semântica direta da compatibilidade folha de rosto × quadro Características. |
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
| `templates/RN799RL6496600.docx` | não | 26 | 0 | 60 | 0 |
| `templates/RN-816-RL-67456-00.docx` | não | 32 | 0 | 54 | 0 |
| `templates/RN799RL6496600.docx` | `gpt-5.6-sol` | 31 | 7 | 48 | 0 |
| `templates/RN-816-RL-67456-00.docx` | `gpt-5.6-sol` | 35 | 11 | 40 | 0 |

(Baseline anterior à expansão: 19 / 0 / 67 / 0 nos dois documentos, sem LLM.)

Sem LLM os documentos de referência não produzem nenhuma falha — eles são conformes, e é isso
que `RealDocumentCheckTests` protege. Com LLM aparecem achados semânticos genuínos (erros de
concordância e regência, divergência entre as revisões da folha de rosto e do quadro,
cabeçalho de seção fora do padrão).

> ⚠️ **Precisão do motor semântico.** Nem todo achado do LLM é correto. Numa das execuções,
> `PS-002:4.7:Pda` acusou "numeração de páginas dentro do quadro Características" — o modelo
> leu a folha-índice (que lista página por revisão) como se fosse paginação do quadro.
> Achados semânticos são um ponto de partida para revisão humana, não um veredito.
> Regras com sinal determinístico devem continuar ganhando um `IRuleCheck` dedicado.

O tempo total de uma revisão com LLM fica em ~35 s: os checks rodam concorrentemente
(`ChecklistEngine`, `maxParalelismo` = 6) e o pacote de evidências é montado uma vez por
documento, não uma vez por regra.

Arquivo de saída (`output/revisado.docx`):
- Comentários OpenXML inseridos para cada violação detectada.
- Campo `UpdateFieldsOnOpen=true` definido em `settings.xml` para forçar
  recálculo de TOC, PAGE e referências ao abrir no Word.

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

1. **Paginação** — substituir heurística de texto por leitura direta de campos
   `PAGE`/`NUMPAGES` no OOXML dos headers/footers.
2. **Continuidade título/conteúdo** — detectar `w:br w:type="page"` no XML para
   identificar títulos órfãos com precisão real (em vez de heurística).
3. **Cabeçalhos padronizados** — distinguir `firstPage` / `even` / `default`
   via tipos OOXML ao invés de unificar tudo na extração.
4. **Mais testes de regressão** — usar `templates/` com documentos adicionais para
   cobrir variações de padrão Cliente.

---

## Segurança

- `OPENAI_API_KEY` **nunca** deve ser commitada. Use variável de ambiente ou
  `src/Cli/appsettings.Development.json` (ignorado pelo `.gitignore`).
- `appsettings.json` é rastreado pelo Git e contém apenas chave vazia como placeholder.
- `appsettings.Development.json` é copiado para o diretório de saída pelo `.csproj`
  (`CopyToOutputDirectory`) — sem isso a chave não seria encontrada em runtime, porque
  `Program.cs` lê a configuração de `AppContext.BaseDirectory`.
