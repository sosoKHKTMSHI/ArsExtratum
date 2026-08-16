# Gate corretivo — Laboratory Curve Projection v1

## Status

SUCCESS. Implementação, baseline do `Teste01.pdf`, suíte integral, build Release e inspeção visual da janela WPF renderizada com o lote real concluídos. O controle interativo do Windows apresentou `E_NOINTERFACE`, contornado sem intervenção do usuário pela renderização direta da mesma janela de produção.

## Objetivo

Consolidar a projeção de curvas laboratoriais por meio de uma interface temporal inequívoca, apresentação numérica compacta, datas globalmente coerentes e deltas reconciliáveis com os valores exibidos, sem modificar a extração semântica, os episódios, a equação CKD-EPI ou a projeção clínica convencional.

## Decisões congeladas

### Filtros temporais

Os filtros serão três opções mutuamente exclusivas, implementadas como `RadioButton`:

```text
( ) Todos
( ) Intervalo personalizado: [data inicial] — [data final]
( ) Nos últimos [  ] dias
```

- Somente os campos pertencentes ao filtro selecionado ficam habilitados.
- `Intervalo personalizado` inclui as duas datas-limite.
- `Últimos X dias` usa a data corrente do computador e inclui o dia atual.
- Quantidade de dias não positiva e intervalo invertido impedem a geração e produzem mensagem explícita.

### Apresentação

O delta será retirado do bloco visual de período e colocado em seção própria:

```text
Apresentação
[ ] Exibir variação (delta) entre resultados
```

A seleção múltipla de exames, o texto copiável e o botão de geração permanecem.

### Datas

- Quando o período efetivo estiver contido em um único ano, usar `DD/MM`.
- Quando o período efetivo atravessar anos, usar `DD/MM/AA` em todas as séries da projeção, mesmo que uma série específica possua pontos em apenas um ano.
- Para `Todos`, o período efetivo é delimitado pelo menor e pelo maior timestamp entre os pontos projetados selecionados.
- Para `Intervalo personalizado`, usar as datas informadas pelo operador.
- Para `Últimos X dias`, usar o intervalo calculado entre a data inicial inclusiva e a data corrente.
- Havendo mais de um ponto da mesma série no mesmo dia, acrescentar `HH:mm` aos pontos daquele dia.

### Valores

A compactação é exclusivamente editorial. `RawValue`, `NumericValue` e proveniência permanecem inalterados no modelo semântico.

- Contagens de plaquetas e leucócitos: inteiro com separador de milhar.
- Escalares documentais: preservar até duas casas significativas conhecidas no corpus e remover zeros decimais finais.
- Exemplos: `13,00 → 13`, `13,40 → 13,4`, `29,10 → 29,1`, `0,80 → 0,8`, `1,01 → 1,01`.
- TFG calculada: manter uma casa decimal truncada.
- Percentuais do leucograma: manter uma casa decimal truncada.
- Unidades permanecem somente no cabeçalho da série.

### Delta

- O primeiro ponto não recebe delta.
- Séries escalares recebem delta quando a opção estiver habilitada.
- Leucograma com frações e bilirrubinas consolidadas nunca recebem delta.
- O delta é calculado entre os valores numéricos efetivamente exibidos, e não entre precisões internas ocultas.
- Contagens usam delta inteiro.
- TFG usa uma casa decimal e deve reconciliar aritmeticamente com os pontos truncados exibidos.
- Demais escalares usam a maior precisão significativa exibida entre os dois pontos adjacentes, limitada a duas casas, removendo zeros finais.
- Aumento usa `+`, redução usa `-` e igualdade efetiva usa `±0`.
- Pequenas variações significativas não podem ser convertidas silenciosamente em `±0`; por exemplo, creatinina `1,01 → 0,97` produz `-0,04`.

## Exemplos de aceite

```text
#PCR (mg/L): 19/01/26 - 236,6 | 18/07/26 - 64,9 (-171,7)
#TGO (U/L): 13/01/15 - 20 | 26/01/23 - 29,1 (+9,1)
#Creatinina (mg/dL): 19/01/16 - 0,9 | 07/07/17 - 1,01 (+0,11) | 23/08/17 - 0,97 (-0,04)
#TFG (mL/min/1,73m²): 19/01/16 - 66,6 | 07/07/17 - 57,6 (-9,0) | 23/08/17 - 60,5 (+2,9)
```

## Escopo de implementação

