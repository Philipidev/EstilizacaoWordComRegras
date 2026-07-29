# Guia de Profiles do Cliente

Este guia explica **o que é um profile**, **para que ele serve** e **como
configurar um para um cliente específico** no Word Compliance Validator.

---

## 1. O que é um profile?

Um *profile* é um arquivo JSON que **calibra as regras** do checklist para
um cliente específico. Sem ele, as regras teriam de assumir defaults
genéricos — e o que vale para o cliente A (ex.: codificação com prefixo
`MRN-`, logomarca no canto superior esquerdo) não vale para o cliente B
(ex.: codificação `VALE-`, logomarca no canto direito).

O profile responde a perguntas como:

- Qual o padrão de **codificação técnica** desse cliente? (regex)
- Quantas **logomarcas** o cabeçalho dele exige?
- Como o cliente nomeia os campos do quadro Características? (ex.:
  "Elaborado por" ou "Emissor"?)
- Quais **tarjas oficiais** podem aparecer em revisões pré-aprovação?
- Quais **frases de erro** indicam problemas em sumário/referências?
- Qual a **fonte e o corpo** esperados no texto? Que margens?
- **Quais regras podem ser avaliadas por LLM**, e com qual modelo cada uma?

Tudo isso fica em um único `.json` por cliente, em `profiles/<cliente>.json`.

> O profile não é só calibragem: ele também **liga e desliga** funcionalidade. O motor
> semântico só roda para os `Ref` listados em `semantico.regrasHabilitadas` (seção 4.11) —
> um profile sem esse parâmetro executa apenas os checks determinísticos.

---

## 2. Por que precisamos disso?

| Sem profile | Com profile |
|---|---|
| Regras assumem nomes fixos ("Elaborado por", "Revisão") — falham para clientes que usam terminologia diferente | Cada cliente tem seus aliases configurados, sem precisar editar código |
| Codificação PdA está hard-coded em regex no Check | Cada cliente define seu próprio padrão de código |
| Tarjas oficiais (`EMISSÃO PARA COMENTÁRIOS DO CLIENTE`) podem causar falsos positivos | Profile lista as tarjas a ignorar |
| Mudar regra exige recompilar | Mudar profile = editar um JSON e rodar de novo |

Em resumo: **o profile separa "o que validar" (código) de "como aplicar para
esse cliente" (configuração)**.

---

## 3. Estrutura mínima de um profile

```json
{
  "cliente": "Nome do Cliente",
  "versao": "1.0",
  "parameters": {
    "codificacao.pdaRegex": "^[A-Z]{2}\\d{1,3}-PDA-\\d{2}-\\d{2}-\\d{3}-[A-Z]{2}$"
  }
}
```

Três campos no topo:

| Campo        | Tipo                   | Obrigatório | Para quê |
|---|---|---|---|
| `cliente`    | string                 | sim | Nome humanizado do cliente (aparece nos relatórios) |
| `versao`     | string                 | sim | Versão do profile — incrementar quando mudar parâmetros relevantes |
| `parameters` | objeto `chave: string` | sim | Mapa de parâmetros (lista completa abaixo) |

> **Todo valor em `parameters` é string**, mesmo quando representa número
> (`"2"`), booleano (`"true"`) ou regex (`"^abc$"`). O CLI faz a conversão na hora de usar.

> ⚠️ Esse mínimo roda **apenas os checks determinísticos**. Para as regras avaliadas por LLM
> é preciso `semantico.regrasHabilitadas` (seção 4.11) — na prática, **copie
> `profiles/exemplo.json`** em vez de partir deste esqueleto.

---

## 4. Parâmetros disponíveis

### 4.1. Logomarcas

| Chave | Default | Onde é usado |
|---|---|---|
| `logomarcas.minimo` | `2` | `LogomarcasNoHeaderCheck` (PS-002:4.1). Mínimo de imagens no cabeçalho (PdA + Cliente). |

```json
"logomarcas.minimo": "2"
```

### 4.2. Quadro "Características do Documento"

| Chave | Default | Para quê |
|---|---|---|
| `quadroCaracteristicas.aliases` | `Características do Documento\|Quadro de Características\|Características` | Como o quadro é chamado no documento (separadores por `\|`). |
| `quadro.camposObrigatorios` | `Codificação\|Título\|Revisão\|Data\|Elaborado por\|Verificado por\|Aprovado por` | Lista de campos que devem estar preenchidos. |
| `quadro.aliases.<campo>` | (defaults internos) | Aliases por campo individual (caso o cliente use nomes diferentes). |

