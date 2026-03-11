@echo off
echo Cleaning all bin and obj folders...

cd /d "%~dp0"

echo.
echo Deleting bin folders...
for /d /r . %%d in (bin) do @if exist "%%d" echo Deleting: %%d && rd /s /q "%%d"

echo.
echo Deleting obj folders...
for /d /r . %%d in (obj) do @if exist "%%d" echo Deleting: %%d && rd /s /q "%%d"

echo.
echo ✅ Build cache cleared!
echo.
echo Now rebuild solution in Visual Studio (Ctrl+Shift+B)
pause
