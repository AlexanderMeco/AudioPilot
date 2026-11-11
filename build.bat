@echo off
setlocal
cd /d "%~dp0"

echo ==================================================
echo  AudioGroup - publish self-contained single exe
echo ==================================================
echo.

dotnet --version >nul 2>&1
if errorlevel 1 (
  echo [ERROR] .NET SDK not found.
  echo         Install it with: winget install Microsoft.DotNet.SDK.8
  exit /b 1
)

echo Publishing... (first run downloads runtime packs, please wait)
dotnet publish AudioGroup.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o "%~dp0publish"
if errorlevel 1 (
  echo.
  echo [ERROR] Publish failed.
  echo         If this is an offline machine, try a framework-dependent build instead:
  echo         dotnet publish AudioGroup.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
  exit /b 1
)

echo.
echo [OK] Output: publish\AudioGroup.exe
echo      Copy AudioGroup.exe together with profiles.json to another PC.
echo      Optional: place switch.wav next to the exe for a custom switch sound.
echo.
