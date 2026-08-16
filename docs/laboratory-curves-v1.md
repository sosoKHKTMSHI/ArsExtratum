# Laboratory Curve Projection v1

## Status

Consolidada no Ars Extractum Inspector após baseline do `Teste01.pdf`, suíte integral e inspeção visual da janela WPF renderizada com o lote real.

## Arquitetura

```text
SemanticPatientBatch enriquecido
        ├── Output Projection convencional
        └── Laboratory Curve Projection
```

A projeção consome somente entidades estruturadas e resultados derivados válidos. Ela não interpreta o texto clínico final, não altera episódios e não cria novos parsers.

## Escopo fechado

- Hemoglobina
- Plaquetas
- Leucócitos totais
- Leucograma com frações
- PCR
- TGO
- TGP
- Amilase
- Lipase
- Bilirrubinas isoladas
- Bilirrubina com frações
- Creatinina sérica
- TFG CKD-EPI calculada
- Ureia
- Sódio
- Potássio

As opções são exibidas dinamicamente somente quando existem resultados elegíveis para o paciente selecionado. Conceitos externos à lista não são admitidos por semelhança ou por possuírem valor numérico.

## Regras editoriais

- Pontos em ordem cronológica crescente.
- Filtros: todos, intervalo personalizado ou últimos X dias.
- Datas `DD/MM` quando o período efetivo está em um ano e `DD/MM/AA` globalmente quando atravessa anos.
- Horário somente quando há múltiplos pontos da série no mesmo dia.
- Unidades obrigatórias no cabeçalho da curva.
- Zeros decimais finais sem significado são removidos somente na projeção; a evidência bruta permanece preservada.
- Primeiro ponto sem delta; aumento `+`, redução `-` e estabilidade `±0`.
- Delta usa a precisão significativa dos valores exibidos; a TFG usa os valores já truncados para que a diferença seja aritmeticamente verificável.
- Resultados censurados por `<` ou `>` não são tratados como valores exatos.
- Ausência não representa zero e não gera placeholder.
- Leucócitos totais são escalares e admitem delta.
- Leucograma com frações usa `#Leucograma (/mm³)`, percentuais truncados em uma casa e não admite delta.
- Bilirrubinas isoladas geram BT, BD e BI em linhas independentes e admitem delta.
- Bilirrubina com frações consolida os componentes na mesma linha e não admite delta.
- TFG usa somente CKD-EPI `Computed`, com uma casa decimal truncada.

## Interface

O botão `Curvas laboratoriais` abre uma janela para seleção múltipla, três filtros temporais exclusivos, seção independente de apresentação/delta, geração e cópia direta do texto.

## Referência de auditoria

`test-bundle/Testes canônicos/Teste01.pdf` cobre as 16 opções autorizadas e formas jovens do leucograma. O corpus não integra o repositório distribuível e permanece somente leitura no ambiente de desenvolvimento.

## Componentes

- `src/ArsExtractum.Core/LaboratoryCurves/`
- `src/ArsExtractum.App/LaboratoryCurvesWindow.xaml`
- `src/ArsExtractum.App/ViewModels/LaboratoryCurvesViewModel.cs`
- `tests/ArsExtractum.Tests/LaboratoryCurveProjectorTests.cs`
- `tests/ArsExtractum.Tests/LaboratoryCurveCorpusValidationTests.cs`
