using DocControlService.Models;
using DocControlService.Shared;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;

namespace DocControlService.Data
{
    public class RoadmapRepository
    {
        private readonly DatabaseManager _db;

        public RoadmapRepository(DatabaseManager db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public int CreateRoadmap(int directoryId, string name, string description, List<RoadmapEvent> events)
        {
            using var conn = _db.GetConnection();
            conn.Open();
            using var txn = conn.BeginTransaction();

            try
            {
                // Створюємо roadmap
                using var cmd = conn.CreateCommand();
                cmd.Transaction = txn;
                cmd.CommandText = @"
                    INSERT INTO Roadmaps (directoryId, name, description, createdAt) 
                    VALUES (@dirId, @name, @desc, @time);";
                cmd.Parameters.AddWithValue("@dirId", directoryId);
                cmd.Parameters.AddWithValue("@name", name);
                cmd.Parameters.AddWithValue("@desc", description ?? "");
                cmd.Parameters.AddWithValue("@time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.ExecuteNonQuery();

                cmd.CommandText = "SELECT last_insert_rowid();";
                int roadmapId = Convert.ToInt32(cmd.ExecuteScalar());

                // Додаємо події
                foreach (var evt in events)
                {
                    using var evtCmd = conn.CreateCommand();
                    evtCmd.Transaction = txn;
                    evtCmd.CommandText = @"
                        INSERT INTO RoadmapEvents (roadmapId, title, description, eventDate, eventType, filePath, category) 
                        VALUES (@roadmapId, @title, @desc, @date, @type, @path, @cat);";
                    evtCmd.Parameters.AddWithValue("@roadmapId", roadmapId);
                    evtCmd.Parameters.AddWithValue("@title", evt.Title);
                    evtCmd.Parameters.AddWithValue("@desc", evt.Description ?? "");
                    evtCmd.Parameters.AddWithValue("@date", evt.EventDate.ToString("yyyy-MM-dd HH:mm:ss"));
                    evtCmd.Parameters.AddWithValue("@type", evt.EventType);
                    evtCmd.Parameters.AddWithValue("@path", evt.FilePath ?? "");
                    evtCmd.Parameters.AddWithValue("@cat", evt.Category ?? "");
                    evtCmd.ExecuteNonQuery();
                }

                txn.Commit();
                return roadmapId;
            }
            catch
            {
                txn.Rollback();
                throw;
            }
        }

        public List<Roadmap> GetAllRoadmaps()
        {
            var roadmaps = new List<Roadmap>();
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, directoryId, name, description, createdAt FROM Roadmaps ORDER BY createdAt DESC;";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var roadmap = new Roadmap
                {
                    Id = reader.GetInt32(0),
                    DirectoryId = reader.GetInt32(1),
                    Name = reader.GetString(2),
                    Description = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    CreatedAt = DateTime.Parse(reader.GetString(4))
                };
                roadmaps.Add(roadmap);
            }

            // Завантажуємо події для кожної roadmap
            foreach (var roadmap in roadmaps)
            {
                roadmap.Events = GetRoadmapEvents(roadmap.Id);
            }

            return roadmaps;
        }

        public Roadmap GetRoadmapById(int id)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, directoryId, name, description, createdAt FROM Roadmaps WHERE id = @id;";
            cmd.Parameters.AddWithValue("@id", id);

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                var roadmap = new Roadmap
                {
                    Id = reader.GetInt32(0),
                    DirectoryId = reader.GetInt32(1),
                    Name = reader.GetString(2),
                    Description = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    CreatedAt = DateTime.Parse(reader.GetString(4)),
                    Events = GetRoadmapEvents(reader.GetInt32(0))
                };
                return roadmap;
            }

            return null;
        }

        private List<RoadmapEvent> GetRoadmapEvents(int roadmapId)
        {
            var events = new List<RoadmapEvent>();
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, title, description, eventDate, eventType, filePath, category 
                FROM RoadmapEvents 
                WHERE roadmapId = @id 
                ORDER BY eventDate;";
            cmd.Parameters.AddWithValue("@id", roadmapId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                events.Add(new RoadmapEvent
                {
                    Id = reader.GetInt32(0),
                    Title = reader.GetString(1),
                    Description = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    EventDate = DateTime.Parse(reader.GetString(3)),
                    EventType = reader.GetString(4),
                    FilePath = reader.IsDBNull(5) ? "" : reader.GetString(5),
                    Category = reader.IsDBNull(6) ? "" : reader.GetString(6)
                });
            }

            return events;
        }

        public bool DeleteRoadmap(int id)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            // SQLite автоматично видалить події через ON DELETE CASCADE
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Roadmaps WHERE id = @id;";
            cmd.Parameters.AddWithValue("@id", id);

            return cmd.ExecuteNonQuery() > 0;
        }
    }
}