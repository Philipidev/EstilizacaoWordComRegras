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

Cada regra do checklist com suporte a automação tem uma implementação concreta
de `IRuleCheck`. Regras marcadas como "revisão manual" (`IA=Não` / `—` no CL-001)
são automaticamente marcadas como `Skipped` pelo `ChecklistEngine`.

---

## Estrutura do projeto

```
EstilizacaoWordComRegras.sln
src/
  Core/
    Abstractions/       IDocxStructureExtractor, ICommentInserter
    Checklist/          ChecklistEntry, ChecklistRef, ChecklistPadrao, IChecklistRepository
    Checks/             IRuleCheck, RuleCheckResult, CheckStatus, ChecklistEngine,
                        ISemanticChecker, SemanticVerdict, DocumentContext
    Models/             DocumentStructure, ExtractedTable*, Violation, Severity
    Profile/            ClientProfile, IClientProfileRepository
  Infrastructure/
    Checks/             LogomarcasNoHeaderCheck, IniciaisDistintasCheck,
                        ConsistenciaIniciaisCheck,
                        QuadroCaracteristicasPreenchidoCheck,
                        IndiceAtualizadoCheck, PaginacaoAtualizadaCheck,
                        CabecalhosPadronizadosCheck, ReferenciasCruzadasCheck,
                        CoerenciaRevisoesCheck, ContinuidadeTituloConteudoCheck,
                        CodificacaoTecnicaCheck, LocalizacaoCodificacaoCheck,
                        CodificacaoClienteCheck, EvolucaoDocumentoCheck,
                        FolhaRostoCheck, FolhaRostoVsCaracteristicasCheck,
                        QuadroCaracteristicas (helper)
    Excel/              ExcelChecklistRepository
    OpenAI/             OpenAiSemanticChecker, OpenAiSettings
    OpenXml/            DocxStructureExtractor, CommentInserter
    Profile/            JsonClientProfileRepository
  Application/
    Services/           DocumentReviewService, DocumentReviewReport
  Cli/
    Program.cs          Comandos: review, dump-checklist
    appsettings.json
tests/
  Core.Tests/           (4 testes)
  Infrastructure.Tests/ (37 testes)
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
- `OPENAI_API_KEY` (env var ou `OpenAI:ApiKey` em `appsettings.json`) — necessário
  apenas para checks semânticos (LLM).

---

## Build & Testes

```bash
dotnet build EstilizacaoWordComRegras.sln
dotnet test  EstilizacaoWordComRegras.sln
```

Resultado esperado: **45 testes, todos passando** (Core 4 + Infrastructure 41).

---

## CLI

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

**Validação:** parâmetros marcados como regex (`codificacao.pdaRegex`,
`codificacao.clienteRegex`) e numéricos (`logomarcas.minimo`,
`folhaRosto.paragrafosIniciais`) são validados no load do profile;
profile inválido provoca exceção imediata em vez de falhar silenciosamente.

---

## Checks implementados (21 de 21 automatizáveis — IA=Sim)

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

### PS-018 — Codificação de Documentos

| Check | Ref | Descrição |
|---|---|---|
| `CodificacaoTecnicaCheck` | `PS-018:4.3:Cliente` | Codificação PdA no nome do arquivo e no quadro |
| `LocalizacaoCodificacaoCheck` | `PS-018:4.3.2 (a):Cliente` | Coerência entre arquivo, folha de rosto e quadro |
| `CodificacaoClienteCheck` | `PS-018:4.7:Cliente` | Codificação do Cliente presente no quadro |
| `EvolucaoDocumentoCheck` | `PS-018:4.8:Cliente` | Revisão segue padrão (0A–0Z, 00, 01–99) |

Total: **17 classes** mapeando para **21 entradas** do CL-001 (`IniciaisDistintasCheck`,
`ConsistenciaIniciaisCheck`, `ContinuidadeTituloConteudoCheck`, `QuadroCaracteristicasPreenchidoCheck`
e `PaginacaoAtualizadaCheck` se autorregistram em mais de uma `Ref`).

---

## Uso de LLM (semantic checker)

| Regra | LLM | Comportamento |
|---|---|---|
| `PS-002:4.3.2:Cliente` | **obrigatório** | Avaliação semântica direta da compatibilidade folha de rosto × quadro Características. |
| `PS-002:4.3.1:Pda` | **opcional / fallback** | Quando habilitado, confirma semanticamente que o texto extraído realmente contém empresa + título + data + codificação. |
| `PS-002:4.3.3 (g):Cliente` | **opcional / override** | Quando habilitado e a heurística detectou divergência de cabeçalhos, o LLM pode aprovar caso as diferenças sejam apenas de formatação. |

Quando o LLM está desativado (`--no-llm` ou sem `OPENAI_API_KEY`), essas regras
caem no caminho regex-only / são puladas com nota explicativa.

---

## Validação contra documento real

Última execução com `templates/RN799RL6496600.docx` + `profiles/exemplo.json`:

Sem LLM (`--no-llm`):

```
Cliente: MRN (exemplo)
Total de itens do checklist: 86
  passou:    19   ← todas as regras IA=Sim estruturais passam
  falhou:     0
  pulado:    67   ← itens manuais (IA=Não / —) + 4.3.3(i) sem "Rev." na folha
  erro:       0
```

Com LLM habilitado (`gpt-5.4-mini`): também executa `PS-002:4.3.2:Cliente`,
que pode levantar `Failed` apenas para divergências reais detectadas via
análise semântica (no doc real: divergência genuína entre revisões da folha
de rosto e do quadro Características).

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

2. Registre em `src/Cli/Program.cs` dentro de `var checks = new List<IRuleCheck> { ... }`.
3. Adicione testes em `tests/Infrastructure.Tests/Checks/`.

Para regras que se aplicam a múltiplos padrões (Cliente e PdA), parametrize o
construtor com `ChecklistPadrao` e registre duas instâncias — veja
`IniciaisDistintasCheck` ou `ContinuidadeTituloConteudoCheck` como referência.

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
    IReadOnlyList<ExtractedHeaderFooter> Headers,      // Kind, Text, ImageCount, FieldCodes
    IReadOnlyList<ExtractedHeaderFooter> Footers,
    IReadOnlyList<ExtractedSection> Sections,
    IReadOnlyList<ExtractedParagraph> Paragraphs,      // ParagraphId, StyleId, Text, EndsWithPageBreak
    IReadOnlyList<ExtractedTable> Tables,
    bool HasPendingTrackChanges,
    bool HasOpenComments,
    bool HasUpdatedToc,
    IReadOnlyList<string> BodyFieldCodes);             // Campos OOXML do body (TOC, REF, ...)
```

Detalhes importantes da extração:

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
  `appsettings.Development.json` (ignorado pelo `.gitignore`).
- `appsettings.json` contém apenas chave vazia como placeholder no Git.
