# Gate — User Interface v1

## Status

CONSOLIDATED — gate automatizado, inspeção visual por renderização e auditoria interativa aprovados pelo usuário em 2026-08-16.

## Objetivo

Entregar a interface operacional destinada ao usuário final do Ars Extractum como executável separado do `Ars Extractum Inspector`, porém consumindo o mesmo motor produtivo, os mesmos contratos e as mesmas projeções. A interface deve privilegiar clareza, rapidez e segurança, sem expor ferramentas de auditoria ou detalhes internos do pipeline.

## Decisões congeladas

### Produtos desktop

- `Ars Extractum Inspector` permanece como ferramenta técnica de inspeção e auditoria.
- `Ars Extractum` será um novo executável destinado ao uso final.
- Os dois executáveis serão framework-dependent, publicados separadamente em `dist/`.
- A interface final não poderá copiar, reimplementar nem alterar regras de Capture, Reconstruction, Sanitization, Assembly, Laboratory Semantic Extraction, CKD-EPI, Output Projection ou Laboratory Curve Projection.
- A sequência produtiva completa será exposta por uma única fachada compartilhada de processamento de sessão, usada por ambos os aplicativos.
- Componentes exclusivamente visuais podem ser próprios de cada aplicativo; regras e resultados permanecem compartilhados.

### Escopo visual

- Paleta em branco, preto e tons de cinza.
- Amarelo reservado para avisos.
- Botões primários em grafite/preto, com texto branco e contraste evidente.
- Botões secundários em cinza claro.
- Painéis brancos, fundo cinza muito claro, bordas discretas e espaçamento regular.
- Título `ARS EXTRACTUM` em Georgia, maiúsculo, com identidade editorial e sem dependência de fonte externa.
- Layout limpo: nenhuma métrica, legenda ou instrução será exibida se não orientar uma ação do usuário.
- Janela redimensionável, com largura mínima capaz de preservar o uso das três colunas.

### Composição principal

A área útil será organizada em três colunas:

1. `PDFs`: coluna esquerda, largura compacta, lista rolável.
2. `Pacientes`: coluna intermediária, lista rolável um pouco mais estreita que o resultado.
3. `Resultado`: coluna principal e flexível, ocupando a maior parte da janela.

O cabeçalho conterá somente:

- identidade do aplicativo;
- ação `Sobre`;
- área de inclusão de PDFs por arrastar/soltar ou seleção;
- ação primária `Processar PDFs`;
- ação `Cancelar` somente durante processamento.

O rodapé conterá:

- estado resumido da sessão;
- progresso durante processamento;
- aviso fixo de que o aplicativo organiza resultados laboratoriais e não interpreta diagnóstico ou conduta.

### Entrada e gestão dos PDFs

- Toda a janela aceitará drag and drop de arquivos PDF.
- O mesmo painel oferecerá o botão `Adicionar PDFs`, com seleção múltipla pelo Explorer.
- Arquivos que não sejam PDF serão recusados com aviso claro, sem interromper a sessão.
- O mesmo caminho físico não será adicionado duas vezes à sessão.
- A lista mostrará nome e estado essencial do arquivo, sem tempos técnicos detalhados.
- Será possível selecionar um PDF e usar `Remover PDF`.
- Um botão de remoção contextual por item poderá ser usado somente se permanecer evidente e acessível; a remoção por seleção é obrigatória e suficiente.
- `Limpar sessão` removerá PDFs, pacientes, resultados, avisos transitórios e estado de processamento, retornando ao estado inicial.
- Remoção e limpeza serão desabilitadas durante processamento para evitar estado parcialmente mutado.

### Processamento

