# Word Compliance Validator

Valida documentos `.docx` contra o checklist **CL-001** (planilha Excel), por cliente. Insere comentários OpenXML nas violações encontradas.

**Stack:** C# / .NET 8 · Open XML SDK · OpenAI (opcional) · Excel (OOXML)

---

## Arquitetura

```
CL-001.xlsx (checklist real)
    ↓ ExcelChecklistRepository
ChecklistEngine
    ↓ itera 86 entradas do checklist
    ↓ encontra IRuleCheck registrada por ChecklistRef
    ↓ executa RunAsync(DocumentContext)
    ↓ captura violações e status (Passed / Failed / Skipped / Error)
DocumentReviewService
    ↓ agrega resultados
    ↓ CommentInserter → .docx com comentários
```

Cada regra do checklist com suporte a automação tem uma implementação concreta de `IRuleCheck`. Regras marcadas como "revisão manual" são automaticamente marcadas como Skipped.

---

## Estrutura do projeto

```
EstilizacaoWordComRegras.sln
src/
  Core/
    Abstractions/       IDocxStructureExtractor
    Checklist/          ChecklistEntry, ChecklistRef, ChecklistPadrao
    Checks/             IRuleCheck, RuleCheckResult, CheckStatus, ChecklistEngine
                        ISemanticChecker, DocumentContext
    Models/             DocumentStructure, ExtractedTable*, Violation, Severity
    Profile/            ClientProfile
  Infrastructure/
    Checks/             LogomarcasNoHeaderCheck, IniciaisDistintasCheck,
                        CodificacaoTecnicaCheck, FolhaRostoVsCaracteristicasCheck
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
  Core.Tests/
    Checks/             ChecklistEngineTests (4 testes)
  Infrastructure.Tests/
    Checks/             IniciaisDistintasCheckTests (3 testes)
    Excel/              ExcelChecklistRepositoryTests (1 teste)
templates/
  checklists/           CL-001-CL00100.xlsx  ← checklist real (86 linhas)
  RN799RL6496600.docx   ← documento de teste real
profiles/
  exemplo.json          ← perfil MRN de exemplo
```

---

## Pré-requisitos

- .NET 8 SDK
- `OPENAI_API_KEY` (env var) — necessário apenas para checks semânticos (LLM)

---

## Build & Testes

```bash
dotnet build EstilizacaoWordComRegras.sln
dotnet test  EstilizacaoWordComRegras.sln
```

Resultado esperado: **8 testes, todos passando**.

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

Com LLM:
```bash
export OPENAI_API_KEY=sk-...
dotnet run --project src/Cli -- review \
  --doc     templates/RN799RL6496600.docx \
  --profile profiles/exemplo.json \
  --out     output/revisado.docx
```

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

```json
{
  "cliente": "Nome do cliente",
  "versao": "1.0",
  "parameters": {
    "logomarcas.minimo": "2",
    "quadroCaracteristicas.aliases": "Características do Documento|Quadro de Características",
    "iniciais.rotuloElaborador": "Elaborado por|Emissor|Elaborador",
    "iniciais.rotuloVerificador": "Verificado por|Verificador|Verificador Técnico",
    "iniciais.rotuloAprovador": "Aprovado por|Aprovador",
    "codificacao.pdaRegex": "^[A-Z]{2}\\d{3}-PDA-\\d{2}-\\d{2}-\\d{3}-[A-Z]{2}$",
    "codificacao.clienteRegex": "^[A-Z0-9]{2,4}-[A-Z]{2,4}-\\d{2}-\\d{2}-\\d{3}-[A-Z]{2}$",
    "codificacao.aliasesRotulo": "Codificação PdA|Codificação|Código do Documento|Documento|Código"
  }
}
```

Todos os valores aceitam múltiplos aliases separados por `|`.

---

## Checks implementados (4 de 21 automatizáveis)

| Check | Ref | Descrição |
|---|---|---|
| `LogomarcasNoHeaderCheck` | PS-002:4.1:Cliente | Verifica se há `logomarcas.minimo` imagens no cabeçalho |
| `IniciaisDistintasCheck` | PS-002:4.3.3 (letra c):Cliente | Elaborador ≠ Verificador no quadro Características |
| `CodificacaoTecnicaCheck` | PS-018:4.3:Cliente | Valida codificação PdA no nome do arquivo e no quadro |
| `FolhaRostoVsCaracteristicasCheck` | PS-002:4.3.2:Cliente | (LLM) Coerência entre folha de rosto e quadro Características |

---

## Como adicionar um novo `IRuleCheck`

1. Crie `src/Infrastructure/Checks/MinhaRegraCheck.cs`:

