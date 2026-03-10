# 🔨 Coffee Cat - Інструкція з збірки та розгортання

## 📦 Необхідні інструменти

### Для розробки:
- **Visual Studio 2022** (рекомендовано Community Edition або вище)
- **.NET 8.0 SDK**
- **Windows 10/11** (для Windows Service)

### Для користувачів:
- **Windows 10/11**
- **.NET 8.0 Runtime** (завантажиться автоматично при інсталяції)

---

## 🏗️ Компіляція проекту

### Варіант 1: Visual Studio (рекомендовано)

1. **Відкрийте Solution:**
   ```
   DocControlSolution.sln
   ```

2. **Оберіть конфігурацію:**
   - **Debug** - для тестування та розробки
   - **Release** - для production використання

3. **Зберіть проект:**
   - `Build → Build Solution` (Ctrl+Shift+B)
   - Або `Build → Rebuild Solution` (для чистої збірки)

4. **Перевірте результат:**
   - Debug: `bin\Debug\net8.0-windows\`
   - Release: `bin\Release\net8.0-windows\`

### Варіант 2: Командний рядок

```cmd
:: Перейдіть до папки Solution
cd Geocadastr_0_1\DocControlSolution

:: Debug збірка
dotnet build -c Debug

:: Release збірка
dotnet build -c Release

:: Publish (створення standalone версії)
dotnet publish -c Release -r win-x64 --self-contained false
```

---

## 📂 Структура скомпільованих файлів

Після збірки у вас буде:

```
DocControlService/bin/Release/
├── DocControlService.exe          # Windows Service
├── DocControlService.dll          # Основна бібліотека сервісу
├── DocControlNetworkCore.dll      # Мережевий модуль
├── DocControlService.Shared.dll   # Спільні моделі
└── ... (інші залежності)

DocControlUI/bin/Release/
├── DocControlUI.exe                # Головний додаток
├── DocControlService.Client.dll   # Клієнт для комунікації
├── DocControlService.Shared.dll   # Спільні моделі
├── MahApps.Metro.dll              # UI бібліотека
└── ... (інші залежності)
```

---

## 🚀 Розгортання на новому ПК

### Крок 1: Підготовка файлів

Скопіюйте **всі файли** з папок:
- `DocControlService\bin\Release\` → `C:\Program Files\CoffeeCat\Service\`
- `DocControlUI\bin\Release\` → `C:\Program Files\CoffeeCat\UI\`
- `StartDocControl.bat` → `C:\Program Files\CoffeeCat\`

### Крок 2: Встановлення Windows Service

Відкрийте **командний рядок від адміністратора** та виконайте:

```cmd
:: Створити сервіс
sc create DocControlService binPath= "C:\Program Files\CoffeeCat\Service\DocControlService.exe" start= auto

:: Встановити опис
sc description DocControlService "Coffee Cat Document Control Service - Управління документами та мережевими шарами"

:: Запустити сервіс
net start DocControlService
```

### Крок 3: Створення ярлика

1. Правий клік на робочому столі → "Створити" → "Ярлик"
2. Розташування:
   ```
   C:\Program Files\CoffeeCat\StartDocControl.bat
   ```
3. Назва: **Coffee Cat**
4. Змініть іконку на `C:\Program Files\CoffeeCat\UI\Assets\424.ico`

### Крок 4: Налаштування автозапуску (опціонально)

**Через Task Scheduler:**
1. Відкрийте `taskschd.msc`
2. Створіть новий Task: "Coffee Cat Startup"
3. Тригер: "At log on"
4. Дія: Запустити `StartDocControl.bat`
5. ✅ "Run with highest privileges"

---

## 🔥 Брандмауер Windows

### Автоматичне правило (рекомендовано):

При першому запуску Windows запитає дозвіл. Натисніть **"Дозволити"**.

### Ручне налаштування:

```cmd
:: Дозволити вхідні підключення для UI
netsh advfirewall firewall add rule name="Coffee Cat UI" dir=in action=allow program="C:\Program Files\CoffeeCat\UI\DocControlUI.exe" enable=yes

:: Дозволити вхідні підключення для Service
netsh advfirewall firewall add rule name="Coffee Cat Service" dir=in action=allow program="C:\Program Files\CoffeeCat\Service\DocControlService.exe" enable=yes

