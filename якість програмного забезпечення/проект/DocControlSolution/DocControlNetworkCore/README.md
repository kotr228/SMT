# DocControl Network Core

## Опис

**DocControl Network Core** — це мережеве ядро для роботи з локальною мережею, яке забезпечує автоматичне виявлення та обмін файлами між комп'ютерами в локальній мережі без необхідності ручної конфігурації.

## Основні можливості

### 1. **Self-Identity (Самоідентифікація)**
- ✅ Автоматична генерація унікального `GUID` при першому запуску
- ✅ Формування "паспорта" користувача з метаданими (ім'я, комп'ютер, IP, порт)
- ✅ Автоматичний вибір вільного TCP/UDP порту або використання вказаного
- ✅ Збереження ідентифікатора між запусками

### 2. **Discovery Service (Виявлення вузлів)**
- ✅ UDP Broadcast для автоматичного виявлення інших вузлів у мережі
- ✅ UDP Listener для прийому повідомлень від інших вузлів
- ✅ Автоматична фільтрація власних пакетів
- ✅ Періодичний Heartbeat (за замовчуванням кожні 10 секунд)

### 3. **Peer Registry (Реєстр вузлів)**
- ✅ Динамічне управління списком активних вузлів
- ✅ Автоматичне виявлення відключених вузлів (timeout 30 секунд)
- ✅ Події додавання/видалення вузлів для інтеграції з UI
- ✅ Thread-safe операції з використанням `ConcurrentDictionary`

### 4. **Command Layer (Обмін командами)**
- ✅ TCP сервер для прийому команд від інших вузлів
- ✅ Підтримка команд:
  - `GetFileList` — отримання списку файлів у директорії
  - `GetFileMeta` — отримання метаданих файлу
  - `Ping` — перевірка доступності
  - `Heartbeat` — підтримка з'єднання
- ✅ JSON серіалізація команд та відповідей
- ✅ Асинхронна обробка запитів

### 5. **File Transfer (Передача файлів)**
- ✅ Потокова передача файлів (Stream-to-Stream)
- ✅ Підтримка великих файлів без завантаження в пам'ять
- ✅ Події прогресу завантаження/відправки
- ✅ Підтримка докачування (offset)
- ✅ Автоматичне створення директорій при завантаженні

### 6. **Security (Безпека)**
- ✅ Валідація шляхів (запобігання виходу за межі дозволеної директорії)
- ✅ Виявлення небезпечних паттернів (`..`, `~`, абсолютні шляхи)
- ✅ IP Whitelist (опціонально)
- ✅ Логування спроб несанкціонованого доступу
- ✅ Валідація розширень файлів та розмірів

## Архітектура

```
DocControlNetworkCore/
├── Models/
│   ├── PeerIdentity.cs          # Модель ідентифікації вузла
│   └── NetworkCommand.cs        # Моделі команд та відповідей
├── Services/
│   ├── SelfIdentityService.cs   # Ідентифікація та конфігурація
│   ├── DiscoveryService.cs      # Виявлення вузлів (UDP)
│   ├── PeerRegistryService.cs   # Реєстр активних вузлів
│   ├── CommandLayerService.cs   # Обмін командами (TCP)
│   ├── FileTransferService.cs   # Передача файлів
│   └── SecurityService.cs       # Безпека та валідація
├── NetworkCoreService.cs        # Головний Windows Service
└── Program.cs                   # Точка входу
```

## Використання

### Запуск у Debug режимі (консоль)

```bash
DocControlNetworkCore.exe --debug
```

або

```bash
DocControlNetworkCore.exe -c
```

### Команди в Debug режимі

- **Q** — Зупинити сервіс
- **S** — Показати статус
- **P** — Показати активні вузли
- **B** — Відправити broadcast

### Встановлення як Windows Service

```bash
sc create DocControlNetworkCore binPath= "C:\Path\To\DocControlNetworkCore.exe"
sc start DocControlNetworkCore
```

## Конфігурація

### Налаштування портів

За замовчуванням:
- **TCP Port**: 8000+ (автоматично шукає вільний)
- **UDP Port**: 9000+ (автоматично шукає вільний)

Можна вказати власні порти при ініціалізації:

```csharp
var identity = identityService.GetOrCreateIdentity(
    preferredTcpPort: 8080,
    preferredUdpPort: 9090
);
```

### Дозволена директорія

За замовчуванням: `C:\SharedFiles`

Змінити можна в `NetworkCoreService.cs`:

```csharp
private string _sharedDirectory = @"D:\MySharedFolder";
```

## Протокол

### Discovery (UDP Broadcast)

Формат повідомлення:
```json
{
  "InstanceId": "guid",
  "UserName": "string",
  "MachineName": "string",
  "IpAddress": "string",
  "TcpPort": 8000,
  "UdpPort": 9000,
  "ProtocolVersion": "1.0"
}
```

### Commands (TCP)

Запит:
```json
{
  "Type": "GetFileList",
  "RequestId": "guid",
  "Payload": "{\"DirectoryPath\":\"/\",\"Filter\":\"*.*\"}",
  "SenderId": "guid"
}
```

Відповідь:
```json
{
  "RequestId": "guid",
  "Success": true,
  "Data": "[{\"FileName\":\"test.txt\",\"Size\":1024,...}]"
}
```

## Безпека

### Рекомендації

1. **Налаштування Firewall**
   - Дозволити UDP порт для Discovery
   - Дозволити TCP порт для Commands
   - Обмежити доступ до локальної мережі

2. **Whitelist**
   - Увімкнути IP whitelist для критичних систем
   - Регулярно переглядати список дозволених IP

3. **Валідація шляхів**
   - Ніколи не змінювати базову директорію на системні папки
   - Регулярно переглядати логи безпеки

## Вимоги

- **.NET 8.0** або новіше
- **Windows** (для Windows Service функціоналу)
- **Права адміністратора** (для роботи з мережею та службами)

## Залежності

- `Microsoft.Data.Sqlite` (9.0.9)
- `System.ServiceProcess.ServiceController` (7.0.0)

## Ліцензія

Проект є частиною **DocControl Solution**.

## Автор

Розроблено для системи **DocControl**.

## Changelog

### v1.0.0 (2025-12-20)
- ✅ Реалізовано Self-Identity
- ✅ Реалізовано Discovery Service (UDP)
- ✅ Реалізовано Peer Registry
- ✅ Реалізовано Command Layer (TCP)
- ✅ Реалізовано File Transfer
- ✅ Реалізовано Security
- ✅ Інтеграція всіх компонентів
- ✅ Підтримка Debug режиму
- ✅ Підтримка Windows Service