1. Reorganizar `LaboratoryCurvesWindow` sem alterar a janela principal.
2. Ajustar o ViewModel para a exclusividade e habilitação dos filtros.
3. Transportar ao formatador o contexto temporal global necessário.
4. Implementar a compactação editorial determinística.
5. Fazer o delta da TFG operar sobre o valor truncado projetado.
6. Atualizar testes e documentação da curva.
7. Registrar o resultado no diário de desenvolvimento somente depois do gate aprovado.

## Fora do escopo

- Novos exames de curva.
- Mudanças em Capture, Reconstruction ou Sanitization.
- Mudanças em Patient/Episode Assembly.
- Mudanças no catálogo ou na Laboratory Semantic Extraction.
- Mudança da fórmula CKD-EPI ou de seus critérios de elegibilidade.
- Mudanças na Output Projection clínica convencional.
- Interface final destinada ao usuário.

## Execução econômica

### 0. Acesso local antecipado

No início da execução, solicitar autorização para abrir e controlar o `Ars Extractum Inspector` e selecionar o PDF local de referência. O acesso será reservado para a verificação final; não haverá sucessivas aberturas durante a implementação.

### 1. Implementação dirigida

- Inspecionar somente os componentes listados neste contrato.
- Aplicar as mudanças editoriais e de interface incrementalmente.
- Não processar o corpus durante ajustes puramente visuais ou unitários.

### 2. Testes focados

Executar testes pequenos para:

- exclusividade dos filtros e habilitação de campos;
- intervalo inclusivo e últimos X dias;
- decisão global `DD/MM` versus `DD/MM/AA`;
- compactação dos escalares;
- deltas inteiro, adaptativo, positivo, negativo e `±0`;
- delta da TFG reconciliado com os valores exibidos;
- ausência de delta nas séries consolidadas;
- abertura e layout da janela WPF sem exceção.

Falha implica correção e teste regressivo antes de avançar.

### 3. Baseline automatizado

- Executar uma validação dirigida com `Teste01.pdf` cobrindo as 16 opções autorizadas e formas jovens.
- Executar a suíte integral uma única vez após os testes focados passarem.
- Executar build Release e publicar o Inspector corrigido em `dist/`.

### 4. Auditoria autônoma no aplicativo

Abrir somente o executável recém-publicado e processar:

`C:\Projetos\ArsExtractum-v2\test-bundle\Testes canônicos\Teste01.pdf`

Na paciente `SENHORINHA ANTUNES`:

1. abrir `Curvas laboratoriais` e confirmar visualmente a nova organização;
2. selecionar todas as opções disponíveis;
3. escolher `Todos` e habilitar delta;
4. gerar e copiar o texto;
5. conferir PCR, TGO, creatinina, TFG, leucograma e bilirrubinas contra os exemplos e o conteúdo documental;
6. sem reprocessar o PDF, exercitar `Intervalo personalizado` dentro de 2026 e `Últimos X dias`;
7. confirmar habilitação correta dos campos, texto copiável, ausência de crash e ausência de modificação do PDF.

Se o controle visual do Windows não conseguir acessar a janela, isso deve ser informado como bloqueio da confirmação visual; testes programáticos não serão apresentados falsamente como inspeção visual.

### 5. Revisão final

- `git diff --check` sem erros.
- Revisar o diff apenas dos arquivos do gate e alterações já pendentes conhecidas.
- Confirmar que PDF, outputs-base e pipeline produtivo anterior não foram alterados.
- Não realizar commit automaticamente.

## Critérios de SUCCESS

O gate somente será considerado concluído quando:

1. os três filtros estiverem visíveis, exclusivos e funcionais;
2. delta estiver visualmente separado do período;
3. datas usarem dois dígitos de ano de forma global quando necessário;
4. zeros decimais editoriais desnecessários forem removidos;
5. casas significativas forem preservadas;
6. todos os deltas forem aritmeticamente verificáveis pelos valores exibidos;
7. TFG `66,6 → 57,6` produzir `-9,0` e `57,6 → 60,5` produzir `+2,9`;
8. leucograma e bilirrubinas consolidadas permanecerem sem delta;
9. as 16 opções autorizadas continuarem disponíveis no `Teste01.pdf`;
10. janela abrir, gerar e copiar sem crash;
11. testes focados e suíte integral passarem;
12. build e publicação Release passarem;
13. auditoria visual e textual autônoma for concluída;
14. nenhuma regressão for encontrada na projeção convencional;
15. diff final estiver limpo de erros e revisado.

## Resultado esperado

`SUCCESS` com evidências resumidas dos testes, auditoria visual, output conferido, executável publicado e arquivos modificados. Qualquer impossibilidade objetiva de controlar a interface deve ser declarada como `BLOCKED` para o item visual, com o restante das evidências preservado.
