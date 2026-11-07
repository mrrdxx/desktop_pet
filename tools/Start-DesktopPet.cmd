@echo off
setlocal
set "PET_ROOT=%~dp0.."
set "PET_EXE=%PET_ROOT%\src\DesktopPet.App\bin\Debug\net10.0-windows\DesktopPet.exe"
if not exist "%PET_EXE%" goto missing
if /i "%~1"=="--check" (
  echo Ready: "%PET_EXE%"
  exit /b 0
)
start "" /d "%PET_ROOT%" "%PET_EXE%"
exit /b %errorlevel%

:missing
echo DesktopPet build was not found at:
echo   "%PET_EXE%"
echo Please build first:
echo   dotnet build DesktopPet.slnx -c Debug
echo See README.md or docs/STATUS.md for details.
pause
exit /b 1
