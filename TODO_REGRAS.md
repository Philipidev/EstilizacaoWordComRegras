# TODO — Regras do Checklist CL-001

Este arquivo rastreia o estado de implementação de cada item do checklist (CL-001 / PS-002, PS-005, PS-018, PS-024).

Convenção de status:

- `[x]` — implementado com `IRuleCheck` dedicado e coberto por teste
- `[~]` — coberto pelo **motor semântico genérico** (`SemanticChecklistCheck`), habilitado via
  `semantico.regrasHabilitadas` no profile
- `[ ]` — pendente (sem check e sem allow-list)
- `[-]` — não automatizável a partir do `.docx`: depende de sistema externo (Meridian,
  e-mail, PL-011). Tratado como `Skipped`.

Formato do `Ref`: `PS:Item:Padrão`.

> **Sobre a coluna `IA?` da planilha.** Ela é conservadora e **não** é mais o teto do que a
> ferramenta executa. O `ChecklistEngine` procura primeiro um check registrado; a coluna só
> é respeitada com `--only-ia-sim`. Várias regras marcadas `IA=Não` foram automatizadas.

---

## PS-002 — Edição de Documentos Técnicos (rev. 31)

| Status | Ref | Título | IA? | Implementação |
|---|---|---|---|---|
| [x] | `PS-002:4.1:Cliente` | Logomarcas no cabeçalho | Sim | `LogomarcasNoHeaderCheck` |
| [~] | `PS-002:4.2:Cliente` | Ortografia e Gramática | Não | motor semântico (`gpt-5.6-sol`) |
| [x] | `PS-002:4.3.1:Pda` | Folha de Rosto (campos PdA) | Sim | `FolhaRostoCheck` |
| [x] | `PS-002:4.3.2:Cliente` | Folha de rosto × Características (LLM) | Sim | `FolhaRostoVsCaracteristicasCheck` |
| [x] | `PS-002:4.3.3 (letra a):Pda` | Continuidade de título e conteúdo | Sim | `ContinuidadeTituloConteudoCheck(Pda)` ⚠ heurístico |
| [x] | `PS-002:4.3.3 (letra a):Cliente` | Continuidade de título e conteúdo | Sim | `ContinuidadeTituloConteudoCheck(Cliente)` ⚠ heurístico |
| [-] | `PS-002:4.3.3 (letra b):Pda` | Iniciais conforme PL-011 | Não | manual — PL-011 é externo ao `.docx` |
| [-] | `PS-002:4.3.3 (letra b):Cliente` | Iniciais conforme PL-011 | Não | manual — PL-011 é externo ao `.docx` |
| [x] | `PS-002:4.3.3 (letra c):Pda` | Imparcialidade na verificação (PdA) | Sim | `IniciaisDistintasCheck(Pda)` |
| [x] | `PS-002:4.3.3 (letra c):Cliente` | Imparcialidade na verificação | Sim | `IniciaisDistintasCheck(Cliente)` |
| [x] | `PS-002:4.3.3 (letra d):Pda` | Consistência de iniciais entre registros | Sim | `ConsistenciaIniciaisCheck(Pda)` |
| [x] | `PS-002:4.3.3 (letra d):Cliente` | Consistência de iniciais entre registros | Sim | `ConsistenciaIniciaisCheck(Cliente)` |
| [~] | `PS-002:4.3.3 (letra e):Pda` | Preenchimento de campos obrigatórios | — | motor semântico |
| [x] | `PS-002:4.3.3 (letra e):Cliente` | Preenchimento de campos obrigatórios | Sim | `QuadroCaracteristicasPreenchidoCheck(item="4.3.3 (letra e)")` |
| [x] | `PS-002:4.3.3 (letra f):Pda` | Paginação atualizada | — | `PaginacaoAtualizadaCheck(Pda, "4.3.3 (letra f)")` |
| [x] | `PS-002:4.3.3 (letra f):Cliente` | Paginação atualizada | Sim | `PaginacaoAtualizadaCheck(item="4.3.3 (letra f)")` |
| [~] | `PS-002:4.3.3 (letra g):Pda` | Padronização de cabeçalhos | — | motor semântico |
| [x] | `PS-002:4.3.3 (letra g):Cliente` | Padronização de cabeçalhos | Sim | `CabecalhosPadronizadosCheck` |
| [~] | `PS-002:4.3.3 (letra h):Pda` | Correção de referências cruzadas | — | motor semântico |
| [x] | `PS-002:4.3.3 (letra h):Cliente` | Correção de referências cruzadas | Sim | `ReferenciasCruzadasCheck` |
| [~] | `PS-002:4.3.3 (letra i):Pda` | Coerência entre revisões | — | motor semântico |
| [x] | `PS-002:4.3.3 (letra i):Cliente` | Coerência entre revisões | Sim | `CoerenciaRevisoesCheck` |
| [x] | `PS-002:4.3.4.1:Pda` | Identificação da página do índice | — | `IndicePaginaCheck` (exige `folhaRosto.contratante`) |
| [~] | `PS-002:4.3.4.2:Pda` | Formatação do índice | — | motor semântico |
| [x] | `PS-002:4.3.4.3:Pda` | Títulos e subtítulos no índice | — | `IndiceConteudoCheck(item="4.3.4.3")` |
| [x] | `PS-002:4.3.4.4:Pda` | Relação de elementos gráficos no índice | — | `IndiceConteudoCheck(item="4.3.4.4")` |
| [~] | `PS-002:4.3.4.5:Pda` | Atualização do índice (PdA) | — | motor semântico |
| [x] | `PS-002:4.3.4.6:Pda` | Painel de navegação (PdA) | — | `PainelNavegacaoCheck(Pda)` |
| [x] | `PS-002:4.3.5.1:Cliente` | Atualização do índice (Cliente) | Sim | `IndiceAtualizadoCheck` |
| [x] | `PS-002:4.3.5.2:Cliente` | Painel de navegação (Cliente) | Não | `PainelNavegacaoCheck(Cliente)` |
| [x] | `PS-002:4.3.6.1:Pda` | Formatação do corpo do documento | — | `FormatacaoCorpoCheck(Pda)` |
| [x] | `PS-002:4.3.6.2:Cliente` | Formatação do corpo (Cliente) | Não | `FormatacaoCorpoCheck(Cliente)` |
| [x] | `PS-002:4.3.6.3:Pda` | Figuras, fotos, gráficos e tabelas | — | `ElementosGraficosCheck(Pda)` |
| [x] | `PS-002:4.3.6.4:Cliente` | Figuras, fotos, gráficos e tabelas (Cliente) | Não | `ElementosGraficosCheck(Cliente)` |
| [x] | `PS-002:4.3.6.5:Pda` | Numeração das páginas (PdA) | — | `NumeracaoPaginasCheck` |
| [x] | `PS-002:4.3.6.6:Cliente` | Numeração das páginas (Cliente) | Sim | `PaginacaoAtualizadaCheck(item="4.3.6.6")` |
| [~] | `PS-002:4.3.7:Pda` | Capa interna | — | motor semântico |
| [~] | `PS-002:4.3.7:Cliente` | Capa interna (Cliente) | Não | motor semântico |
| [x] | `PS-002:4.3.8:Pda` | Apêndice e anexo | — | `ApendiceAnexoCheck(Pda)` |
| [x] | `PS-002:4.3.8:Cliente` | Apêndice e anexo (Cliente) | Não | `ApendiceAnexoCheck(Cliente)` |
| [~] | `PS-002:4.3.9:Pda` | DCE como apêndice/anexo | — | motor semântico (exige saber se é DCE) |
| [~] | `PS-002:4.3.9:Cliente` | DCE como apêndice/anexo (Cliente) | Não | motor semântico |
| [x] | `PS-002:4.7:Pda` | Quadro Características preenchido (PdA) | — | `QuadroCaracteristicasPreenchidoCheck(Pda, "4.7")` |
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
| [~] | `PS-018:4.3:Pda` | Codificação técnica (PdA) | — | motor semântico (`gpt-5.6-terra`) |
| [x] | `PS-018:4.3:Cliente` | Codificação técnica (Cliente) | Sim | `CodificacaoTecnicaCheck` |
| [~] | `PS-018:4.3.2 (letra a):Pda` | Localização da codificação (PdA) | — | motor semântico |
| [x] | `PS-018:4.3.2 (letra a):Cliente` | Localização da codificação | Sim | `LocalizacaoCodificacaoCheck` |
| [~] | `PS-018:4.4:Pda/Cliente` | Citação de documentos | Não | motor semântico |
| [~] | `PS-018:4.7:Pda` | Codificação do cliente (PdA) | — | motor semântico (`gpt-5.6-terra`) |
| [x] | `PS-018:4.7:Cliente` | Codificação do cliente | Sim | `CodificacaoClienteCheck` |
| [~] | `PS-018:4.8:Pda` | Evolução do documento (PdA) | — | motor semântico (`gpt-5.6-terra`) |
| [x] | `PS-018:4.8:Cliente` | Evolução do documento | Sim | `EvolucaoDocumentoCheck` |
| [~] | `PS-018:4.9:Pda/Cliente` | Coerência entre revisões | Não | motor semântico |