- `Processar PDFs` executará sempre o pipeline produtivo completo; o usuário não escolherá etapas.
- O botão ficará habilitado somente quando houver PDFs válidos e nenhuma execução ativa.
- `Cancelar` ficará visível/habilitado somente durante execução.
- O cancelamento deverá ser cooperativo, preservar a integridade do aplicativo e retornar a um estado utilizável.
- Falha de um documento será apresentada de forma compreensível, sem encerramento inesperado e sem exposição de stack trace.
- A interface permanecerá responsiva e mostrará progresso e estado atual em linguagem de usuário.
- Um novo processamento substituirá os resultados anteriores da mesma sessão de forma explícita e determinística.

### Pacientes

- Após processamento, pacientes serão listados pelo nome.
- Cada item poderá informar somente a quantidade de episódios e PDFs, quando isso ajudar a diferenciação.
- O primeiro paciente será selecionado automaticamente quando houver resultado.
- A seleção de paciente atualizará resultado, avisos culturais e disponibilidade das curvas.
- Nenhum output de pacientes diferentes poderá ser combinado na área de resultado.

### Resultado clínico

- A área será editável, selecionável, rolável e adequada à cópia manual.
- `Copiar resultado` copiará somente o texto clínico do paciente selecionado.
- O usuário poderá editar a projeção localmente sem modificar o modelo semântico ou os demais pacientes.
- Alterar o paciente e retornar a ele restaurará a projeção canônica, não uma edição temporária anterior.
- A opção `Exibir unidades` permanecerá disponível em posição discreta junto às ações do resultado.
- Culturas continuarão omitidas por padrão da projeção clínica detalhada.
- Havendo culturas, o output preservará a indicação editorial `Culturais` e a área amarela informará que a conferência documental é necessária.
- `Conferir culturais` permanecerá disponível somente quando o paciente selecionado possuir culturas.

### Curvas laboratoriais

- `Curvas laboratoriais` ficará habilitado somente quando houver paciente selecionado e lote semântico enriquecido válido.
- A janela consolidada de curvas será preservada funcionalmente e adaptada apenas à identidade visual final.
- Será aberta com `Owner` definido como a janela principal, por `ShowDialog()` e com `ShowInTaskbar = false`.
- Ela bloqueará a janela principal enquanto estiver aberta e não criará uma segunda entrada na barra de tarefas.
- Seleção de exames, filtros temporais, delta, geração e cópia permanecerão conforme o gate consolidado de curvas.
- Fechar a janela retornará à mesma sessão e ao mesmo paciente.

### Avisos

- A área de avisos terá fundo amarelo, altura máxima e rolagem vertical própria.
- Ficará recolhida ou compacta quando não houver aviso relevante.
- Poderá informar culturas detectadas, PDFs rejeitados, documentos com falha, cancelamento e limitações operacionais relevantes.
- Avisos não serão inseridos no texto clínico copiável, salvo a indicação editorial já contratada para culturas.
- A área não exibirá notices técnicos de informação que não demandem atenção do operador.

### Sobre

`Sobre` abrirá diálogo modal, com `Owner`, `ShowDialog()` e `ShowInTaskbar = false`, contendo de forma breve:

- nome e versão;
- finalidade do Ars Extractum;
- processamento local e offline;
- ausência de diagnóstico, prognóstico ou definição de conduta;
- CKD-EPI 2021 race-free e natureza calculada da TFG exibida;
- limitação e necessidade de conferência de culturas;
- contato `felipe.somavila@hotmail.com`;
- créditos do projeto.

Informações de schema, catálogo, cobertura e implementação não aparecerão nessa janela.

## Elementos proibidos na interface final

- seleção das etapas do pipeline;
- visualização de outputs intermediários;
- exportação de ZIPs de auditoria;
- nomes de schemas, regras ou IDs internos;
- cobertura, contadores diagnósticos e tempos por etapa;
- botões de reconstrução, JSON ou validação;
- termos voltados ao desenvolvedor ou ao auditor.

Esses recursos permanecem no Inspector.

## Arquitetura de implementação