Exemplo: se o cliente usa "Emissor" em vez de "Elaborado por":

```json
"quadro.aliases.Elaborado por": "Elaborado por|Elaborador|Emissor"
```

Quando o profile não define um alias por campo, o validator usa defaults
embutidos:

| Campo (no `camposObrigatorios`) | Aliases default |
|---|---|
| `Codificação`    | `Codificação\|Código\|Codificação PdA\|Documento` |
| `Título`         | `Título\|Titulo\|Assunto` |
| `Revisão`        | `Revisão\|Rev.\|Rev` |
| `Data`           | `Data\|Data de emissão\|Emissão` |
| `Elaborado por`  | `Elaborado por\|Elaborador\|Emissor` |
| `Verificado por` | `Verificado por\|Verificador\|Verificador Técnico` |
| `Aprovado por`   | `Aprovado por\|Aprovador` |

### 4.3. Iniciais (elaborador / verificador / aprovador)

| Chave | Default | Para quê |
|---|---|---|
| `iniciais.rotuloElaborador`  | `Elaborado por\|Emissor\|Elaborador` | Rótulos no quadro para extrair iniciais do elaborador (PS-002:4.3.3 c/d). |
| `iniciais.rotuloVerificador` | `Verificado por\|Verificador\|Verificador Técnico` | Idem para verificador. |
| `iniciais.rotuloAprovador`   | `Aprovado por\|Aprovador` | Idem para aprovador. |

Usado pelas regras de **imparcialidade** (`PS-002:4.3.3 c`) e
**consistência de iniciais** (`PS-002:4.3.3 d`).

### 4.4. Codificação técnica

| Chave | Default | Para quê |
|---|---|---|
| `codificacao.pdaRegex`       | (sem default — obrigatório se quiser validar codificação PdA) | Regex que define o **padrão PdA**. |
| `codificacao.clienteRegex`   | opcional | Regex que define o **padrão Cliente**. |
| `codificacao.aliasesRotulo`  | `Codificação PdA\|Codificação\|Código do Documento\|Documento\|Código` | Rótulos do quadro que **contêm** o código. |
| `revisao.aliasesRotulo`      | `Revisão\|Rev.\|Rev` | Rótulos do campo de revisão. |

> **Importante:** o regex precisa ser válido — no load do profile o CLI tenta compilá-lo e,
> se falhar, aborta com `Profile '<cliente>' contém parâmetros inválidos:` seguido da chave e
> do erro, **antes** de abrir o `.docx`.
>
> Essa validação cobre apenas quatro chaves: `codificacao.pdaRegex` e
> `codificacao.clienteRegex` (regex) e `logomarcas.minimo` e `folhaRosto.paragrafosIniciais`
> (inteiros ≥ 0). Os demais parâmetros numéricos (`corpo.tamanho`,
> `corpo.toleranciaPercentual`, …) **não** são validados: um valor inválido faz o parâmetro
> cair no default silenciosamente.

Exemplo de regex PdA:

```
^[A-Z]{2}\d{1,3}-PDA-\d{2}-\d{2}-\d{3}-[A-Z]{2}$
```

Decodificando: 2 letras + 1-3 dígitos + `-PDA-` + grupos numéricos +
2 letras tipo doc. (No JSON, lembre de escapar: `\\d` em vez de `\d`.)

### 4.5. Folha de rosto

| Chave | Default | Para quê |
|---|---|---|
| `folhaRosto.paragrafosIniciais` | `60` | Quantos parágrafos do início do documento considerar como "folha de rosto" antes do primeiro heading. |
| `folhaRosto.contratante` | (vazio) | Nome da empresa contratante a procurar literalmente na folha. |
| `folhaRosto.titulo` | (vazio) | Título esperado do documento. |

Quando `folhaRosto.contratante` ou `folhaRosto.titulo` estão vazios, a regra
pula essa verificação específica. Útil quando você quer manter o profile
genérico e só validar **codificação** + **data**.

### 4.6. Cabeçalhos & tarjas oficiais

| Chave | Default | Para quê |
|---|---|---|
| `cabecalho.tarjasIgnoradas` | `EMISSÃO PARA COMENTÁRIOS DO CLIENTE\|NÃO É VÁLIDO PARA EXECUÇÃO\|DOCUMENTO CANCELADO\|EMISSÃO PARA COMENTÁRIOS` | Frases a remover dos cabeçalhos antes de comparar (revisões 0A-0Z/cancelamento podem ter tarjas que não devem causar "divergência"). |

