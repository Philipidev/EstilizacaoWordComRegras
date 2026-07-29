# Guia de Uso — CLI do Word Compliance Validator

Como validar um documento `.docx` e receber de volta o arquivo **com anotações
(comentários do Word)** apontando as não-conformidades encontradas.

---

## 1. Pré-requisitos


| Requisito                                   | Para quê                                      | Obrigatório?                                                                            |
| ------------------------------------------- | --------------------------------------------- | --------------------------------------------------------------------------------------- |
| .NET 8 SDK                                  | rodar o CLI                                   | sim                                                                                     |
| `OPENAI_API_KEY`                            | regras semânticas (LLM)                       | não — sem ela o CLI roda só os checks determinísticos e avisa `[info] LLM desabilitado` |
| Arquivo `.docx` a validar                   | entrada                                       | sim                                                                                     |
| Arquivo de **profile do cliente** (`.json`) | regras parametrizadas                         | sim                                                                                     |
| Microsoft Word (ou LibreOffice)             | abrir o `.docx` revisado e ver os comentários | sim (para enxergar)                                                                     |


> Já há um profile pronto em `profiles/exemplo.json`. Para um cliente específico,
> faça uma cópia e ajuste os parâmetros — veja a seção **"Profile do cliente"**
> abaixo.

---



## 2. Chamada básica do CLI

A partir da raiz do repositório (`C:\Repositorios\Sysdam\EstilizacaoWordComRegras`):

```powershell
dotnet run --project src/Cli -- review `
  --doc      "C:\Repositorios\Sysdam\EstilizacaoWordComRegras\templates\RN-816-RL-67456-00.docx" `
  --profile  "profiles\exemplo.json" `
  --out      "C:\Repositorios\Sysdam\EstilizacaoWordComRegras\templates\out\RN-816-RL-67456-00-REVISADO.docx"
```

O que acontece:

1. O CLI lê o `.docx` indicado em `--doc`.
2. Carrega o profile do cliente.
3. Executa as regras automatizadas do checklist CL-001: **38 checks determinísticos**
  dedicados (39 quando o LLM está ativo), mais as regras avaliadas pelo **motor semântico
   genérico** conforme a allow-list `semantico.regrasHabilitadas` do profile
   (23 em `profiles/exemplo.json`).
4. Insere **comentários OpenXML** no `.docx` para cada violação encontrada.
5. Salva o resultado em `--out` (cria a pasta se não existir). Se o documento passar **sem
  nenhuma violação**, o arquivo de saída é uma cópia byte a byte do original.
6. Imprime no terminal o resumo: o total de itens do checklist e quantas regras deram
  `passou`, `falhou`, `pulado` e `erro`.

---



## 3. Flags disponíveis


| Flag            | Tipo    | Descrição                                                                                                        |
| --------------- | ------- | ---------------------------------------------------------------------------------------------------------------- |
| `--doc`         | caminho | **(obrigatório)** documento `.docx` a revisar                                                                    |
| `--profile`     | caminho | **(obrigatório)** profile JSON do cliente                                                                        |
| `--out`         | caminho | **(obrigatório)** onde salvar o `.docx` com comentários                                                          |
| `--checklist`   | caminho | opcional — caminho do `CL-001-CL00100.xlsx` (default: `templates/checklists/CL-001-CL00100.xlsx`)                |
| `--no-llm`      | flag    | desabilita checks semânticos via LLM (não precisa de `OPENAI_API_KEY`)                                           |
| `--verbose`     | flag    | mostra também itens `Passed` e `Skipped` no terminal (padrão: só `Failed` e `Error`)                             |
| `--only-ia-sim` | flag    | opcional (default `false`) — executa **apenas** as entradas marcadas `IA=Sim` na planilha (comportamento legado) |


> **Sobre a coluna** `IA?` **do CL-001.** Por padrão o CLI executa todo check registrado, mesmo
> quando a planilha marca a entrada como `IA=Não` — essa coluna é conservadora e virou apenas
> informativa. Use `--only-ia-sim` para restringir às 21 entradas `IA=Sim`, como fazia a
> versão anterior.

---



## 4. Exemplos práticos



### 4.1. Validação rápida sem usar LLM

```powershell
dotnet run --project src/Cli -- review `
  --doc "templates/RN-816-RL-67456-00.docx" `
  --profile "profiles/exemplo.json" `
  --out "output/revisado.docx" `
  --no-llm