1. Criar `src/ArsExtractum.UserApp/` como novo projeto WPF `WinExe`.
2. Publicar com `AssemblyName` igual a `Ars Extractum`.
3. Adicionar o projeto à solution.
4. Extrair do Inspector a composição produtiva para uma fachada compartilhada, responsável por:
   - construir o pipeline oficial;
   - processar a coleção de PDFs;
   - executar Assembly, extração semântica, CKD-EPI e Output Projection na ordem oficial;
   - devolver pacientes, `SemanticPatientBatch`, projeção clínica, avisos e progresso.
5. Manter a fachada sem dependência de XAML e sem políticas exclusivas de auditoria.
6. Fazer Inspector e UserApp consumirem essa fachada.
7. Manter Laboratory Curve Projection e formatadores no Core; a interface final apenas fornece opções e apresenta o resultado.
8. Não alterar contratos produtivos nem baselines editoriais para atender ao layout.

## Estados obrigatórios da interface

1. **Inicial:** sem arquivos; processamento e ações de resultado desabilitados.
2. **Arquivos carregados:** lista preenchida; processamento habilitado.
3. **Processando:** inclusão, remoção e limpeza bloqueadas; progresso e cancelamento disponíveis.
4. **Concluído:** pacientes e output disponíveis; ações condicionadas ao paciente selecionado.
5. **Cancelado:** sessão utilizável, sem lote parcial apresentado como completo.
6. **Falha:** erro compreensível, aplicativo aberto e possibilidade de corrigir a seleção ou limpar a sessão.

## Matriz mínima de verificação funcional

| Controle/campo | Verificação obrigatória |
|---|---|
| Área drag and drop | aceita um e vários PDFs; rejeita outros formatos; não duplica o mesmo caminho |
| Adicionar PDFs | abre seletor, aceita múltiplos PDFs e permite cancelar sem alterar a sessão |
| Lista de PDFs | rola, seleciona, trunca nomes longos sem perder tooltip/caminho útil |
| Remover PDF | remove somente o selecionado e atualiza habilitações |
| Limpar sessão | restaura integralmente o estado inicial |
| Processar PDFs | executa a cadeia produtiva completa uma única vez |
| Cancelar | cancela com segurança e não deixa resultado parcial tratável como final |
| Progresso/status | acompanha execução sem travar a janela |
| Lista de pacientes | seleciona corretamente e nunca mistura outputs |
| Resultado editável | permite edição, seleção, rolagem e cópia |
| Exibir unidades | reprojeta apenas o paciente/output conforme contrato e não altera evidência |
| Copiar resultado | copia exatamente o texto visível do paciente selecionado |
| Avisos | aparecem, rolam, não deformam a janela e não contaminam o texto copiado |
| Conferir culturais | só habilita com cultura e abre conteúdo correspondente ao paciente |
| Curvas laboratoriais | abre modal, bloqueia a janela principal, não aparece separadamente na barra e mantém todas as funções consolidadas |
| Sobre | abre modal, exibe conteúdo contratado e fecha sem afetar a sessão |
| Redimensionamento | preserva acesso às listas, output e ações na resolução mínima |
| Fechar aplicativo | encerra diálogos filhos e não mantém processo órfão |

## Estratégia de execução econômica

### 1. Estrutura e motor compartilhado

- Criar primeiro o projeto e a fachada compartilhada.
- Migrar o Inspector para a fachada e executar testes focados de equivalência antes de construir a nova tela.
- Não alterar o motor clínico nem reprocessar o corpus durante ajustes puramente visuais.

### 2. Funcionalidade antes do acabamento

- Implementar estados, comandos, drag and drop, listas, seleção e modais.
- Validar fluxos com testes de ViewModel e smoke tests WPF.
- Aplicar o refinamento visual somente após o fluxo estar estável.

### 3. Diagnóstico visual dirigido

- Renderizar e inspecionar a janela nos estados inicial, carregado, processando simulado, concluído, com aviso e com diálogo modal.
- Verificar hierarquia visual, alinhamento, contraste, áreas vazias, truncamento, rolagem e resolução mínima.
- Ajustar autonomamente defeitos visuais objetivos antes da entrega.
- Evitar mudanças estéticas repetitivas sem evidência de ganho funcional.