## PS-024 — Emissão de Documentos Técnicos (rev. 25)

| Status | Ref | Título | IA? | Implementação |
|---|---|---|---|---|
| [x] | `PS-024:4.1.2:Pda/Cliente` | Etapa para comentários e aprovação (0A–0Z) | — | `TarjaEmissaoCheck(item="4.1.2")` ³ |
| [x] | `PS-024:4.1.3:Pda/Cliente` | Emissão final (00) e revisões subsequentes | — | `TarjaEmissaoCheck(item="4.1.3")` ¹ |
| [x] | `PS-024:4.1.4:Pda/Cliente` | Documentos traduzidos | — | `TarjaEmissaoCheck(item="4.1.4")` — registrado, mas retorna sempre `Skipped` ² |
| [~] | `PS-024:4.3:Pda/Cliente` | Cancelamento de documentos | — | motor semântico (`gpt-5.6-luna`) |

¹ A tarja "Não é Válido para Execução" só é exigida para estudo preliminar, projeto conceitual
e projeto básico — o tipo do projeto não é dedutível do `.docx`, então a cobrança depende de
`tarja.exigeNaoValidoExecucao=true` no profile. Sem isso, o resultado é `Skipped` com nota.

² O check existe para que a entrada apareça no relatório com uma justificativa explícita
("exige comparar com a versão em português, fora do `.docx`") em vez de cair no genérico
"Revisão manual (IA=Não)". Ele nunca valida nada — a comparação exige a versão em português,
que é externa ao arquivo.

