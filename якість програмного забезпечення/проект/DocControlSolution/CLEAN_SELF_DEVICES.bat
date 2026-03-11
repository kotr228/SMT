@echo off
chcp 65001 >nul
echo ========================================
echo  Очищення власних пристроїв з БД
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
    pause
    exit /b 1
)

echo ✅ Знайдено БД: %DB_PATH%
echo.

REM Перевірити чи є sqlite3
where sqlite3 >nul 2>nul
if errorlevel 1 (
    echo ⚠️  sqlite3 не знайдено. Встанови SQLite щоб очистити БД.
    echo    Завантажити: https://www.sqlite.org/download.html
    echo.
    pause
    exit /b 1
)

echo Пристрої ДО очищення:
echo ========================================
sqlite3 "%DB_PATH%" "SELECT id, Name FROM Devises;"
echo.

REM Отримати локальне ім'я комп'ютера
for /f "tokens=*" %%a in ('hostname') do set HOSTNAME=%%a
for /f "tokens=*" %%a in ('echo %USERNAME%') do set USERNAME_VAR=%%a

echo Видаляємо пристрої з ім'ям: %USERNAME_VAR%@%HOSTNAME%
echo.

REM Видалити пристрої що містять локальне ім'я
sqlite3 "%DB_PATH%" "DELETE FROM Devises WHERE Name LIKE '%%%USERNAME_VAR%@%HOSTNAME%%%';"

echo Пристрої ПІСЛЯ очищення:
echo ========================================
sqlite3 "%DB_PATH%" "SELECT id, Name FROM Devises;"
echo.

echo ✅ Очищення завершено!
echo.
pause