Quando o documento está em revisão `0A-0Z` (segundo o `PS-024:4.1.2`) ele
pode ter a tarja "EMISSÃO PARA COMENTÁRIOS DO CLIENTE" em todos os
cabeçalhos. O comparador de padronização deve ignorá-la — esse parâmetro
controla isso.

### 4.7. Marcas de erro do Word

| Chave | Default | Para quê |
|---|---|---|
| `referenciasCruzadas.marcasErro` | `Erro! Indicador não definido\|Erro! Fonte de referência\|Error! Reference source not found\|Error! Bookmark not defined` | Frases que o Word imprime quando uma referência cruzada está quebrada. |
| `indice.marcasErro` | `Erro! Indicador não definido\|Erro! Nenhuma entrada\|Error! No table of contents entries found` | Frases que o Word imprime quando o sumário não foi atualizado. |

Útil quando o documento original está em **outra língua** (inglês, espanhol)
ou em uma versão do Word com terminologia diferente.

### 4.8. Formatação do corpo do documento

| Chave | Default | Para quê |
|---|---|---|
| `corpo.fonte` | `Times New Roman` | Fonte esperada nos parágrafos de corpo (`FormatacaoCorpoCheck`, PS-002:4.3.6.1/4.3.6.2). |
| `corpo.tamanho` | `12` | Corpo esperado, em pontos. |
| `corpo.toleranciaPercentual` | `10` | % de parágrafos divergentes tolerado antes de acusar violação. |
| `corpo.alinhamento` | (sem default — **opt-in**) | Alinhamento esperado (`both`, `left`, `center`, `right`). Sem valor, o alinhamento **não é verificado**. |
| `corpo.margemSuperior` | (sem default — **opt-in**) | Margem superior esperada, em **twips**. |
| `corpo.margemInferior` | (sem default — **opt-in**) | Margem inferior, em twips. |
| `corpo.margemEsquerda` | (sem default — **opt-in**) | Margem esquerda, em twips. |
| `corpo.margemDireita` | (sem default — **opt-in**) | Margem direita, em twips. |

A avaliação é **proporcional, não absoluta**: documentos técnicos legitimamente têm parágrafos
fora do padrão (legendas, notas, texto em figuras). Parágrafos cuja fonte ou tamanho não pôde
ser resolvido são **ignorados**, não contados como divergentes.

> **Twips** = 1/20 de ponto = 1/1440 de polegada. Margem de 2,5 cm ≈ `1417`. A tolerância
> aplicada é de 20 twips (1 pt), para absorver arredondamentos do Word.

```json
"corpo.fonte": "Times New Roman",
"corpo.tamanho": "12",
"corpo.toleranciaPercentual": "10"
```

### 4.9. Elementos gráficos, numeração e painel de navegação

| Chave | Default | Para quê |
|---|---|---|
| `elementosGraficos.toleranciaPercentual` | `20` | % de figuras não centralizadas ou de legendas mal posicionadas tolerado (`ElementosGraficosCheck`, PS-002:4.3.6.3/4.3.6.4). |
| `numeracao.exigirDireita` | `false` | `true` exige que o cabeçalho com o campo `PAGE` tenha parágrafo alinhado à direita (`NumeracaoPaginasCheck`, PS-002:4.3.6.5). |
| `painelNavegacao.comprimentoMaximoTitulo` | `200` | Acima desse número de caracteres, um "título" é reportado como provável parágrafo de corpo marcado indevidamente (`PainelNavegacaoCheck`, PS-002:4.3.4.6/4.3.5.2). |

> `numeracao.exigirDireita` fica `false` por padrão porque em vários clientes o número de
> página vive dentro de uma célula da tarja do cabeçalho, cujo alinhamento não corresponde ao
> da página — ligar sem conferir o layout gera falso positivo.

### 4.10. Tarjas de emissão (PS-024)

