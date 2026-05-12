# Word Document Compliance Validator

Sistema para validação, padronização e versionamento de documentos Word (`.docx`) baseado em regras configuráveis por cliente.

**Stack:** C# / .NET 8 · Open XML SDK · OpenAI API · JSON estruturado · Comentários OpenXML em `.docx`.

## Filosofia

```
IA define o padrão.
Usuário homologa.
Sistema fiscaliza.
```

A IA **interpreta** regras textuais e produz JSON estruturado. O sistema **valida deterministicamente** via OpenXML.

## Arquitetura

```
┌────────────────────┐
│ Modelo .docx       │
└─────────┬──────────┘
          ▼
┌────────────────────┐
│ DocxStructureExtractor (Open XML SDK)
└─────────┬──────────┘
          ▼
┌────────────────────┐
│ OpenAiRuleParser (regras textuais → JSON estruturado)
└─────────┬──────────┘
          ▼
┌────────────────────┐
│ RuleEngine (validação determinística)
└─────────┬──────────┘
          ▼
┌────────────────────┐
│ CommentInserter (comentários OpenXML no .docx)
└────────────────────┘
```

## Estrutura

```
src/
  Core/            Models, Rules, Validation, Serialization
  Infrastructure/  OpenXml, OpenAI, Persistence
  Application/     Services (RuleGeneration, Validation)
  Cli/             Console app (System.CommandLine)
tests/
  Core.Tests/
  Infrastructure.Tests/
templates/         (.docx modelos – fornecidos pelo usuário)
rules/             padrões versionados (gerados)
output/            documentos validados
```

## Pré-requisitos

- .NET 8 SDK
- Chave de API OpenAI (variável `OPENAI_API_KEY` ou `OpenAI:ApiKey` em `appsettings`)

## Build & Test

```bash
dotnet build
dotnet test
```

## Uso

### Gerar um padrão (modelo + regras textuais → JSON)

```bash
export OPENAI_API_KEY=sk-...
dotnet run --project src/Cli -- generate-rules \
  --template templates/modelo.docx \
  --rules    templates/regras.txt \
  --client   "Cliente X"
```

Saída: `rules/Cliente X/v1.0.json`. Use `--major` para bump major.

### Validar um documento contra um padrão

```bash
dotnet run --project src/Cli -- validate \
  --doc   documento.docx \
  --rules "rules/Cliente X/v1.0.json" \
  --out   output/documento.commented.docx
```

Saída: `.docx` com comentários `[RULE-ID] mensagem` em cada inconsistência. Exit code `2` quando há violações `error`.

## Exemplo de JSON de regra

```json
{
  "cliente": "Cliente X",
  "versaoPadrao": "1.0",
  "styles": {
    "titulo1": { "font": "Calibri", "size": 14, "bold": true, "italic": null, "alignment": null }
  },
  "headers": [
    { "required": true, "contains": ["Nome Cliente", "Código Documento"] }
  ],
  "footers": [],
  "rules": [
    { "id": "R001", "type": "header", "severity": "error", "message": "Header obrigatório ausente." }
  ]
}
```

## Funcionalidades futuras

- API REST (ASP.NET Core) e frontend React/TS
- Diff entre versões de DOCX
- Correção automática
- Aprovação de regras via UI
- Add-in para Word, integração SharePoint, Azure OpenAI

## Notas técnicas

- **OpenXML como fonte da verdade** para validação estrutural (estilos, headers, footers, seções, track changes).
- **Interop não é usado** — sem dependência de Word instalado, mais estável e automatizável.
- **OpenAI** é usado **apenas para interpretar regras textuais** e produzir o JSON do padrão. Não valida documentos.

Referências Open XML SDK:
- [Comentários em documentos Word](https://learn.microsoft.com/en-us/office/open-xml/word/how-to-insert-a-comment-into-a-word-processing-document)
- [Recuperar comentários](https://learn.microsoft.com/en-us/office/open-xml/word/how-to-retrieve-comments-from-a-word-processing-document)
