using DocControl.Maps.Core.Models;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace DocControl.Maps.Core.Data
{
    /// <summary>
    /// Репозиторій для роботи з кешем карт (SQLite)
    /// </summary>
    public class MapCacheRepository : IDisposable
    {
        private readonly string _dbPath;
        private readonly string _connectionString;

        public MapCacheRepository(string cachePath)
        {
            Directory.CreateDirectory(cachePath);
            _dbPath = Path.Combine(cachePath, "MapCache.db");
            _connectionString = $"Data Source={_dbPath}";

            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS MapTiles (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    x INTEGER NOT NULL,
                    y INTEGER NOT NULL,
                    zoom INTEGER NOT NULL,
                    provider TEXT NOT NULL,
                    imageData BLOB NOT NULL,
                    downloadedAt TEXT NOT NULL,
                    UNIQUE(x, y, zoom, provider)
                );
                
                CREATE INDEX IF NOT EXISTS idx_tiles_coords 
                ON MapTiles(x, y, zoom, provider);
                
                CREATE TABLE IF NOT EXISTS CachedRegions (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL,
                    minLat REAL NOT NULL,
                    minLon REAL NOT NULL,
                    maxLat REAL NOT NULL,
                    maxLon REAL NOT NULL,
                    minZoom INTEGER NOT NULL,
                    maxZoom INTEGER NOT NULL,
                    provider TEXT NOT NULL,
                    downloadedAt TEXT NOT NULL,
                    sizeBytes INTEGER NOT NULL,
                    tileCount INTEGER NOT NULL
                );
            ";
            cmd.ExecuteNonQuery();
        }

        public async Task<MapTile> GetTileAsync(int x, int y, int zoom, string provider)
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT imageData, downloadedAt 
                FROM MapTiles 
                WHERE x = @x AND y = @y AND zoom = @zoom AND provider = @provider
                LIMIT 1;";

            cmd.Parameters.AddWithValue("@x", x);
            cmd.Parameters.AddWithValue("@y", y);
            cmd.Parameters.AddWithValue("@zoom", zoom);
            cmd.Parameters.AddWithValue("@provider", provider);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new MapTile
                {
                    X = x,
                    Y = y,
                    Zoom = zoom,
                    Provider = provider,
                    ImageData = (byte[])reader["imageData"],
                    DownloadedAt = DateTime.Parse(reader["downloadedAt"].ToString()),
                    IsCached = true
                };
            }

            return null;
        }

        public async Task SaveTileAsync(MapTile tile)
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT OR REPLACE INTO MapTiles (x, y, zoom, provider, imageData, downloadedAt)
                VALUES (@x, @y, @zoom, @provider, @data, @time);";

            cmd.Parameters.AddWithValue("@x", tile.X);
            cmd.Parameters.AddWithValue("@y", tile.Y);
            cmd.Parameters.AddWithValue("@zoom", tile.Zoom);
            cmd.Parameters.AddWithValue("@provider", tile.Provider);
            cmd.Parameters.AddWithValue("@data", tile.ImageData);
            cmd.Parameters.AddWithValue("@time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<bool> IsTileCachedAsync(int x, int y, int zoom, string provider)
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COUNT(*) FROM MapTiles 
                WHERE x = @x AND y = @y AND zoom = @zoom AND provider = @provider;";

            cmd.Parameters.AddWithValue("@x", x);
            cmd.Parameters.AddWithValue("@y", y);
            cmd.Parameters.AddWithValue("@zoom", zoom);
            cmd.Parameters.AddWithValue("@provider", provider);

            var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            return count > 0;
        }

        public async Task<long> GetCacheSizeAsync()
        {
            if (!File.Exists(_dbPath))
                return 0;

            return await Task.Run(() => new FileInfo(_dbPath).Length);
        }

        public async Task ClearCacheAsync()
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM MapTiles;";
            await cmd.ExecuteNonQueryAsync();

            cmd.CommandText = "VACUUM;";
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task ClearOldCacheAsync(int daysOld)
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            var cutoffDate = DateTime.Now.AddDays(-daysOld);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                DELETE FROM MapTiles 
                WHERE downloadedAt < @cutoff;";
            cmd.Parameters.AddWithValue("@cutoff", cutoffDate.ToString("yyyy-MM-dd HH:mm:ss"));

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<int> SaveRegionAsync(CachedRegion region)
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO CachedRegions 
                (name, minLat, minLon, maxLat, maxLon, minZoom, maxZoom, provider, downloadedAt, sizeBytes, tileCount)
                VALUES (@name, @minLat, @minLon, @maxLat, @maxLon, @minZoom, @maxZoom, @provider, @time, @size, @count);";

            cmd.Parameters.AddWithValue("@name", region.Name);
            cmd.Parameters.AddWithValue("@minLat", region.MinLatitude);
            cmd.Parameters.AddWithValue("@minLon", region.MinLongitude);
            cmd.Parameters.AddWithValue("@maxLat", region.MaxLatitude);
            cmd.Parameters.AddWithValue("@maxLon", region.MaxLongitude);
            cmd.Parameters.AddWithValue("@minZoom", region.MinZoom);
            cmd.Parameters.AddWithValue("@maxZoom", region.MaxZoom);
            cmd.Parameters.AddWithValue("@provider", region.Provider);
            cmd.Parameters.AddWithValue("@time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@size", region.SizeBytes);
            cmd.Parameters.AddWithValue("@count", region.TileCount);

            await cmd.ExecuteNonQueryAsync();

            cmd.CommandText = "SELECT last_insert_rowid();";
            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        public async Task<List<CachedRegion>> GetCachedRegionsAsync()
        {
            var regions = new List<CachedRegion>();

            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM CachedRegions ORDER BY downloadedAt DESC;";

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                regions.Add(new CachedRegion
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    MinLatitude = reader.GetDouble(2),
                    MinLongitude = reader.GetDouble(3),
                    MaxLatitude = reader.GetDouble(4),
                    MaxLongitude = reader.GetDouble(5),
                    MinZoom = reader.GetInt32(6),
                    MaxZoom = reader.GetInt32(7),
                    Provider = reader.GetString(8),
                    DownloadedAt = DateTime.Parse(reader.GetString(9)),
                    SizeBytes = reader.GetInt64(10),
                    TileCount = reader.GetInt32(11)
                });
            }

            return regions;
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
        }
    }
}