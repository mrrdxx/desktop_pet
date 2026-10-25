@echo off
setlocal
set "PET_ROOT=%~dp0.."
set "PET_EXE=%PET_ROOT%\artifacts\bear-natural\build\bin\DesktopPet.App\debug\DesktopPet.exe"
if not exist "%PET_EXE%" goto missing
if /i "%~1"=="--check" (
  echo Ready: "%PET_EXE%"
  exit /b 0
)
start "" /d "%PET_ROOT%" "%PET_EXE%"
exit /b %errorlevel%

:missing
echo DesktopPet bear build was not found.
echo Please rebuild the bear version using the instructions in docs/STATUS.md.
pause
exit /b 1
