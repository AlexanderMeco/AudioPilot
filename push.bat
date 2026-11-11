@echo off
setlocal
cd /d "%~dp0"

echo ==========================================
echo  Push AudioPilot to GitHub
echo ==========================================
echo.

git remote get-url origin >nul 2>&1
if errorlevel 1 (
  echo [ERROR] No 'origin' remote is configured yet.
  echo.
  echo Send the repository URL to the assistant first, or run:
  echo   git remote add origin https://github.com/YOUR_NAME/AudioPilot.git
  goto :end
)

echo Remote:
git remote get-url origin
echo.
echo A GitHub sign-in window may appear. Complete it once.
echo (build outputs and personal config are ignored by .gitignore)
echo.
git push -u origin main
if errorlevel 1 (
  echo.
  echo [FAILED] Push failed. See the message above.
) else (
  echo.
  echo [OK] Pushed to GitHub successfully.
)

:end
echo.
pause
