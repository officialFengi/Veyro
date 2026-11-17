$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $projectRoot 'Veyro.csproj'
$appOutput = Join-Path $projectRoot 'publish'
$setupProject = Join-Path $PSScriptRoot 'Veyro.Setup.csproj'
$setupOutput = Join-Path $projectRoot 'dist'
dotnet publish $appProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o $appOutput
if ($LASTEXITCODE -ne 0) { throw 'Не удалось собрать Veyro.' }
$uninstallerOutput = Join-Path $PSScriptRoot 'uninstaller-publish'
dotnet publish $setupProject -c Release -r win-x64 --self-contained true -p:UninstallerBuild=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -o $uninstallerOutput
if ($LASTEXITCODE -ne 0) { throw 'Не удалось собрать программу удаления.' }
Compress-Archive -LiteralPath @((Join-Path $appOutput 'Veyro.exe'), (Join-Path $uninstallerOutput 'Uninstall-Veyro.exe')) -DestinationPath (Join-Path $PSScriptRoot 'payload.zip') -Force
dotnet publish $setupProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -o $setupOutput
if ($LASTEXITCODE -ne 0) { throw 'Не удалось собрать установщик.' }
Write-Output (Join-Path $setupOutput 'Veyro-Setup.exe')