| Chave | Default | Para quê |
|---|---|---|
| `tarja.comentariosCliente` | `Emissão para Comentários do Cliente\|Emissao para Comentarios do Cliente\|Para Comentários do Cliente` | Textos aceitos como tarja para revisões `0A`–`0Z` (`TarjaEmissaoCheck`, PS-024:4.1.2). |
| `tarja.naoValidoExecucao` | `Não é Válido para Execução\|Nao e Valido para Execucao\|Não Válido para Execução` | Textos aceitos como tarja de emissão final (PS-024:4.1.3). |
| `tarja.exigeNaoValidoExecucao` | `false` | `true` cobra a tarja em revisões `00`+ . |

A tarja é procurada em **todo o texto** — corpo, tabelas e cabeçalhos — porque nos documentos
de referência ela aparece na descrição da revisão dentro da folha índice, não como carimbo
isolado.

> `tarja.exigeNaoValidoExecucao` fica `false` porque a exigência vale apenas para estudo
> preliminar, projeto conceitual e projeto básico — o tipo do projeto não é dedutível do
> `.docx`. Ligue no profile do cliente quando souber que se aplica; caso contrário a regra
> retorna `Skipped` com nota explicando.

### 4.11. Motor semântico (LLM)

| Chave | Default | Para quê |
|---|---|---|
| `semantico.regrasHabilitadas` | **(sem default — vazio = motor desligado)** | Lista de `Ref` separada por `\|` que o motor semântico genérico pode assumir. Aceita `*` para todas as entradas sem check dedicado. |
| `semantico.modelo.default` | (usa `OpenAI:Model`, hoje `gpt-5.6-sol`) | Modelo padrão das regras semânticas. |
| `semantico.modelo.<Ref>` | (usa o default) | Override de modelo por regra, ex.: `"semantico.modelo.PS-018:4.3:Pda": "gpt-5.6-terra"`. |

> ⚠️ **Sem `semantico.regrasHabilitadas`, nenhuma regra semântica roda** — mesmo com a chave
> da OpenAI configurada e sem `--no-llm`. A habilitação é allow-list explícita, nunca
> automática, e as entradas não habilitadas aparecem apenas como `Skipped` no relatório.
> É o modo de falha mais fácil de não perceber ao montar um profile do zero.

A allow-list é explícita de propósito: o PS-005 inteiro (fluxo Meridian, e-mails ao GQ,
autoridade do aprovador) é indecidível a partir do `.docx`, e habilitá-lo gastaria tokens
para produzir "não aplicável" em todos os itens.

**Tiering de modelo.** A família GPT-5.6 tem três níveis — Sol (mais forte), Terra
(equilibrado) e Luna (rápido/barato). Rodar Sol em 20+ regras por documento é caro, e várias
delas são checagem simples de presença de texto:

```json
"semantico.regrasHabilitadas": "PS-002:4.2:Cliente|PS-018:4.4:Pda|PS-024:4.3:Cliente",
"semantico.modelo.default": "gpt-5.6-sol",
"semantico.modelo.PS-024:4.3:Cliente": "gpt-5.6-luna"
```

Uma entrada só é assumida pelo motor se **também** tiver a coluna `Descrição` preenchida no
CL-001 — é ela que vira a instrução de verificação enviada ao modelo.

> Os achados do motor semântico são **ponto de partida para revisão humana, não veredito**.
> Sempre que uma regra tiver sinal determinístico no OOXML, prefira escrever um `IRuleCheck`
> dedicado: é mais barato, mais rápido e auditável.

---

## 5. Tabela "regra ↔ parâmetro"

Mapa rápido de qual regra usa qual parâmetro:

