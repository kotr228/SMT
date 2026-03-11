@echo off
chcp 65001 >nul
echo ========================================
echo  Перевірка стану NetworkCore
echo ========================================
echo.

REM Перевірити чи запущений DocControlService
echo [1] Перевірка Windows Service...
sc query DocControlService | find "RUNNING" >nul
if errorlevel 1 (
    echo ❌ DocControlService НЕ ЗАПУЩЕНИЙ
    echo.
    echo Для запуску сервісу:
    echo 1. Відкрий Services.msc
    echo 2. Знайди "DocControl Service"
    echo 3. Правий клік -^> Start
    echo.
) else (
    echo ✅ DocControlService ЗАПУЩЕНИЙ
    echo.
)

REM Перевірити чи запущений окремий NetworkCore.exe
echo [2] Перевірка окремих процесів NetworkCore.exe...
tasklist | find /I "DocControlNetworkCore.exe" >nul
if errorlevel 1 (
    echo ✅ Немає окремих процесів NetworkCore
    echo.
) else (
    echo ⚠️  ЗНАЙДЕНО окремі процеси NetworkCore.exe:
    echo.
    tasklist | find /I "DocControlNetworkCore.exe"
    echo.
    echo УВАГА: Закрий всі окремі процеси NetworkCore.exe!
    echo Тільки DocControlService має запускати NetworkCore!
    echo.
)

REM Перевірити які процеси використовують порти 8000 та 9000
echo [3] Перевірка використання портів 8000/9000...
netstat -ano | find ":8000" | find "LISTENING"
netstat -ano | find ":9000"
echo.

echo ========================================
echo Рекомендації:
echo ========================================
echo.
echo 1. Закрий ВСІ вікна NetworkCore.exe (консольні вікна)
echo 2. Запусти DocControlService через Services.msc
echo 3. Відкрий DocControlUI
echo 4. Перейди на вкладку "💻 Мережа"
echo 5. Натисни "🔄 Оновити"
echo.
pause
