@echo off
rem GROBS Optimizer - gera os executaveis x64/x86 e o instalador em dist\
rem Requer: Node.js 18+ e .NET SDK 6+ (o app final so precisa do Windows 7 SP1/10/11 com .NET 4.8 e WebView2).
setlocal
cd /d "%~dp0"

echo [1/4] Interface (React + Vite)...
pushd ui
if not exist node_modules call npm install --no-audit --no-fund || goto :erro
call npm run build || goto :erro
popd

echo [2/4] Executavel x64...
dotnet build host\GrobsOptimizer.csproj -c Release -p:Platform=x64 -nologo -v:q || goto :erro
echo [3/4] Executavel x86...
dotnet build host\GrobsOptimizer.csproj -c Release -p:Platform=x86 -nologo -v:q || goto :erro

if not exist dist\payload mkdir dist\payload
copy /y host\bin\x64\Release\net48\GROBS.exe dist\GROBS-Optimizer-x64.exe >nul
copy /y host\bin\x86\Release\net48\GROBS.exe dist\GROBS-Optimizer-x86.exe >nul

echo [4/4] Instalador...
dotnet build installer\Installer.csproj -c Release -nologo -v:q || goto :erro
copy /y installer\bin\Release\net48\GROBS-Optimizer-Setup.exe dist\GROBS-Optimizer-Setup.exe >nul
echo.
echo Pronto em dist\:
echo   GROBS-Optimizer-Setup.exe  (instalador com escolha de modulos, 32 e 64 bits)
echo   GROBS-Optimizer-x64.exe / GROBS-Optimizer-x86.exe  (versoes portateis)
exit /b 0

:erro
echo Falha no build.
exit /b 1