| Regra | Parâmetros consumidos |
|---|---|
| `PS-002:4.1:Cliente` (Logomarcas) | `logomarcas.minimo` |
| `PS-002:4.3.1:Pda` (Folha de Rosto) | `folhaRosto.paragrafosIniciais`, `folhaRosto.contratante`, `folhaRosto.titulo`, `codificacao.pdaRegex` |
| `PS-002:4.3.2:Cliente` (LLM) | `quadroCaracteristicas.aliases` |
| `PS-002:4.3.3 (c)` (Imparcialidade) | `quadroCaracteristicas.aliases`, `iniciais.rotuloElaborador`, `iniciais.rotuloVerificador` |
| `PS-002:4.3.3 (d)` (Consistência iniciais) | `quadroCaracteristicas.aliases`, `iniciais.rotulo*` |
| `PS-002:4.3.3 (e)` (Campos obrigatórios) | `quadro.camposObrigatorios`, `quadro.aliases.<campo>` |
| `PS-002:4.3.3 (f)` / `4.3.6.6` (Paginação) | — (usa apenas campos OOXML) |
| `PS-002:4.3.3 (g)` (Cabeçalhos) | `cabecalho.tarjasIgnoradas` |
| `PS-002:4.3.3 (h)` (Refs cruzadas) | `referenciasCruzadas.marcasErro` |
| `PS-002:4.3.3 (i)` (Coerência revisões) | `quadroCaracteristicas.aliases`, `revisao.aliasesRotulo` |
| `PS-002:4.3.5.1` (Índice) | `indice.marcasErro` |
| `PS-002:4.7` (Quadro preenchido) | `quadro.camposObrigatorios`, `quadro.aliases.<campo>` |
| `PS-018:4.3` (Codificação técnica) | `codificacao.pdaRegex`, `codificacao.clienteRegex`, `codificacao.aliasesRotulo`, `quadroCaracteristicas.aliases` |
| `PS-018:4.3.2 (a)` (Localização do código) | `codificacao.pdaRegex`, `quadroCaracteristicas.aliases` |
| `PS-018:4.7` (Codificação Cliente) | `codificacao.clienteRegex`, `quadroCaracteristicas.aliases` |
| `PS-018:4.8` (Evolução do documento) | `quadroCaracteristicas.aliases`, `revisao.aliasesRotulo` |
| `PS-002:4.3.4.1` (Página do índice) | `folhaRosto.contratante` — **sem ele a regra fica `Skipped`** |
| `PS-002:4.3.4.3` / `4.3.4.4` (Índice: títulos e elementos gráficos) | — (usa outline levels e campos `PAGEREF`) |
| `PS-002:4.3.4.6` / `4.3.5.2` (Painel de navegação) | `painelNavegacao.comprimentoMaximoTitulo` |
| `PS-002:4.3.6.1` / `4.3.6.2` (Formatação do corpo) | `corpo.fonte`, `corpo.tamanho`, `corpo.toleranciaPercentual`, `corpo.alinhamento`, `corpo.margem*` |
| `PS-002:4.3.6.3` / `4.3.6.4` (Elementos gráficos) | `elementosGraficos.toleranciaPercentual` |
| `PS-002:4.3.6.5` (Numeração das páginas) | `numeracao.exigirDireita` |
| `PS-002:4.3.8` (Apêndice e anexo) | `quadroCaracteristicas.aliases` |
| `PS-024:4.1.2` / `4.1.3` / `4.1.4` (Tarjas de emissão) | `tarja.comentariosCliente`, `tarja.naoValidoExecucao`, `tarja.exigeNaoValidoExecucao`, `quadroCaracteristicas.aliases`, `revisao.aliasesRotulo` |
| Regras do motor semântico (23 em `exemplo.json`) | `semantico.regrasHabilitadas`, `semantico.modelo.default`, `semantico.modelo.<Ref>` |

---

## 6. Exemplo completo de profile

A fonte da verdade é `profiles/exemplo.json` — este bloco é uma cópia e pode divergir; em
caso de dúvida, leia o arquivo.

```json
{
  "cliente": "MRN (exemplo)",
  "versao": "1.1",
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
    "folhaRosto.titulo": "",

    "corpo.fonte": "Times New Roman",
    "corpo.tamanho": "12",
    "corpo.toleranciaPercentual": "10",

    "elementosGraficos.toleranciaPercentual": "20",
    "numeracao.exigirDireita": "false",
    "painelNavegacao.comprimentoMaximoTitulo": "200",

    "tarja.comentariosCliente": "Emissão para Comentários do Cliente|Emissao para Comentarios do Cliente|Para Comentários do Cliente",
    "tarja.naoValidoExecucao": "Não é Válido para Execução|Nao e Valido para Execucao|Não Válido para Execução",
    "tarja.exigeNaoValidoExecucao": "false",

    "semantico.regrasHabilitadas": "PS-002:4.2:Cliente|PS-002:4.3.4.2:Pda|… (23 refs no total)",

    "semantico.modelo.default": "gpt-5.6-sol",
    "semantico.modelo.PS-002:4.3.4.2:Pda": "gpt-5.6-luna",
    "semantico.modelo.PS-018:4.3:Pda": "gpt-5.6-terra"
  }
}
```

> `folhaRosto.contratante` está vazio no exemplo — por isso `PS-002:4.3.4.1:Pda` sai como
> `Skipped`. Preencha no profile do cliente real para ligar essa regra.

---

## 7. Criando um profile para um novo cliente

