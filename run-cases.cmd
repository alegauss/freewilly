@echo off
rem WW87. Publish this application and run every case in tests\FreeWilly.Cases against it - the one
rem command a person types, here and on CI.
rem
rem It exists because the two scripts it replaces were each their own command with its own defaults,
rem and neither was on the ordinary path: scripts\page-probe.ps1 was a check.yml step and
rem scripts\Capture-Window.ps1 was whatever was last in somebody's shell history.
rem
rem The publish is the application and not the test project. winwright.json names the published
rem single file, and the capture launches it: a run that built only the driver would photograph
rem whatever .exe was last left there, which is a preview of an old build. Release, because that is
rem the configuration the single-file self-contained properties are conditioned on.
rem
rem The cases need a desk, and the page probe needs ISCC on the PATH. That is why this project is
rem not in FreeWilly.slnx - `dotnet test` at the root runs the solution and can promise neither.
setlocal

set CONFIG=%1
if "%CONFIG%"=="" set CONFIG=Release

set RESULTS=%2
if "%RESULTS%"=="" set RESULTS=TestResults

dotnet publish "%~dp0src\FreeWilly.Tray\FreeWilly.Tray.csproj" -c %CONFIG% --nologo || exit /b 1

rem A trx and not just the console, because a run somebody reads afterwards cannot read what
rem scrolled past in it.
dotnet test "%~dp0tests\FreeWilly.Cases\FreeWilly.Cases.csproj" ^
  --configuration %CONFIG% --nologo ^
  --logger "trx;LogFileName=cases.trx" ^
  --results-directory "%~dp0%RESULTS%"

exit /b %ERRORLEVEL%