³ Cobre os dois sentidos da regra. Além de exigir a tarja quando a revisão está em 0A–0Z,
acusa a tarja **remanescente** quando a revisão já saiu dessa etapa — que é o caso que falha
na prática: a tarja entra na 0A, fica num cabeçalho de seção e ninguém a remove ao emitir a
00. O `RN-816-RL-67456-00.docx` tem exatamente esse defeito (cabeçalho `first` da seção 6);
enquanto a revisão vigente era lida do cabeçalho da grade de folhas (`0B` em vez de `00`), o
check concluía que o documento ainda estava na etapa de comentários e **aprovava** a tarja
obsoleta. A revisão vigente agora vem de `QuadroCaracteristicas.HistoricoDeRevisoes`, que
localiza o histórico pelo cabeçalho com coluna de revisão *e* de data.

---

## Resumo de progresso

Contagem por **entrada do CL-001** (as tabelas acima têm linhas `:Pda/Cliente` que
representam duas entradas cada, então o número de linhas é menor que o de entradas):

- Total de itens no CL-001: **86**
- Com `IRuleCheck` dedicado `[x]`: **39** — sendo **38** sem LLM
  (`PS-002:4.3.2:Cliente` exige avaliação semântica e só é registrado com a chave configurada)
- Cobertos pelo motor semântico `[~]` (allow-list de `profiles/exemplo.json`): **23**
- Manuais `[-]` — dependem de sistema externo: **24**
  (22 entradas do PS-005 + as 2 de `4.3.3 (letra b)`, que exigem o PL-011)
