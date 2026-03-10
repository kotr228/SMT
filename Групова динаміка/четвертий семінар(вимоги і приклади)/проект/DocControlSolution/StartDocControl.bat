@echo off
:: ====================================================
:: Coffee Cat Document Control System - Launcher
:: ====================================================

title Coffee Cat - Запуск системи

echo.
echo ===================================
echo   Coffee Cat Document Control
echo   v0.10.0 - Multi-User Edition
echo ===================================
echo.

:: Перевірка прав адміністратора
net session >nul 2>&1
if %errorLevel% == 0 (
    echo [OK] Запущено з правами адміністратора
) else (
    echo [!] УВАГА: Програма запущена без прав адміністратора
    echo.
    echo Для повного функціоналу потрібні права адміністратора:
    echo - Запуск/зупинка Windows Service
    echo - Управління мережевими шарами
    echo.
    choice /C YN /M "Продовжити без прав адміністратора"
    if errorlevel 2 exit /b
)

echo.
echo [1/3] Перевірка Windows Service...

:: Перевірка чи запущений сервіс
sc query DocControlService | find "RUNNING" >nul
if %errorLevel% == 0 (
    echo [OK] DocControlService запущено
) else (
    echo [!] DocControlService не запущено

    choice /C YN /M "Запустити DocControlService зараз"
    if errorlevel 1 (
        echo [*] Запуск сервісу...
        net start DocControlService

        if %errorLevel% == 0 (
            echo [OK] Сервіс успішно запущено
        ) else (
            echo [X] Не вдалося запустити сервіс
            echo     Можливо, сервіс не встановлено або потрібні права адміністратора
        )
    )
)

echo.
echo [2/3] Перевірка файлів...

if exist "DocControlUI\bin\Debug\DocControlUI.exe" (
    echo [OK] DocControlUI.exe знайдено (Debug)
    set "UI_PATH=DocControlUI\bin\Debug\DocControlUI.exe"
) else if exist "DocControlUI\bin\Release\DocControlUI.exe" (
    echo [OK] DocControlUI.exe знайдено (Release)
    set "UI_PATH=DocControlUI\bin\Release\DocControlUI.exe"
) else (
    echo [X] DocControlUI.exe не знайдено
    echo     Спочатку скомпілюйте проект у Visual Studio
    pause
    exit /b 1
)

echo.
echo [3/3] Запуск інтерфейсу...
echo.

:: Запуск UI
start "" "%UI_PATH%"

echo [OK] Coffee Cat запущено!
echo.
echo Вікно буде закрито через 3 секунди...
timeout /t 3 >nul

exit
