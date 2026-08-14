# Ars Extractum

Aplicativo desktop Windows para extração local, determinística e auditável de resultados laboratoriais em PDFs textuais.

## Executar

1. Execute `Install-requirements.ps1` uma única vez para instalar o .NET Desktop Runtime 10 x64, caso ainda não exista.
2. Execute `dist/ArsExtractum.exe`.

O processamento produtivo é local e offline. A conexão é necessária somente se o runtime precisar ser instalado pelo WinGet.

## Compilar e testar

Requer .NET SDK 10:

```powershell
dotnet restore ArsExtractum.slnx
dotnet build ArsExtractum.slnx -c Release --no-restore
dotnet test ArsExtractum.slnx -c Release --no-build
```

## Publicar o executável

```powershell
dotnet publish src/ArsExtractum.App/ArsExtractum.App.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o dist
```

A publicação é framework-dependent e single-file: o repositório transporta o aplicativo, não uma cópia do runtime .NET/WPF.