```csharp
using WordComplianceValidator.Core.Checklist;
using WordComplianceValidator.Core.Checks;
using WordComplianceValidator.Core.Models;

public sealed class MinhaRegraCheck : IRuleCheck
{
    public ChecklistRef Ref { get; } = new("PS-002", "4.3.3 (letra d)", ChecklistPadrao.Cliente);

    public Task<RuleCheckResult> RunAsync(DocumentContext ctx, CancellationToken ct = default)
    {
        // lógica de validação usando ctx.Structure e ctx.Profile
        var violations = new List<Violation>();
        // ...
        var status = violations.Count == 0 ? CheckStatus.Passed : CheckStatus.Failed;
        return Task.FromResult(new RuleCheckResult(Ref, status, violations));
    }
}
```

2. Registre em `src/Cli/Program.cs`:

```csharp
var checks = new List<IRuleCheck>
{
    new LogomarcasNoHeaderCheck(),
    new IniciaisDistintasCheck(),
    new CodificacaoTecnicaCheck(),
    new MinhaRegraCheck(),   // ← adicionar aqui
    // ...
};
```

---

## Checks pendentes (prioritários)

Todos mapeiam entradas do CL-001 com `IA=Sim`. Refs no formato `PS:Item:Padrao`.

| Ref | Descrição |
|---|---|
| `PS-002:4.3.3 (letra c):Pda` | Iniciais distintas — padrão PdA |
| `PS-002:4.3.3 (letra d):Cliente/Pda` | Consistência de iniciais (folha de rosto × Meridian × quadro) |
| `PS-002:4.3.3 (letra e):Cliente` | Campos obrigatórios preenchidos |
| `PS-002:4.3.3 (letra f):Cliente` | Paginação atualizada |
| `PS-002:4.3.3 (letra g):Cliente` | Padronização de cabeçalhos |
| `PS-002:4.3.3 (letra h):Cliente` | Correção de referências cruzadas |
| `PS-002:4.3.3 (letra i):Cliente` | Coerência entre revisões |
| `PS-002:4.3.5.1:Cliente` | Índice atualizado |
| `PS-002:4.3.6.6:Cliente` | Numeração de páginas |
| `PS-002:4.7:Cliente` | Quadro Características preenchido |
| `PS-018:4.3.2 (letra a):Cliente` | Localização da codificação |
| `PS-018:4.7:Cliente` | Codificação do cliente |
| `PS-018:4.8:Cliente` | Evolução do documento |

---

## Problemas conhecidos / calibração pendente

### `IniciaisDistintasCheck` — documento `RN799RL6496600.docx`

No documento de teste real, "Emissor" e "Verificador" são **cabeçalhos de coluna** no histórico de revisões dentro da tabela "Características do Documento", não rótulos de linha. Existe um fallback `FieldsByColumn` em `QuadroCaracteristicas` que deve detectar esse layout, mas ainda retorna `Warning` para esse documento. **Investigar:** talvez o índice de coluna não bata por células mescladas; verificar com dump de `ExtractedTableCell` para `table[16]`.

### `CodificacaoTecnicaCheck` — nome do arquivo

O arquivo `RN799RL6496600.docx` tem nome em código Meridian (não PdA). O check emite `Warning` (não `Error`) para o nome do arquivo — comportamento esperado, pois a codificação PdA correta (`RN799-PDA-26-04-022-RT`) está dentro do documento. O campo "Codificação PdA" no quadro Características também não está sendo encontrado ainda — ajustar `codificacao.aliasesRotulo` no perfil.

---

## Modelo de dados relevante

```csharp
// Violação encontrada por um check
record Violation(string RuleId, Severity Severity, string Message, ViolationLocation Location);
enum Severity { Info, Warning, Error }

// Resultado por entrada do checklist
record RuleCheckResult(ChecklistRef Ref, CheckStatus Status,
                       IReadOnlyList<Violation> Violations, string? Note = null);
enum CheckStatus { Passed, Failed, Skipped, Error }

// Contexto passado para cada check
class DocumentContext(string SourcePath, DocumentStructure Structure, ClientProfile Profile);

// Estrutura extraída do .docx
record DocumentStructure(
    string FileName,
    IReadOnlyDictionary<string, ExtractedStyle> Styles,
    IReadOnlyList<ExtractedHeaderFooter> Headers,
    IReadOnlyList<ExtractedHeaderFooter> Footers,
    IReadOnlyList<ExtractedSection> Sections,
    IReadOnlyList<ExtractedParagraph> Paragraphs,
    IReadOnlyList<ExtractedTable> Tables,
    bool HasPendingTrackChanges,
    bool HasOpenComments,
    bool HasUpdatedToc);
```

---

## Segurança

- `OPENAI_API_KEY` **nunca** deve ser commitada. Use variável de ambiente ou `appsettings.Development.json` (ignorado pelo `.gitignore`).
- `appsettings.json` contém apenas chave vazia como placeholder.
