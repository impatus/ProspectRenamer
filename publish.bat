@echo off
setlocal
cd /d "%~dp0"

echo Publishing Prospect Renamer for Windows x64...
dotnet publish ".\ProspectRenamer.App\ProspectRenamer.App.csproj" ^
  --configuration Release ^
  --runtime win-x64 ^
  --self-contained true ^
  --output ".\publish" ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:DebugType=None ^
  -p:DebugSymbols=false

if errorlevel 1 (
  echo.
  echo Publish failed.
  exit /b 1
)

echo.
echo Ready: %~dp0publish\ProspectRenamer.exe
endlocal
