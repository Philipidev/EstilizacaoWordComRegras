# TODO — Regras do Checklist CL-001

Este arquivo rastreia o estado de implementação de cada item do checklist (CL-001 / PS-002, PS-005, PS-018, PS-024).

Convenção de status:

- `[x]` — implementado e coberto por teste
- `[ ]` — pendente (IA=Sim, ainda sem `IRuleCheck`)
- `[-]` — não automatizável (IA=Não ou `—`): tratado como `Skipped` pelo `ChecklistEngine`

Formato do `Ref`: `PS:Item:Padrão`.

---

## PS-002 — Edição de Documentos Técnicos (rev. 31)

| Status | Ref | Título | IA? | Implementação |
|---|---|---|---|---|
| [x] | `PS-002:4.1:Cliente` | Logomarcas no cabeçalho | Sim | `LogomarcasNoHeaderCheck` |
| [-] | `PS-002:4.2:Cliente` | Ortografia e Gramática | Não | manual |
| [x] | `PS-002:4.3.1:Pda` | Folha de Rosto (campos PdA) | Sim | `FolhaRostoCheck` |
| [x] | `PS-002:4.3.2:Cliente` | Folha de rosto × Características (LLM) | Sim | `FolhaRostoVsCaracteristicasCheck` |
| [x] | `PS-002:4.3.3 (letra a):Pda` | Continuidade de título e conteúdo | Sim | `ContinuidadeTituloConteudoCheck(Pda)` ⚠ heurístico |
| [x] | `PS-002:4.3.3 (letra a):Cliente` | Continuidade de título e conteúdo | Sim | `ContinuidadeTituloConteudoCheck(Cliente)` ⚠ heurístico |
| [-] | `PS-002:4.3.3 (letra b):Pda` | Iniciais conforme PL-011 | Não | manual |
| [-] | `PS-002:4.3.3 (letra b):Cliente` | Iniciais conforme PL-011 | Não | manual |
| [x] | `PS-002:4.3.3 (letra c):Pda` | Imparcialidade na verificação (PdA) | Sim | `IniciaisDistintasCheck(Pda)` |
| [x] | `PS-002:4.3.3 (letra c):Cliente` | Imparcialidade na verificação | Sim | `IniciaisDistintasCheck(Cliente)` |
| [x] | `PS-002:4.3.3 (letra d):Pda` | Consistência de iniciais entre registros | Sim | `ConsistenciaIniciaisCheck(Pda)` |
| [x] | `PS-002:4.3.3 (letra d):Cliente` | Consistência de iniciais entre registros | Sim | `ConsistenciaIniciaisCheck(Cliente)` |
| [-] | `PS-002:4.3.3 (letra e):Pda` | Preenchimento de campos obrigatórios | — | manual |
| [x] | `PS-002:4.3.3 (letra e):Cliente` | Preenchimento de campos obrigatórios | Sim | `QuadroCaracteristicasPreenchidoCheck(item="4.3.3 (letra e)")` |
| [-] | `PS-002:4.3.3 (letra f):Pda` | Paginação atualizada | — | manual |
| [x] | `PS-002:4.3.3 (letra f):Cliente` | Paginação atualizada | Sim | `PaginacaoAtualizadaCheck(item="4.3.3 (letra f)")` |
| [-] | `PS-002:4.3.3 (letra g):Pda` | Padronização de cabeçalhos | — | manual |
| [x] | `PS-002:4.3.3 (letra g):Cliente` | Padronização de cabeçalhos | Sim | `CabecalhosPadronizadosCheck` |
| [-] | `PS-002:4.3.3 (letra h):Pda` | Correção de referências cruzadas | — | manual |
| [x] | `PS-002:4.3.3 (letra h):Cliente` | Correção de referências cruzadas | Sim | `ReferenciasCruzadasCheck` |
| [-] | `PS-002:4.3.3 (letra i):Pda` | Coerência entre revisões | — | manual |
| [x] | `PS-002:4.3.3 (letra i):Cliente` | Coerência entre revisões | Sim | `CoerenciaRevisoesCheck` |
| [-] | `PS-002:4.3.4.1:Pda` | Identificação da página do índice | — | manual |
| [-] | `PS-002:4.3.4.2:Pda` | Formatação do índice | — | manual |
| [-] | `PS-002:4.3.4.3:Pda` | Títulos e subtítulos no índice | — | manual |
| [-] | `PS-002:4.3.4.4:Pda` | Relação de elementos gráficos no índice | — | manual |
| [-] | `PS-002:4.3.4.5:Pda` | Atualização do índice (PdA) | — | manual |
| [-] | `PS-002:4.3.4.6:Pda` | Painel de navegação (PdA) | — | manual |
| [x] | `PS-002:4.3.5.1:Cliente` | Atualização do índice (Cliente) | Sim | `IndiceAtualizadoCheck` |
| [-] | `PS-002:4.3.5.2:Cliente` | Painel de navegação (Cliente) | Não | manual |
| [-] | `PS-002:4.3.6.1:Pda` | Formatação do corpo do documento | — | manual |
| [-] | `PS-002:4.3.6.2:Cliente` | Formatação do corpo (Cliente) | Não | manual |
| [-] | `PS-002:4.3.6.3:Pda` | Figuras, fotos, gráficos e tabelas | — | manual |
| [-] | `PS-002:4.3.6.4:Cliente` | Figuras, fotos, gráficos e tabelas (Cliente) | Não | manual |
| [-] | `PS-002:4.3.6.5:Pda` | Numeração das páginas (PdA) | — | manual |
| [x] | `PS-002:4.3.6.6:Cliente` | Numeração das páginas (Cliente) | Sim | `PaginacaoAtualizadaCheck(item="4.3.6.6")` |
| [-] | `PS-002:4.3.7:Pda` | Capa interna | — | manual |
| [-] | `PS-002:4.3.7:Cliente` | Capa interna (Cliente) | Não | manual |
| [-] | `PS-002:4.3.8:Pda` | Apêndice e anexo | — | manual |
| [-] | `PS-002:4.3.8:Cliente` | Apêndice e anexo (Cliente) | Não | manual |
| [-] | `PS-002:4.3.9:Pda` | DCE como apêndice/anexo | — | manual |
| [-] | `PS-002:4.3.9:Cliente` | DCE como apêndice/anexo (Cliente) | Não | manual |
| [-] | `PS-002:4.7:Pda` | Quadro Características preenchido (PdA) | — | manual |
| [x] | `PS-002:4.7:Cliente` | Quadro Características preenchido (Cliente) | Sim | `QuadroCaracteristicasPreenchidoCheck` |