- Soma: 39 + 23 + 24 = **86** ✅
- **Sem destino declarado: 0** — garantido por `CatalogCoverageTests`

> Para referência histórica: a versão anterior cobria **21** itens, limitada pela coluna
> `IA?` da planilha.

## Resultado contra documentos reais

Última execução com `profiles/exemplo.json`:

| Documento | LLM | passou | falhou | pulado | erro |
|---|---|---:|---:|---:|---:|
| `RN799RL6496600.docx` | não | 26 | 0 | 60 | 0 |
| `RN-816-RL-67456-00.docx` | não | 32 | 0 | 54 | 0 |
| `RN799RL6496600.docx` | `gpt-5.6-sol` | 31 | 7 | 48 | 0 |
| `RN-816-RL-67456-00.docx` | `gpt-5.6-sol` | 35 | 11 | 40 | 0 |

(Baseline anterior à expansão: 19 / 0 / 67 / 0 nos dois, sem LLM.)

Rodada completa com LLM: **~35 s** — os checks rodam concorrentemente e o pacote de
evidências é montado uma vez por documento.

### Cobertura de testes (93 testes)

- `RealDocumentCheckTests` — documentos reais conformes não produzem `Severity.Error`
  nem `Failed` (**precisão**).
- `NegativeCorpusTests` — documentos gerados com defeito deliberado via `DocxFixtureBuilder`
  **têm** de ser detectados (**recall**). Sem essa metade, um check que nunca acusa nada
  passaria despercebido.
- `CatalogCoverageTests` — toda entrada do CL-001 tem destino explícito; nenhum check órfão;
  nenhuma regra do PS-005 consome tokens.
- `SemanticChecklistCheckTests` — motor genérico exercitado com `FakeSemanticChecker`,
  sem rede.
- `DocxStructureExtractorTests` — regressão do bug de enum do OpenXML SDK 3.x.
- `CliExitCodeTests` — executa o CLI **como processo** e confere o código de saída (0 aprovado
  / 2 reprovado). É o único nível em que o exit code é observável: o bug em que
  `Environment.ExitCode` era sobrescrito pelo retorno de `InvokeAsync` passava despercebido
  por qualquer teste unitário.

Validado iterativamente: bugs corrigidos durante a calibragem:

- `QuadroCaracteristicasPreenchidoCheck` — passou a usar aliases por campo (Elaborador/Emissor sinônimos) e fallback de layout coluna para quadros com histórico de revisões.
- `CabecalhosPadronizadosCheck` — filtra tokens residuais de posicionamento de imagem flutuante (`right218440`); cabeçalhos só com imagem (sem texto) não contam como divergentes; imagens só reclamam quando >50% divergem do padrão.
- `CoerenciaRevisoesCheck` — só aceita revisões em contexto explícito (`Rev. XX`); evita pegar números soltos de tabelas. Sem contexto → Skipped.
- `FolhaRostoCheck` / `LocalizacaoCodificacaoCheck` — "folha de rosto" inclui parágrafos antes do primeiro heading + primeiras 2 tabelas + headers de seção.

## LLM (semantic checker) — uso atual

Modelo padrão **`gpt-5.6-sol`**, com tiering por regra no profile
(`semantico.modelo.<Ref>`): Luna para checagens simples de presença de texto, Terra para
codificação/revisão, Sol para julgamento (ortografia, coerência, capa interna).

| Regra | LLM | Função |
|---|---|---|
| `PS-002:4.3.2:Cliente` | obrigatório | Compatibilidade folha de rosto × quadro (avaliação semântica) |
| `PS-002:4.3.1:Pda` | opcional/fallback | Confirma presença dos 4 elementos da folha de rosto |
| `PS-002:4.3.3 (g):Cliente` | opcional/override | Pode reverter divergência de cabeçalho que seja só formatação |
| 23 refs `[~]` | motor genérico | `SemanticChecklistCheck`, instrução vinda da coluna Descrição |