Workflow recomendado:

1. **Copie** `profiles/exemplo.json` para `profiles/<nome-do-cliente>.json`.
2. **Atualize** `cliente` e `versao`.
3. **Ajuste o regex de codificação** (`codificacao.pdaRegex` /
   `clienteRegex`) para refletir o padrão do cliente.
4. **Rode** o CLI passando o novo profile contra um documento conhecido
   desse cliente (com `--verbose`):

   ```powershell
   dotnet run --project src/Cli -- review `
     --doc "exemplo-cliente.docx" `
     --profile "profiles/novo-cliente.json" `
     --out "saida.docx" `
     --no-llm --verbose
   ```

5. **Olhe quais regras ficaram `Failed` ou `Skipped`** com nota explicativa
   (ex.: *"Quadro 'Características' não localizado"* → ajuste
   `quadroCaracteristicas.aliases`).
6. **Itere**: ajuste o profile e rode de novo até chegar a `falhou: 0` e `erro: 0`.

> O contador `pulado` **não** chega a zero: itens sem check dedicado nem regra semântica
> habilitada — todo o PS-005, por exemplo — permanecem como revisão manual. Na execução de
> referência com `exemplo.json`: 32 `passou` e 54 `pulado` de 86.

7. **Revise a allow-list semântica.** Se você começou de um profile enxuto em vez de copiar o
   `exemplo.json`, `semantico.regrasHabilitadas` provavelmente está ausente e ~23 regras nunca
   rodaram — elas aparecem apenas como `Skipped`, sem erro. Ver seção 4.11.

### Quando precisar de campo novo no quadro

Exemplo: o cliente tem um campo `Status` que precisa ser obrigatório.

1. Acrescente em `quadro.camposObrigatorios`:

   ```json
   "quadro.camposObrigatorios": "Codificação|Título|Revisão|Status"
   ```

2. (Opcional) Defina aliases caso o nome varie:

   ```json
   "quadro.aliases.Status": "Status|Situação|Condição"
   ```

Pronto — sem mexer em código.

---

## 8. Validação automática do profile

O `JsonClientProfileRepository` valida o profile **no momento do load**:

- **Regex inválido** em `codificacao.pdaRegex` ou `codificacao.clienteRegex`
  → exceção imediata.
- **Valor não-numérico** em `logomarcas.minimo` ou
  `folhaRosto.paragrafosIniciais` → exceção imediata.

Exemplo de mensagem ao rodar com profile quebrado:

```
Profile 'MRN (exemplo)' contém parâmetros inválidos:
  - 'codificacao.pdaRegex': regex inválido — Invalid pattern '[' at offset 1.
```

Falhar early aqui é melhor do que falhar 86 regras silenciosamente.

---

## 9. Boas práticas

| Faça | Não faça |
|---|---|
| Mantenha um profile por cliente (`profiles/mrn.json`, `profiles/vale.json`, ...) | Coloque tudo num único profile gigante |
| Incremente `versao` quando alterar parâmetros sensíveis (regex de codificação, lista de campos obrigatórios) | Edite o profile sem versionar — fica difícil rastrear regressões |
| Use aliases generosos (`Verificado por\|Verificador\|Verificador Técnico`) para tolerar variações textuais | Force uma terminologia rígida que falhe em pequenas diferenças |
| Comite o profile no Git **junto com o documento de teste** do cliente | Comite chaves/segredos (use `.gitignore`) |
| Teste o profile com `--verbose` contra documentos reais antes de adotar em produção | Acredite que defaults internos sempre funcionam — todo cliente é diferente |

---

## 10. Localização dos arquivos

```
profiles/
  exemplo.json       ← profile de exemplo (MRN — comitado)
  mrn.json           ← (você cria) profile real do cliente MRN
  vale.json          ← (você cria) profile real do cliente Vale
  ...
```

Para usar:

```powershell
dotnet run --project src/Cli -- review `
  --doc "documento.docx" `
  --profile "profiles\mrn.json" `   # ← aqui você escolhe o cliente
  --out "revisado.docx"
```

---

## Resumo em uma linha

O **profile é a "ficha de identidade"** de um cliente: ele diz ao validator *como aquele
cliente nomeia as coisas e qual o padrão dele*, para que as mesmas regras automatizadas
funcionem corretamente sem precisar alterar código — e, via `semantico.regrasHabilitadas`,
também decide **quais** regras entram em jogo.