```

Saída esperada (exemplo real):

```
[info] LLM desabilitado (sem OPENAI_API_KEY ou --no-llm). Checks semânticos serão pulados.

Cliente: MRN (exemplo)
Total de itens do checklist: 86
  passou:    32
  falhou:    0
  pulado:    54
  erro:      0


Saída: C:\...\output\revisado.docx
```

Abra `output/revisado.docx` no Word — se houver violações, os comentários
estarão visíveis no painel lateral de revisão.

### 4.2. Validação completa com LLM

Defina a chave da OpenAI **antes** de chamar o CLI:

```powershell
# Apenas para a sessão atual do PowerShell:
$env:OPENAI_API_KEY = "sk-proj-…"

dotnet run --project src/Cli -- review `
  --doc "templates/RN-816-RL-67456-00.docx" `
  --profile "profiles/exemplo.json" `
  --out "output/revisado.docx"
```

Alternativamente, coloque a chave em `src/Cli/appsettings.Development.json`:

```json
{
  "OpenAI": {
    "ApiKey": "sk-proj-…"
  }
}
```

> ⚠️ **Não coloque a chave em** `src/Cli/appsettings.json`**.** Esse arquivo é **rastreado pelo
> git** e existe apenas com `ApiKey: ""` como placeholder — qualquer chave ali vaza no
> primeiro `git commit -a`. O `appsettings.Development.json` está no `.gitignore` e é copiado
> para o diretório de saída pelo `.csproj`, então funciona em runtime sem entrar no
> versionamento.

A precedência é: `OPENAI_API_KEY` (variável de ambiente) → `OpenAI:ApiKey` do
`appsettings.Development.json` → `appsettings.json`.

### 4.3. Vendo o detalhe de tudo (`--verbose`)

```powershell
dotnet run --project src/Cli -- review `
  --doc "templates/RN-816-RL-67456-00.docx" `
  --profile "profiles/exemplo.json" `
  --out "output/revisado.docx" `
  --no-llm --verbose
```

