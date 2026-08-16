# Ars Extractum

Aplicativo desktop Windows para extração local, determinística e auditável de resultados laboratoriais presentes em PDFs textuais.

## Requisito de execução

O Ars Extractum é distribuído como aplicativo **framework-dependent** e requer o **Microsoft .NET Desktop Runtime 10 x64**. Esse runtime fornece o ambiente .NET e os componentes WPF usados pela interface; ele é instalado uma única vez no computador e não precisa ser transportado novamente a cada versão do aplicativo.

- Download oficial: [.NET Desktop Runtime 10 para Windows x64](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- Instalação oferecida pelo repositório: execute [`Install-requirements.ps1`](Install-requirements.ps1). O script verifica a instalação existente e, quando necessário, solicita ao WinGet o pacote oficial `Microsoft.DotNet.DesktopRuntime.10`.

Depois de instalar o requisito, execute [`dist/Ars Extractum.exe`](dist/Ars%20Extractum.exe). O runtime não precisa ser reinstalado nas atualizações seguintes enquanto a versão principal exigida continuar disponível.

## Estrutura do repositório

```text
ArsExtractum.slnx                 solução .NET
Directory.Build.props            versão, análise estática e opções comuns de compilação
Directory.Packages.props         versões centralizadas das dependências NuGet
Install-requirements.ps1         verificação e instalação do .NET Desktop Runtime

dist/
├── Ars Extractum.exe            aplicativo destinado ao usuário final
└── Ars Extractum Inspector.exe  ferramenta técnica de inspeção e auditoria

src/
├── ArsExtractum.Core/            contratos e regras determinísticas do domínio
├── ArsExtractum.PdfPig/          captura textual e geométrica de PDFs via PdfPig
├── ArsExtractum.Runtime/         fachada produtiva compartilhada pelos executáveis
├── ArsExtractum.UserApp/         interface operacional do usuário
└── ArsExtractum.App/             interface técnica do Inspector

tests/
└── ArsExtractum.Tests/           testes unitários, integração, corpus e interface

docs/                             contratos e resultados dos gates recentes
```

### Componentes do motor

| Componente | Responsabilidade |
|---|---|
| `ArsExtractum.Core` | Modelos documentais, assembly de pacientes e episódios, catálogo laboratorial, extração semântica, CKD-EPI 2021, projeção clínica e curvas. |
| `ArsExtractum.PdfPig` | Adaptação da biblioteca PdfPig para captura determinística de páginas, palavras e geometria. |
| `ArsExtractum.Runtime` | Compõe o pipeline oficial e executa a sessão produtiva completa. É a única fachada usada pelo aplicativo final e pelo Inspector. |
| `ArsExtractum.UserApp` | Gerencia PDFs, pacientes, resultado editável, avisos, culturas e curvas sem conter regras clínicas próprias. |
| `ArsExtractum.App` | Expõe etapas e representações intermediárias para desenvolvimento, regressão e auditoria. |

O catálogo produtivo do laboratório está versionado em:

```text
src/ArsExtractum.Core/LaboratorySemantic/Catalog/fsph-nh-laboratory-catalog.v1.json
```

## Pipeline de processamento

```text
PDF
  ↓
Capture
  ↓
Reconstruction
  ↓
Sanitization
  ↓
Patient / Episode Documentary Assembly
  ↓
Laboratory Semantic Extraction
  ↓
SemanticPatientBatch enriquecido por CKD-EPI 2021
  ├── Clinical Output Projection
  └── Laboratory Curve Projection
```

### 1. Capture

Lê PDFs textuais com PdfPig e preserva páginas, palavras, coordenadas e identificadores de origem. Não há OCR nem interpretação clínica nessa etapa.

**Contrato principal:** `CaptureDocument`.

### 2. Reconstruction

Reagrupa as palavras capturadas em linhas e páginas documentais determinísticas. A geometria original permanece disponível para rastreabilidade.

**Contrato principal:** `ReconstructedDocument`.

### 3. Sanitization

Remove cabeçalhos, rodapés, colunas de referência e ruídos documentais conhecidos. Preserva texto original, texto higienizado, regras aplicadas, palavras-fonte e segmentos suprimidos.

**Contrato principal:** `SanitizedDocument`.

### 4. Patient / Episode Documentary Assembly

Agrupa documentos por paciente e episódio, usando identidade, requisição, data e hora. Organiza páginas cronologicamente e representa blocos documentais exatamente equivalentes uma única vez, mantendo todas as aparições como proveniência.

**Contrato principal:** `PatientBatch`.

### 5. Laboratory Semantic Extraction

Reconhece exames e estruturas presentes no catálogo do laboratório. Converte blocos documentais em ocorrências laboratoriais com observações, valores raw e numéricos, unidades, materiais, relações e evidência por campo. Conteúdo não estruturável permanece explicitamente representado; não é descartado.

**Contrato principal:** `SemanticPatientBatch`.

### 6. Derived Measurement — CKD-EPI 2021

Enriquece ocorrências elegíveis de creatinina sérica com TFG calculada pela equação CKD-EPI 2021 creatinina, race-free. A idade é calculada a partir da data de nascimento e da data da requisição. A TFG informada pelo laboratório não é usada como entrada ou fallback.

**Contrato transportado:** `DerivedObservation` dentro da ocorrência de creatinina no `SemanticPatientBatch`.

### 7. Output projections

A projeção clínica seleciona, ordena, abrevia e formata entidades já estruturadas; não volta a interpretar o texto higienizado. A projeção de curvas usa uma lista fechada de resultados quantitativos e oferece filtros temporais e deltas determinísticos.

**Contratos principais:** `ClinicalOutputBatch` e `LaboratoryCurveProjection`.

## Propriedades dos contratos

- IDs determinísticos e contratos versionados.
- Proveniência reversível até documento, página, linha e palavras-fonte.
- Preservação conjunta de valor documental (`RawValue`) e valor estruturado (`NumericValue`).
- Separação entre observação documental e medição calculada pelo software.
- Fallback explícito e cobertura contabilizada, sem descarte silencioso.
- Processamento local, offline e sem telemetria.
- Ausência de IA ou OCR no pipeline produtivo.

## Utilização

1. Execute `dist/Ars Extractum.exe`.
2. Arraste PDFs para a janela ou selecione **Adicionar PDFs**.
3. Remova arquivos inadequados ou use **Limpar sessão**, se necessário.
4. Selecione **Processar PDFs**.
5. Escolha um paciente na lista.
6. Revise e copie o resultado clínico editável.
7. Use **Curvas laboratoriais** para selecionar exames, período e apresentação de delta.
8. Quando houver culturas, observe o alerta e utilize **Conferir culturais** para consultar o conteúdo documental associado.

O botão **Sobre** apresenta versão, finalidade, cálculo renal utilizado, limitações e contato do projeto.

## Aplicativo e Inspector

### Ars Extractum

Interface destinada ao uso regular. Executa sempre o pipeline produtivo completo e não expõe etapas, schemas ou outputs intermediários.

### Ars Extractum Inspector

Ferramenta destinada a desenvolvimento e auditoria. Permite visualizar etapas intermediárias, conferir representações técnicas e exportar pacotes de análise. Não é necessário para o uso normal.

Os dois executáveis usam o mesmo `ArsExtractum.Runtime`; diferenças entre eles pertencem somente à interface e às ferramentas de inspeção.

## Compilação e testes

Para compilar o código-fonte é necessário o **.NET SDK 10**, que é diferente do Desktop Runtime exigido apenas para executar os binários.

```powershell
dotnet restore ArsExtractum.slnx --locked-mode
dotnet build ArsExtractum.slnx -c Release --no-restore
dotnet test tests/ArsExtractum.Tests/ArsExtractum.Tests.csproj -c Release --no-restore
```

A validação consolidada desta versão contém 100 testes automatizados, incluindo regras documentais e semânticas, output, curvas, fachada compartilhada e smoke tests WPF.

## Publicação

```powershell
dotnet publish src/ArsExtractum.UserApp/ArsExtractum.UserApp.csproj `
  -c Release -r win-x64 --self-contained false `
  -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o dist

dotnet publish src/ArsExtractum.App/ArsExtractum.App.csproj `
  -c Release -r win-x64 --self-contained false `
  -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o dist
```

Os binários são single-file e framework-dependent. Bibliotecas do runtime não são incluídas no repositório.

## Escopo e limitações

- Destinado a PDFs textuais produzidos pelo laboratório contemplado pelo catálogo atual.
- Não realiza OCR.
- Não interpreta normalidade, diagnóstico, prognóstico ou conduta.
- Não corrige valores clínicos silenciosamente.
- Resultados culturais possuem variação documental e devem ser conferidos pelo operador.
- A TFG calculada depende da presença e validade dos requisitos documentais definidos no contrato CKD-EPI.

## Privacidade

Todo o processamento ocorre localmente. O aplicativo não envia PDFs, dados de pacientes ou resultados para servidores e não mantém banco de dados ou telemetria.

## Contato

Relatos de correção: `felipe.somavila@hotmail.com`
