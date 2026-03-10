@echo off
chcp 65001 >nul
echo ========================================
echo  Діагностика збереження пристроїв
echo ========================================
echo.

REM Знайти БД файл
set DB_PATH=

if exist ".\DocControlService\bin\Debug\net8.0-windows\DocControl.db" (
    set DB_PATH=.\DocControlService\bin\Debug\net8.0-windows\DocControl.db
)
if exist ".\DocControlService\bin\Release\net8.0-windows\DocControl.db" (
    set DB_PATH=.\DocControlService\bin\Release\net8.0-windows\DocControl.db
)
if exist "C:\ProgramData\DocControlService\DocControl.db" (
    set DB_PATH=C:\ProgramData\DocControlService\DocControl.db
)

if "%DB_PATH%"=="" (
    echo ❌ Не знайдено файл бази даних DocControl.db
    echo.
    echo Можливі розташування:
    echo - .\DocControlService\bin\Debug\net8.0-windows\DocControl.db
    echo - .\DocControlService\bin\Release\net8.0-windows\DocControl.db
    echo - C:\ProgramData\DocControlService\DocControl.db
    echo.
    pause
    exit /b 1
)

echo ✅ Знайдено БД: %DB_PATH%
echo.

REM Перевірити чи є sqlite3
where sqlite3 >nul 2>nul
if errorlevel 1 (
    echo ⚠️  sqlite3 не знайдено. Встанови SQLite щоб перевірити БД.
    echo    Завантажити: https://www.sqlite.org/download.html
    echo.
    echo Показую тільки Event Log...
    echo.
    goto :check_eventlog
)

echo [1] Пристрої в БД (таблиця Devises):
echo ========================================
sqlite3 "%DB_PATH%" "SELECT id, Name, Acces FROM Devises ORDER BY id;"
echo.

:check_eventlog
echo [2] Останні події в Event Log (DocControlService):
echo ========================================
powershell -Command "Get-EventLog -LogName Application -Source DocControlService -Newest 20 | Where-Object {$_.Message -like '*NetworkCore*'} | Format-Table TimeGenerated, EntryType, Message -AutoSize"
echo.

echo ========================================
echo Інструкції:
echo ========================================
echo.
echo 1. Перевір чи є пристрої в таблиці Devises
echo 2. Перевір Event Log на помилки збереження
echo 3. Якщо пристроїв немає - перезапусти DocControlService
echo 4. Запусти тестовий вузол: start_test_node.bat
echo 5. Почекай 10-20 секунд
echo 6. Запусти цей скрипт знову
echo.
pause
