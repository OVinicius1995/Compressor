@echo off
setlocal
pushd "%~dp0"
dotnet build JiraVideoCompressor.csproj -c Release --nologo
if errorlevel 1 (
  echo Nao foi possivel compilar. Confira o SDK .NET 9 e as mensagens acima.
  pause
  popd
  exit /b 1
)
start "" "%~dp0bin\Release\net9.0-windows\JiraVideoCompressor.exe" --config "%~dp0appsettings.json"
popd
endlocal