:: Відкрити TCP порт 5000
netsh advfirewall firewall add rule name="Coffee Cat TCP" dir=in action=allow protocol=TCP localport=5000

:: Відкрити UDP порт 5001
netsh advfirewall firewall add rule name="Coffee Cat UDP" dir=in action=allow protocol=UDP localport=5001
```

---

## 🧪 Тестування після встановлення

### 1. Перевірка Windows Service

```cmd
sc query DocControlService
```

Має показати:
```
STATE: 4 RUNNING
```

### 2. Запуск UI

Запустіть `StartDocControl.bat` або `DocControlUI.exe`

### 3. Перевірка мережевого з'єднання

1. Відкрийте вкладку "Мережа"
2. Має показати локальний пристрій у списку

### 4. Тест багатокористувацького режиму

На іншому ПК у тій самій мережі:
1. Встановіть Coffee Cat
2. Відкрийте вкладку "Мережа"
3. Має з'явитися перший ПК у списку

---

## 📊 Розгортання на мережі (багато ПК)

### Сценарій: 10 комп'ютерів в офісі

**На кожному ПК:**

1. **Встановіть Windows Service** (один раз):
   ```cmd
   sc create DocControlService binPath= "C:\CoffeeCat\Service\DocControlService.exe" start= auto
   net start DocControlService
   ```

2. **Створіть ярлик** для `StartDocControl.bat`

3. **Налаштуйте брандмауер:**
   - Дозвольте порти 5000-5001
   - Або виконайте скрипт вище

**Центральний сервер (опціонально):**

Якщо є виділений сервер для файлів:
1. Встановіть тільки **DocControlService**
2. Додайте директорії для роздачі
3. Надайте доступ всім клієнтським ПК

**Клієнтські ПК:**
1. Повна установка (Service + UI)
2. Підключаються до серверу через вкладку "Мережа"

---

## 🔧 Оновлення версії

### Процедура оновлення:

1. **Зупиніть сервіс:**
   ```cmd
   net stop DocControlService
   ```

2. **Закрийте UI** (якщо запущений)

3. **Замініть файли:**
   - Скопіюйте нові файли поверх старих
   - Або видаліть стару версію та встановіть нову

4. **Запустіть сервіс:**
   ```cmd
   net start DocControlService
   ```

5. **Запустіть UI** та перевірте версію у вікні запуску

### Резервне копіювання БД:

Перед оновленням скопіюйте:
```
C:\ProgramData\DocControl\DocControl.db
```

---

## 🐛 Troubleshooting при збірці

### Помилка: "Не знайдено .NET SDK"

**Рішення:**
```cmd
dotnet --version
```

Якщо команда не знайдена - встановіть .NET SDK:
https://dotnet.microsoft.com/download/dotnet/8.0

### Помилка: "Не вдалося завантажити MahApps.Metro"

**Рішення:**
```cmd
dotnet restore
```

### Помилка: "Access denied при створенні сервісу"

**Рішення:**
- Запустіть cmd від адміністратора
- Перевірте чи не запущений старий сервіс

### Помилка: "Не вдалося знайти Assets/424.ico"

**Рішення:**
- Переконайтеся що файл існує у `DocControlUI/Assets/424.ico`
- Або змініть шлях в XAML файлах

---

## 📋 Checklist розгортання

- [ ] .NET 8.0 Runtime встановлено
- [ ] Windows Service створено
- [ ] Windows Service запущено
- [ ] Брандмауер налаштовано
- [ ] UI запускається без помилок
- [ ] StartupWindow показує всі компоненти як "✅"
- [ ] Мережа працює (видно інші ПК)
- [ ] Тест багатокористувацького режиму пройдено

---

## 🎯 Production Ready Checklist

Перед використанням у production:

- [ ] Змінити інтервал автозбереження з 10 на 30+ секунд
- [ ] Протестувати на реальних пристроях
- [ ] Налаштувати резервне копіювання БД
- [ ] Створити інструкцію для користувачів
- [ ] Налаштувати логування помилок
- [ ] Протестувати відновлення після збоїв
- [ ] Перевірити роботу з 10+ одночасними користувачами

---

## 📞 Контакти

- **GitHub:** github.com/yourusername/DCS
- **Issues:** github.com/yourusername/DCS/issues
- **Документація:** См. ЗАПУСК.md

---

**Версія:** v0.11.0
**Дата:** 2024-12-30
**Build:** Release