Mostra todas as regras (`Passed`/`Failed`/`Skipped`) e, no caso de `Skipped`,
a justificativa (ex.: *"Revisão manual (IA=Não)"* ou *"Quadro Características
não localizado"*).

### 4.4. Caminhos com espaços

Sempre coloque caminhos com espaços entre aspas duplas:

```powershell
dotnet run --project src/Cli -- review `
  --doc "C:\Meus Documentos\projeto X\rel-01.docx" `
  --profile "profiles/exemplo.json" `
  --out "C:\Meus Documentos\projeto X\rel-01-revisado.docx"
```



### 4.5. Apenas listar o checklist (debug)

Útil para conferir quais entradas do `CL-001-CL00100.xlsx` o CLI consegue ler e como cada
uma está marcada na coluna `IA?` da planilha. Atenção: essa coluna é **apenas informativa** —
o CLI executa todo check registrado independentemente de `IA=Não` (use `--only-ia-sim` para
o comportamento legado, restrito às 21 entradas `IA=Sim`):

```powershell
dotnet run --project src/Cli -- dump-checklist
```

---



## 5. Como ler o arquivo revisado

Abra `output/revisado.docx` no Microsoft Word.

- Os **comentários** ficam visíveis no painel lateral de revisão (`Revisão → Mostrar Comentários`).
- Cada comentário começa com o identificador da regra entre colchetes,
ex.: `[PS-002:4.3.3 (letra c):Cliente] Iniciais idênticas (...)`.
- Quando **houve pelo menos uma violação**, o CLI reescreve o documento marcando
`UpdateFieldsOnOpen=true`; ao abrir no Word, os campos (sumário/TOC, números de página,
referências) são recalculados automaticamente. Se o documento passar sem nenhuma violação,
o arquivo de `--out` é cópia byte a byte do original — sem comentários e sem essa marcação.



### Onde ver a severidade

A severidade **não aparece no comentário do Word**. O texto inserido no `.docx` é apenas
`[<identificador da regra>] <mensagem>`.

Os prefixos de severidade aparecem **somente na listagem do terminal**, no formato
`→ [Error] mensagem…`:


| Símbolo no terminal | Significado                                                           |
| ------------------- | --------------------------------------------------------------------- |
| `[Error] …`         | violação **crítica** — deve ser corrigida (faz o exit code virar `2`) |
| `[Warning] …`       | inconsistência menor — revisar, mas não reprova o documento           |
| `[Info] …`          | observação contextual                                                 |


Ou seja: use o **terminal** para priorizar por severidade e o **Word** para localizar cada
apontamento no texto.

---



## 6. Exit code do processo

Útil em pipelines de CI:


| Exit | Quando                                                                                                  |
| ---- | ------------------------------------------------------------------------------------------------------- |
| `0`  | Sem violações de severidade `Error`. Documento "aprovado". Violações `Warning`/`Info` **não** reprovam. |
| `2`  | Pelo menos uma violação `Error` foi encontrada.                                                         |


> O contador `erro:` do resumo é outra coisa: conta regras que **lançaram exceção** e não
> chegaram a ser avaliadas. Ele **não** altera o exit code — um documento pode sair com `0`
> tendo regras que nem rodaram. Em CI, verifique também que `erro: 0`.

```powershell
# Exemplo em script:
dotnet run --project src/Cli -- review --doc "$doc" --profile "$profile" --out "$out" --no-llm
if ($LASTEXITCODE -eq 2) {
    Write-Host "Documento reprovado." -ForegroundColor Red
    exit 2
}
```

---



## 7. Profile do cliente

O profile (`.json`) configura como as regras se comportam para um cliente
específico. Estrutura mínima:

```json
{
  "cliente": "Nome do Cliente",
  "versao": "1.0",
  "parameters": {
    "codificacao.pdaRegex": "^[A-Z]{2}\\d{1,3}-PDA-\\d{2}-\\d{2}-\\d{3}-[A-Z]{2}$",
    "codificacao.clienteRegex": "^[A-Z0-9]{2,4}-[A-Z]{2,4}-\\d{2}-\\d{2}-\\d{3}-[A-Z]{2}$",
    "logomarcas.minimo": "2"
  }
}
```

> ⚠️ Esse mínimo **desliga o motor semântico**. As regras avaliadas por LLM só rodam quando o
> profile lista os `Ref` em `semantico.regrasHabilitadas` — a habilitação é allow-list
> explícita, nunca automática. Um profile sem esse parâmetro roda apenas os checks
> determinísticos, mesmo com a chave da OpenAI configurada, e as demais entradas aparecem
> silenciosamente como `Skipped`.

**Prefira copiar** `profiles/exemplo.json` para `profiles/<cliente>.json` e ajustar os
valores — ele já traz a allow-list semântica, o tiering de modelo e os parâmetros de
formatação do corpo.

> 👉 Para o guia **completo** de profiles (o que são, por que existem, lista
> exaustiva de parâmetros, tabela "regra ↔ parâmetro", como criar para um
> novo cliente), veja **[PROFILES.md](PROFILES.md)**.

> O CLI valida o profile no momento do load, **antes** de abrir o `.docx`, mas a validação
> cobre apenas quatro chaves: `codificacao.pdaRegex` e `codificacao.clienteRegex` (precisam
> ser regex válidos) e `logomarcas.minimo` e `folhaRosto.paragrafosIniciais` (inteiros ≥ 0).
> Os demais parâmetros numéricos (`corpo.tamanho`, `corpo.toleranciaPercentual`, …) **não**
> são validados: um valor inválido faz o parâmetro cair no default em silêncio.

---



## 8. Workflow recomendado

```
            ┌─────────────────┐
            │  documento.docx │
            └────────┬────────┘
                     │
                     ▼
    dotnet run --project src/Cli -- review
        --doc   documento.docx
        --profile profiles/<cliente>.json
        --out   revisado.docx
                     │
                     ▼
            ┌──────────────────┐
            │  revisado.docx   │  ← abra no Word
            │  + comentários   │
            └──────────────────┘
```

Sugestão de fluxo na equipe:

1. **Quem edita** o documento exporta como `.docx`.
2. **Quem revisa** roda o CLI passando o arquivo + profile do cliente.
3. **O Word abre** o arquivo `revisado.docx` e exibe os comentários no painel
  lateral; o time corrige cada apontamento.
4. Repetir até `falhou: 0` e `erro: 0`.

> O contador `pulado` **não** chega a zero. Itens sem check dedicado nem regra semântica
> habilitada — todo o PS-005, por exemplo, que trata de fluxo Meridian, e-mails ao GQ e
> autoridade do aprovador — permanecem como revisão manual por serem indecidíveis a partir
> do `.docx`. Na execução de referência: 32 `passou` e 54 `pulado` de 86.

---



## 9. Solução de problemas


| Sintoma                                                                                    | Causa provável                                                                                                              | Como resolver                                                                                                                                       |
| ------------------------------------------------------------------------------------------ | --------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------- |
| `[info] LLM desabilitado (sem OPENAI_API_KEY ou --no-llm)` sem você ter passado `--no-llm` | nenhuma chave encontrada — o CLI **não** dá erro, ele desliga o LLM em silêncio                                             | defina `$env:OPENAI_API_KEY` ou preencha `src/Cli/appsettings.Development.json`                                                                     |
| Regras semânticas voltam como `[Error]` com `note: Erro ao executar check: …`              | chave inválida/expirada ou sem crédito — a exceção é capturada por regra pelo engine                                        | confira a chave; rode com `--no-llm` para isolar se o problema é só o LLM                                                                           |
| Entradas do checklist ficam `Skipped` mesmo com o LLM ligado                               | o profile não lista os `Ref` em `semantico.regrasHabilitadas`                                                               | copie a allow-list de `profiles/exemplo.json` — ver seção 7                                                                                         |
| `Unhandled exception: System.IO.FileNotFoundException: Could not find file '…'`            | caminho de `--doc` ou `--profile` inexistente                                                                               | confira os caminhos (use aspas se houver espaços)                                                                                                   |
| `Unhandled exception: System.IO.FileNotFoundException: Checklist .xlsx não encontrado.`    | `--checklist` aponta para arquivo inexistente, ou você rodou fora da raiz do repo (o default é relativo ao diretório atual) | rode a partir da raiz ou passe `--checklist` com caminho absoluto                                                                                   |
| `Profile '<cliente>' contém parâmetros inválidos:` seguido de lista                        | regex inválido em `codificacao.*Regex` ou valor não-inteiro em `logomarcas.minimo` / `folhaRosto.paragrafosIniciais`        | corrija os parâmetros apontados na mensagem                                                                                                         |
| `Profile JSON inválido.`                                                                   | o arquivo contém literalmente `null`. **JSON malformado dá outro erro**: `JsonException` com a posição do caractere         | valide o JSON do profile                                                                                                                            |
| `Documento .docx sem MainDocumentPart`                                                     | `.docx` estruturalmente válido mas sem a parte principal                                                                    | arquivo corrompido — recupere de outra origem                                                                                                       |
| `System.IO.FileFormatException` ao abrir o documento                                       | não é um `.docx` real (ex.: `.doc` antigo renomeado, ou outro formato)                                                      | reabra no Word e salve como `.docx`                                                                                                                 |
| `Sheet 'WORD' não encontrada`                                                              | checklist customizado sem a sheet correta                                                                                   | use o `CL-001-CL00100.xlsx` original ou ajuste o seu                                                                                                |
| Comentários não aparecem no Word                                                           | painel oculto, **ou** o documento passou sem violação (nesse caso não há comentário algum e o arquivo é cópia do original)  | em **Revisão → Mostrar Comentários** habilite a exibição; confira se o resumo mostra `falhou: 0`                                                    |
| Numeração de páginas / sumário ficam em branco                                             | os campos não foram recalculados                                                                                            | feche e reabra o arquivo (é na **abertura** que o `UpdateFieldsOnOpen` age, não no salvamento), ou selecione tudo com **Ctrl+A** e pressione **F9** |


---



## 10. Resumo

```powershell
# Caminho mais curto, sem LLM, salvando o resultado:
dotnet run --project src/Cli -- review `
  --doc "MEU-DOC.docx" `
  --profile "profiles/exemplo.json" `
  --out "MEU-DOC-revisado.docx" `
  --no-llm
```

Depois é só **abrir** `MEU-DOC-revisado.docx` **no Word** e revisar os
comentários inseridos. Pronto.