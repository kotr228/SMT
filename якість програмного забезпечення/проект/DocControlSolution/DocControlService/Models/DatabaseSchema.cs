namespace DocControlService.Models
{
    public static class DatabaseSchema
    {
        public static readonly string[] CreateTables = new[]
        {
            // ========== ІСНУЮЧІ ТАБЛИЦІ v0.1-0.2 ==========
            
            @"CREATE TABLE IF NOT EXISTS directory (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name VARCHAR(60) NOT NULL,
                Browse VARCHAR(200) NOT NULL
            );",

            @"CREATE TABLE IF NOT EXISTS Objects (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name VARCHAR(150) NOT NULL,
                inBrowse VARCHAR(150),
                idDirectory INT,
                FOREIGN KEY(idDirectory) REFERENCES directory(id)
            );",

            @"CREATE TABLE IF NOT EXISTS TypeFiles (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                extention VARCHAR(45) NOT NULL,
                TypeName VARCHAR(150) NOT NULL
            );",

            @"CREATE TABLE IF NOT EXISTS Folders (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                NameFolder VARCHAR(150) NOT NULL,
                inBrowse VARCHAR(150),
                idObject INT,
                idDirectory INT,
                FOREIGN KEY(idObject) REFERENCES Objects(id),
                FOREIGN KEY(idDirectory) REFERENCES directory(id)
            );",

            @"CREATE TABLE IF NOT EXISTS Files (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                NameFile VARCHAR(150) NOT NULL,
                inBrowse VARCHAR(150),
                idTypeFile INT,
                idFolder INT,
                idObject INT,
                idDirectory INT,
                FOREIGN KEY(idTypeFile) REFERENCES TypeFiles(id),
                FOREIGN KEY(idFolder) REFERENCES Folders(id),
                FOREIGN KEY(idObject) REFERENCES Objects(id),
                FOREIGN KEY(idDirectory) REFERENCES directory(id)
            );",

            @"CREATE TABLE IF NOT EXISTS DirectoryAccess (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                idDirectory INT NOT NULL,
                IsShared INTEGER NOT NULL DEFAULT 1,
                UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY(idDirectory) REFERENCES directory(id)
            );",

            @"CREATE TABLE IF NOT EXISTS Devises (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT,
                Acces INTEGER NOT NULL DEFAULT 0
            );",

            @"CREATE TABLE IF NOT EXISTS NetworkAccesDirectory (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                idDyrectory INTEGER,
                Status INTEGER NOT NULL DEFAULT 0,
                idDevises INTEGER,
                FOREIGN KEY(idDyrectory) REFERENCES directory(id),
                FOREIGN KEY(idDevises) REFERENCES Devises(id)
            );",

            @"CREATE TABLE IF NOT EXISTS CommitStatusLog (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                directoryId INTEGER NOT NULL,
                directoryPath TEXT NOT NULL,
                status TEXT NOT NULL,
                message TEXT,
                timestamp DATETIME DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY(directoryId) REFERENCES directory(id)
            );",

            @"CREATE TABLE IF NOT EXISTS ErrorLog (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                errorType TEXT NOT NULL,
                errorMessage TEXT NOT NULL,
                userFriendlyMessage TEXT NOT NULL,
                stackTrace TEXT,
                timestamp DATETIME DEFAULT CURRENT_TIMESTAMP,
                isResolved INTEGER DEFAULT 0
            );",

            @"CREATE TABLE IF NOT EXISTS AppSettings (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                settingKey TEXT NOT NULL UNIQUE,
                settingValue TEXT NOT NULL,
                description TEXT,
                updatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
            );",

            @"CREATE TABLE IF NOT EXISTS Roadmaps (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                directoryId INTEGER NOT NULL,
                name TEXT NOT NULL,
                description TEXT,
                createdAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY(directoryId) REFERENCES directory(id)
            );",

            @"CREATE TABLE IF NOT EXISTS RoadmapEvents (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                roadmapId INTEGER NOT NULL,
                title TEXT NOT NULL,
                description TEXT,
                eventDate DATETIME NOT NULL,
                eventType TEXT NOT NULL,
                filePath TEXT,
                category TEXT,
                FOREIGN KEY(roadmapId) REFERENCES Roadmaps(id) ON DELETE CASCADE
            );",

            @"CREATE TABLE IF NOT EXISTS ExternalServices (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                serviceType TEXT NOT NULL,
                url TEXT NOT NULL,
                apiKey TEXT,
                isActive INTEGER DEFAULT 1,
                lastUsed DATETIME,
                createdAt DATETIME DEFAULT CURRENT_TIMESTAMP
            );",
            
            // ========== НОВІ ТАБЛИЦІ v0.3 - ГЕОДОРОЖНІ КАРТИ ==========
            
            @"CREATE TABLE IF NOT EXISTS GeoRoadmaps (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                directoryId INTEGER NOT NULL,
                name TEXT NOT NULL,
                description TEXT,
                createdAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                updatedAt DATETIME,
                createdBy TEXT,
                mapProvider TEXT NOT NULL DEFAULT 'OpenStreetMap',
                centerLatitude REAL NOT NULL DEFAULT 0.0,
                centerLongitude REAL NOT NULL DEFAULT 0.0,
                zoomLevel INTEGER NOT NULL DEFAULT 10,
                FOREIGN KEY(directoryId) REFERENCES directory(id) ON DELETE CASCADE
            );",

            @"CREATE TABLE IF NOT EXISTS GeoRoadmapNodes (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                geoRoadmapId INTEGER NOT NULL,
                title TEXT NOT NULL,
                description TEXT,
                latitude REAL NOT NULL,
                longitude REAL NOT NULL,
                address TEXT,
                nodeType TEXT NOT NULL,
                iconName TEXT,
                color TEXT DEFAULT '#2196F3',
                eventDate DATETIME,
                relatedFiles TEXT,
                orderIndex INTEGER DEFAULT 0,
                FOREIGN KEY(geoRoadmapId) REFERENCES GeoRoadmaps(id) ON DELETE CASCADE
            );",

            @"CREATE TABLE IF NOT EXISTS GeoRoadmapRoutes (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                geoRoadmapId INTEGER NOT NULL,
                fromNodeId INTEGER NOT NULL,
                toNodeId INTEGER NOT NULL,
                label TEXT,
                color TEXT DEFAULT '#2196F3',
                style TEXT DEFAULT 'Solid',
                strokeWidth INTEGER DEFAULT 2,
                FOREIGN KEY(geoRoadmapId) REFERENCES GeoRoadmaps(id) ON DELETE CASCADE,
                FOREIGN KEY(fromNodeId) REFERENCES GeoRoadmapNodes(id) ON DELETE CASCADE,
                FOREIGN KEY(toNodeId) REFERENCES GeoRoadmapNodes(id) ON DELETE CASCADE
            );",

            @"CREATE TABLE IF NOT EXISTS GeoRoadmapAreas (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                geoRoadmapId INTEGER NOT NULL,
                name TEXT NOT NULL,
                description TEXT,
                polygonCoordinates TEXT NOT NULL,
                fillColor TEXT DEFAULT '#2196F3',
                strokeColor TEXT DEFAULT '#1976D2',
                opacity REAL DEFAULT 0.3,
                FOREIGN KEY(geoRoadmapId) REFERENCES GeoRoadmaps(id) ON DELETE CASCADE
            );",

            @"CREATE TABLE IF NOT EXISTS GeoRoadmapTemplates (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                description TEXT,
                category TEXT,
                templateJson TEXT NOT NULL,
                isBuiltIn INTEGER DEFAULT 0,
                createdAt DATETIME DEFAULT CURRENT_TIMESTAMP
            );",

            @"CREATE TABLE IF NOT EXISTS IpFilterRules (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                ruleName TEXT NOT NULL,
                ipAddress TEXT NOT NULL,
                action TEXT NOT NULL,
                isEnabled INTEGER DEFAULT 1,
                description TEXT,
                createdAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                directoryId INTEGER,
                geoRoadmapId INTEGER,
                FOREIGN KEY(directoryId) REFERENCES directory(id) ON DELETE CASCADE,
                FOREIGN KEY(geoRoadmapId) REFERENCES GeoRoadmaps(id) ON DELETE CASCADE
            );",
            
            // Індекси для швидкого пошуку
            @"CREATE INDEX IF NOT EXISTS idx_georoadmap_directory 
              ON GeoRoadmaps(directoryId);",

            @"CREATE INDEX IF NOT EXISTS idx_geonode_roadmap 
              ON GeoRoadmapNodes(geoRoadmapId);",

            @"CREATE INDEX IF NOT EXISTS idx_georoute_roadmap 
              ON GeoRoadmapRoutes(geoRoadmapId);",

            @"CREATE INDEX IF NOT EXISTS idx_geoarea_roadmap 
              ON GeoRoadmapAreas(geoRoadmapId);",

            @"CREATE INDEX IF NOT EXISTS idx_ipfilter_directory 
              ON IpFilterRules(directoryId);",

            @"CREATE INDEX IF NOT EXISTS idx_ipfilter_georoadmap
              ON IpFilterRules(geoRoadmapId);"
        };

        // Вбудовані шаблони геокарт
        public static readonly string[] InsertDefaultTemplates = new[]
        {
            @"INSERT OR IGNORE INTO GeoRoadmapTemplates (id, name, description, category, templateJson, isBuiltIn) 
              VALUES (1, 'Будівельний проект', 'Шаблон для будівельних об''єктів', 'Будівництво', 
              '{""nodes"":[{""title"":""Початок робіт"",""type"":""Milestone""},{""title"":""Офіс"",""type"":""Office""},{""title"":""Об''єкт"",""type"":""Site""}]}', 1);",

            @"INSERT OR IGNORE INTO GeoRoadmapTemplates (id, name, description, category, templateJson, isBuiltIn) 
              VALUES (2, 'Логістичний маршрут', 'Шаблон для планування доставки', 'Логістика', 
              '{""nodes"":[{""title"":""Склад"",""type"":""Location""},{""title"":""Точка доставки"",""type"":""Checkpoint""}]}', 1);",

            @"INSERT OR IGNORE INTO GeoRoadmapTemplates (id, name, description, category, templateJson, isBuiltIn) 
              VALUES (3, 'Інспекційні візити', 'Шаблон для планування інспекцій', 'Інспекції', 
              '{""nodes"":[{""title"":""Контрольна точка"",""type"":""Checkpoint""},{""title"":""Зустріч"",""type"":""Meeting""}]}', 1);"
        };
    }
}