### 4. Teste real final

- Publicar os dois executáveis em Release.
- Abrir exclusivamente o novo `Ars Extractum`.
- Processar um conjunto pequeno de PDFs canônicos que cubra múltiplos pacientes, culturas e curvas.
- Exercitar cada controle e campo da matriz acima.
- Comparar o output final e as curvas com o Inspector usando a mesma entrada.
- Confirmar igualdade semântica e editorial, ressalvadas apenas diferenças de interface.
- Confirmar que nenhum PDF ou output-base foi modificado.

### 5. Regressão e revisão

- Executar testes focados durante a implementação.
- Executar a suíte integral uma única vez após aprovação dos testes focados e do diagnóstico visual.
- Executar build Release completo.
- Executar `git diff --check` e revisar o diff final.
- Atualizar documentação e diário apenas após o gate técnico passar.
- Não realizar commit automaticamente.

## Critérios de SUCCESS

O gate somente poderá ser apresentado como concluído quando:

1. existir executável final separado do Inspector;
2. ambos usarem comprovadamente a mesma fachada e o mesmo motor produtivo;
3. o Inspector não sofrer regressão;
4. a interface final não expuser controles técnicos;
5. drag and drop, seleção, remoção e limpeza funcionarem;
6. processamento, cancelamento, progresso e falhas preservarem uma sessão íntegra;
7. pacientes forem selecionáveis sem mistura de output;
8. resultado for editável, rolável e copiável;
9. unidades, culturas e conferência cultural respeitarem os contratos vigentes;
10. curvas e Sobre forem modais, bloquearem a janela principal e não criarem outra entrada na barra de tarefas;
11. alertas forem claros, amarelos, limitados e roláveis;
12. todos os controles da matriz forem exercitados e aprovados;
13. diagnóstico visual confirmar layout equilibrado na resolução normal e mínima;
14. saída do UserApp corresponder à saída do Inspector para a mesma entrada;
15. testes focados, suíte integral e build Release passarem;
16. os dois executáveis publicados abrirem e concluírem seus smoke tests;
17. nenhum PDF, output-base ou contrato clínico tiver sido modificado;
18. diff final estiver revisado e sem erro de whitespace;
19. documentação e diário registrarem o novo produto e suas responsabilidades;
20. não houver erro conhecido reproduzível em campo ou botão contratado.

## Condição de entrega

O resultado somente será entregue para aprovação humana após o gate técnico e visual estar satisfatório. Defeito local simples implica diagnóstico, correção, teste regressivo e repetição do item afetado. A execução somente poderá terminar como `BLOCKED` diante de impedimento externo objetivo que não possa ser resolvido localmente.

## Fora do escopo

- mudanças na extração, catálogo ou regras editoriais clínicas;
- novos exames ou cálculos derivados;
- instalador gráfico;
- atualização automática;
- persistência de sessões;
- telemetria, servidor, login ou banco de dados;
- abertura de PDF em visualizador incorporado;
- redesign do Inspector além da adaptação necessária à fachada compartilhada;
- commit ou publicação no GitHub.

## Resultado da implementação

- Novo projeto `src/ArsExtractum.UserApp/` e executável `Ars Extractum.exe`.
- Fachada única em `src/ArsExtractum.Runtime/`, também usada pelo Inspector para compor o pipeline documental oficial.
- Fluxo real validado com `Teste01.pdf`, incluindo pacientes, output, aviso cultural, unidades e disponibilidade das curvas.
- Output do aplicativo final comparado literalmente ao output do Inspector para a mesma entrada.
- Estados iniciais e concluídos renderizados e inspecionados em 1500×860 e 1120×650.
- Testes focados da interface: 5 aprovados.
- Suíte integral: 100 aprovados, zero falha.
- Build Release: zero aviso e zero erro.
- Auditoria interativa humana concluída com todos os campos e controles funcionais; fase aprovada e consolidada.