### ⚠️ Limite de precisão do motor semântico

Nem todo achado do LLM é correto. Na calibragem, `PS-002:4.7:Pda` acusou "numeração de
páginas dentro do quadro Características" — o modelo leu a grade de controle de folhas (que
lista página por revisão) como se fosse paginação do quadro. **Achados semânticos são ponto
de partida para revisão humana, não veredito.** Sempre que uma regra tiver sinal
determinístico, ela deve migrar de `[~]` para `[x]` com um `IRuleCheck` dedicado.

Esse caso específico já migrou: `4.7:Pda` é determinístico e decide a ausência de numeração
pela seção que contém o quadro. O mesmo aconteceu com `4.3.3 (letra f):Pda`, onde o modelo
lia o valor cacheado do campo `PAGE` (`FL.: 7/99`, idêntico em toda parte de cabeçalho) como
paginação repetida entre seções.

Contrapartida da migração de `4.7:Pda`: `FieldsByLabel` casa o rótulo "Nome do Aprovador" com
valor não vazio e dá o campo por preenchido, então a assinatura em branco não sai mais por
esse item — ela continua saindo por `4.3.3 (letra e)`.

O contrato tem três estados — `conforme` / `nao_aplicavel` / `nao_conforme` — e o prompt
instrui a preferir `nao_aplicavel` na dúvida. Sem esse estado, os muitos itens condicionais
do CL-001 ("quando aplicável", "no caso de DCE…") virariam falso positivo em massa.

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
- ✅ ~~Cabeçalhos com tarjas oficiais do PdA não causam falsa divergência~~
  (concluído: parâmetro `cabecalho.tarjasIgnoradas` + filtro de resíduo curto).
- ✅ ~~Adicionar mais documento de teste real~~
  (concluído: `RN-816-RL-67456-00.docx` + `RealDocumentCheckTests` cobre ambos).
- ✅ ~~Destravar o teto da coluna `IA?` no `ChecklistEngine`~~
  (concluído: check registrado tem precedência; `--only-ia-sim` preserva o legado).
- ✅ ~~Corrigir leitura de enums do OpenXML SDK 3.x~~
  (concluído: `InnerText` em vez de `ToString()`; o filtro de header `first` em
  `CabecalhosPadronizadosCheck` nunca casava antes disso).
- ✅ ~~Corpus negativo para medir recall~~
  (concluído: `DocxFixtureBuilder` + `NegativeCorpusTests`).
- ✅ ~~Motor semântico genérico movido pela planilha~~
  (concluído: `SemanticChecklistCheck` + allow-list e tiering por regra no profile).

Pendentes:

1. Tornar os termos PT-BR remanescentes configuráveis (palavras em `MesAno` do
   `FolhaRostoCheck`) — desnecessário enquanto o escopo for só projetos brasileiros.
2. Suporte a perfil "MRN" oficial (separar do `exemplo.json`) com aliases finos
   da terminologia do cliente real — e preencher `folhaRosto.contratante`, hoje vazio,
   o que mantém `PS-002:4.3.4.1:Pda` em `Skipped`.
3. Migrar refs `[~]` com sinal determinístico para `[x]`. Candidatos mais claros:
   `PS-002:4.3.4.5:Pda` (atualização do índice — já há sinal OOXML em
   `IndiceAtualizadoCheck`) e as variantes `:Pda` de `4.3.3 (letra e/f/h)`, que replicam
   regras já implementadas no padrão Cliente.
4. `IEvidenceSelector` por regra: hoje `DefaultEvidenceSelector` manda o mesmo retrato para
   todas as regras. Recortes específicos reduziriam custo e aumentariam o sinal — por
   exemplo, `PS-002:4.2` (ortografia) só precisa do corpo, não do índice nem dos cabeçalhos.
5. Paginação real via renderização (LibreOffice/Word) para `PS-002:4.3.3 (letra a)` —
   OOXML não tem layout paginado, então "título na mesma página do conteúdo" segue
   heurístico (`w:br w:type="page"`).
