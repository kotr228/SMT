@echo off
REM Скрипт для запуску тестового вузла NetworkCore на тому ж комп'ютері

echo ========================================
echo  Запуск тестового вузла NetworkCore
echo ========================================
echo.

REM Створити папку для тестового вузла
set TEST_DIR=C:\TestNode
if not exist "%TEST_DIR%" mkdir "%TEST_DIR%"
echo Папка: %TEST_DIR%

REM Створити SharedFiles для тестового вузла
if not exist "%TEST_DIR%\SharedFiles" mkdir "%TEST_DIR%\SharedFiles"
echo Створено: %TEST_DIR%\SharedFiles

REM Створити тестові файли
echo Тестовий файл 1 > "%TEST_DIR%\SharedFiles\test1.txt"
echo Тестовий файл 2 > "%TEST_DIR%\SharedFiles\test2.txt"
echo Створено тестові файли

REM Перевірка що ми в правильній папці
if not exist "DocControlNetworkCore" (
    echo ПОМИЛКА: Скрипт запущено з неправильної папки!
    echo.
    echo Поточна папка: %CD%
    echo.
    echo Скрипт має запускатися з папки DocControlSolution:
    echo Приклад: C:\Users\...\DCS\Geocadastr_0_1\DocControlSolution\
    echo.
    pause
    exit /b 1
)

REM Перевірка наявності dotnet SDK
where dotnet >nul 2>nul
if errorlevel 1 (
    echo ПОМИЛКА: dotnet SDK не знайдено!
    echo.
    echo Встанови .NET 8.0 SDK з https://dotnet.microsoft.com/download
    echo.
    pause
    exit /b 1
)

REM Автоматична збірка проекту
echo ========================================
echo  Збірка проекту DocControlNetworkCore
echo ========================================
echo.
dotnet build DocControlNetworkCore\DocControlNetworkCore.csproj -c Debug --verbosity minimal
if errorlevel 1 (
    echo.
    echo ПОМИЛКА: Збірка проекту не вдалась!
    echo.
    echo Відкрий проект у Visual Studio і подивись на помилки компіляції.
    echo.
    pause
    exit /b 1
)
echo.
echo ✓ Проект успішно зібрано
echo.

REM Знайти NetworkCore.exe
set SOURCE_EXE=.\DocControlNetworkCore\bin\Debug\net8.0-windows\DocControlNetworkCore.exe
if not exist "%SOURCE_EXE%" (
    echo ПОМИЛКА: Не знайдено %SOURCE_EXE%
    echo Збірка завершилась, але exe не створено. Перевір помилки компіляції.
    echo.
    pause
    exit /b 1
)

echo Знайдено: %SOURCE_EXE%

REM Копіюємо всі файли з папки bin рекурсивно
for %%F in ("%SOURCE_EXE%") do set SOURCE_DIR=%%~dpF
echo Копіювання файлів з %SOURCE_DIR%...
xcopy /E /I /Y /Q "%SOURCE_DIR%*" "%TEST_DIR%\" > nul
echo Скопійовано NetworkCore.exe та всі залежності (включаючи DLL)

REM Створити network_identity.json з іншими портами
echo { > "%TEST_DIR%\network_identity.json"
echo   "InstanceId": "22222222-2222-2222-2222-222222222222", >> "%TEST_DIR%\network_identity.json"
echo   "UserName": "TestNode", >> "%TEST_DIR%\network_identity.json"
echo   "MachineName": "TEST-PC", >> "%TEST_DIR%\network_identity.json"
echo   "IpAddress": "127.0.0.1", >> "%TEST_DIR%\network_identity.json"
echo   "TcpPort": 8001, >> "%TEST_DIR%\network_identity.json"
echo   "UdpPort": 9001 >> "%TEST_DIR%\network_identity.json"
echo } >> "%TEST_DIR%\network_identity.json"
echo Створено network_identity.json

echo.
echo ========================================
echo  Запуск тестового вузла...
echo  TCP: 8001, UDP: 9001
echo ========================================
echo.
echo ВАЖЛИВО: NetworkCore потребує прав адміністратора
echo Якщо з'явиться запит UAC - дозволь запуск
echo.

cd /d "%TEST_DIR%"
start "TestNode NetworkCore" DocControlNetworkCore.exe --debug

echo.
echo Тестовий вузол запущено в окремому вікні
echo Перевір вікно "TestNode NetworkCore"
echo.
pause