## PS-005 — Controle de Qualidade dos Documentos Técnicos (rev. 38)

| Status | Ref | Título | IA? |
|---|---|---|---|
| [-] | `PS-005:4.1.1:Pda/Cliente` | Cópia de verificação convencional | — |
| [-] | `PS-005:4.1.2:Pda/Cliente` | Cópia de verificação não convencional | — |
| [-] | `PS-005:4.2.3:Pda/Cliente` | Registro da elaboração | — |
| [-] | `PS-005:4.3.4:Pda/Cliente` | Registro da verificação técnica | — |
| [-] | `PS-005:4.4:Pda/Cliente` | Correções após verificação técnica | — |
| [-] | `PS-005:4.5:Pda/Cliente` | Conferência pelo verificador técnico | — |
| [-] | `PS-005:4.6.3:Pda/Cliente` | Registro da aprovação | — |
| [-] | `PS-005:4.7.1:Pda/Cliente` | Preparação no Meridian | — |
| [-] | `PS-005:4.7.2:Pda/Cliente` | Comunicação para verificação do GQ | — |
| [-] | `PS-005:4.9:Pda/Cliente` | Intervenção no fluxo do documento | — |
| [-] | `PS-005:4.10:Pda/Cliente` | Responsabilidades e autoridades | — |

> Todos os itens de PS-005 estão marcados `—` na coluna IA? do checklist → manuais.

## PS-018 — Codificação de Documentos (rev. 23)

| Status | Ref | Título | IA? | Implementação |
|---|---|---|---|---|
| [-] | `PS-018:4.3:Pda` | Codificação técnica (PdA) | — | manual |
| [x] | `PS-018:4.3:Cliente` | Codificação técnica (Cliente) | Sim | `CodificacaoTecnicaCheck` |
| [-] | `PS-018:4.3.2 (letra a):Pda` | Localização da codificação (PdA) | — | manual |
| [x] | `PS-018:4.3.2 (letra a):Cliente` | Localização da codificação | Sim | `LocalizacaoCodificacaoCheck` |
| [-] | `PS-018:4.4:Pda/Cliente` | Citação de documentos | Não | manual |
| [-] | `PS-018:4.7:Pda` | Codificação do cliente (PdA) | — | manual |
| [x] | `PS-018:4.7:Cliente` | Codificação do cliente | Sim | `CodificacaoClienteCheck` |
| [-] | `PS-018:4.8:Pda` | Evolução do documento (PdA) | — | manual |
| [x] | `PS-018:4.8:Cliente` | Evolução do documento | Sim | `EvolucaoDocumentoCheck` |
| [-] | `PS-018:4.9:Pda/Cliente` | Coerência entre revisões | Não | manual |

## PS-024 — Emissão de Documentos Técnicos (rev. 25)

| Status | Ref | Título | IA? |
|---|---|---|---|
| [-] | `PS-024:4.1.2:Pda/Cliente` | Etapa para comentários e aprovação (0A–0Z) | — |
| [-] | `PS-024:4.1.3:Pda/Cliente` | Emissão final (00) e revisões subsequentes | — |
| [-] | `PS-024:4.1.4:Pda/Cliente` | Documentos traduzidos | — |
| [-] | `PS-024:4.3:Pda/Cliente` | Cancelamento de documentos | — |

> Todos os itens de PS-024 estão marcados `—` na coluna IA? → manuais.

---

## Resumo de progresso

- Total de itens no CL-001: **86**
- Automatizáveis (IA=Sim): **21**
- Implementados: **21** (todos os IA=Sim)
- **Pendentes (IA=Sim)**: **0** ✅
- Manuais (IA=Não / —): **65**

## Resultado contra documento real `RN799RL6496600.docx`

Última execução com `profiles/exemplo.json`, sem LLM:

```
Total: 86 itens
  passou:    19   ← todas as regras estruturais aplicáveis verdes
  falhou:     0
  pulado:    67   ← manuais (IA=Não / —) + PS-002:4.3.3(i) sem "Rev." na folha de rosto
  erro:       0
```

Com LLM (`gpt-5.4-mini`) também disponível para validação semântica de:
- `PS-002:4.3.2:Cliente` — folha de rosto × quadro (obrigatório)
- `PS-002:4.3.1:Pda` — completude da folha de rosto (fallback opcional)
- `PS-002:4.3.3 (g):Cliente` — equivalência semântica de cabeçalhos (override opcional)

Validado iterativamente: bugs corrigidos durante a calibragem:

- `QuadroCaracteristicasPreenchidoCheck` — passou a usar aliases por campo (Elaborador/Emissor sinônimos) e fallback de layout coluna para quadros com histórico de revisões.
- `CabecalhosPadronizadosCheck` — filtra tokens residuais de posicionamento de imagem flutuante (`right218440`); cabeçalhos só com imagem (sem texto) não contam como divergentes; imagens só reclamam quando >50% divergem do padrão.
- `CoerenciaRevisoesCheck` — só aceita revisões em contexto explícito (`Rev. XX`); evita pegar números soltos de tabelas. Sem contexto → Skipped.
- `FolhaRostoCheck` / `LocalizacaoCodificacaoCheck` — "folha de rosto" inclui parágrafos antes do primeiro heading + primeiras 2 tabelas + headers de seção.

## LLM (semantic checker) — uso atual

| Regra | LLM | Função |
|---|---|---|
| `PS-002:4.3.2:Cliente` | obrigatório | Compatibilidade folha de rosto × quadro (avaliação semântica) |
| `PS-002:4.3.1:Pda` | opcional/fallback | Confirma presença dos 4 elementos da folha de rosto |

## Notas sobre heurísticas (já refinadas via OOXML)

- ✅ `PS-002:4.3.3 (letra a)` — detecta `w:br w:type="page"` em parágrafos com
  estilo de heading; identifica títulos órfãos com precisão real (Failed se
  detectado).
- ✅ `PS-002:4.3.3 (letra f)` e `4.3.6.6` — usa campos OOXML `PAGE`/`NUMPAGES`
  como sinal principal; texto extraído é só fallback.
- ✅ `PS-002:4.3.5.1` — detecta campo OOXML `TOC` no body + heurística antiga
  (SDT/style `toc`); pega marcas de erro do Word como Error.

## Próximos passos sugeridos

- ✅ ~~Distinguir `firstPage`/`even`/`default` headers via tipos OOXML~~
  (concluído: extractor resolve relId → kind via `HeaderReference/FooterReference`).
- ✅ ~~Refinar `PS-018:4.3.2 (a)` para aceitar codificações alternativas~~
  (concluído: stem do arquivo é aceito quando aparece literalmente em alguma célula
  do quadro Características).
- ✅ ~~LLM-fallback opcional em `CabecalhosPadronizadosCheck`~~
  (concluído: aceita `ISemanticChecker` que pode reverter Failed → Passed via LLM).

- ✅ ~~Configurar atualização automática do TOC/PAGE no `.docx` revisado~~
  (concluído: `CommentInserter` define `UpdateFieldsOnOpen=true` nos settings.xml).
- ✅ ~~Validação de parâmetros do profile no momento do load~~
  (concluído: regex válidos e ints positivos em `JsonClientProfileRepository`).
- ✅ ~~Marcas de erro de TOC/refs cruzadas configuráveis~~
  (concluído: parâmetros `referenciasCruzadas.marcasErro` e `indice.marcasErro`).

Pendentes:

1. Adicionar mais documentos de teste em `templates/` (variações de padrão Cliente).
2. Tornar os termos PT-BR remanescentes configuráveis (palavras em `MesAno` do
   `FolhaRostoCheck`, etc.) — provavelmente desnecessário enquanto o escopo for
   só projetos brasileiros.
