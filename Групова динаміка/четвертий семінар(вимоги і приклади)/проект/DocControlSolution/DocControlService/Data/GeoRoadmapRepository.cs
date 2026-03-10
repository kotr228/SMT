using DocControlService.Models;
using DocControlService.Shared;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace DocControlService.Data
{
    public class GeoRoadmapRepository
    {
        private readonly DatabaseManager _db;

        public GeoRoadmapRepository(DatabaseManager db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        #region GeoRoadmap CRUD

        public int CreateGeoRoadmap(CreateGeoRoadmapRequest request, string createdBy = "System")
        {
            using var conn = _db.GetConnection();
            conn.Open();
            using var txn = conn.BeginTransaction();

            try
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = txn;
                cmd.CommandText = @"
                    INSERT INTO GeoRoadmaps 
                    (directoryId, name, description, createdBy, mapProvider, 
                     centerLatitude, centerLongitude, zoomLevel, createdAt, updatedAt) 
                    VALUES (@dirId, @name, @desc, @createdBy, @provider, @lat, @lng, @zoom, @time, @time);";

                cmd.Parameters.AddWithValue("@dirId", request.DirectoryId);
                cmd.Parameters.AddWithValue("@name", request.Name);
                cmd.Parameters.AddWithValue("@desc", request.Description ?? "");
                cmd.Parameters.AddWithValue("@createdBy", createdBy);
                cmd.Parameters.AddWithValue("@provider", request.MapProvider.ToString());
                cmd.Parameters.AddWithValue("@lat", request.CenterLatitude);
                cmd.Parameters.AddWithValue("@lng", request.CenterLongitude);
                cmd.Parameters.AddWithValue("@zoom", request.ZoomLevel);
                cmd.Parameters.AddWithValue("@time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                cmd.ExecuteNonQuery();

                cmd.CommandText = "SELECT last_insert_rowid();";
                int roadmapId = Convert.ToInt32(cmd.ExecuteScalar());

                txn.Commit();
                return roadmapId;
            }
            catch
            {
                txn.Rollback();
                throw;
            }
        }

        public List<GeoRoadmap> GetAllGeoRoadmaps()
        {
            var roadmaps = new List<GeoRoadmap>();
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, directoryId, name, description, createdAt, updatedAt, createdBy,
                       mapProvider, centerLatitude, centerLongitude, zoomLevel 
                FROM GeoRoadmaps 
                ORDER BY createdAt DESC;";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var roadmap = new GeoRoadmap
                {
                    Id = reader.GetInt32(0),
                    DirectoryId = reader.GetInt32(1),
                    Name = reader.GetString(2),
                    Description = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    CreatedAt = DateTime.Parse(reader.GetString(4)),
                    UpdatedAt = reader.IsDBNull(5) ? null : DateTime.Parse(reader.GetString(5)),
                    CreatedBy = reader.IsDBNull(6) ? "Unknown" : reader.GetString(6),
                    MapProvider = Enum.Parse<MapProvider>(reader.GetString(7)),
                    CenterLatitude = reader.GetDouble(8),
                    CenterLongitude = reader.GetDouble(9),
                    ZoomLevel = reader.GetInt32(10)
                };

                // Завантажуємо пов'язані дані
                roadmap.Nodes = GetNodesByRoadmap(roadmap.Id);
                roadmap.Routes = GetRoutesByRoadmap(roadmap.Id);
                roadmap.Areas = GetAreasByRoadmap(roadmap.Id);

                roadmaps.Add(roadmap);
            }

            return roadmaps;
        }

        public GeoRoadmap GetGeoRoadmapById(int id)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, directoryId, name, description, createdAt, updatedAt, createdBy,
                       mapProvider, centerLatitude, centerLongitude, zoomLevel 
                FROM GeoRoadmaps 
                WHERE id = @id;";
            cmd.Parameters.AddWithValue("@id", id);

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                var roadmap = new GeoRoadmap
                {
                    Id = reader.GetInt32(0),
                    DirectoryId = reader.GetInt32(1),
                    Name = reader.GetString(2),
                    Description = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    CreatedAt = DateTime.Parse(reader.GetString(4)),
                    UpdatedAt = reader.IsDBNull(5) ? null : DateTime.Parse(reader.GetString(5)),
                    CreatedBy = reader.IsDBNull(6) ? "Unknown" : reader.GetString(6),
                    MapProvider = Enum.Parse<MapProvider>(reader.GetString(7)),
                    CenterLatitude = reader.GetDouble(8),
                    CenterLongitude = reader.GetDouble(9),
                    ZoomLevel = reader.GetInt32(10),
                    Nodes = GetNodesByRoadmap(id),
                    Routes = GetRoutesByRoadmap(id),
                    Areas = GetAreasByRoadmap(id)
                };

                return roadmap;
            }

            return null;
        }

        public bool UpdateGeoRoadmap(GeoRoadmap roadmap)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE GeoRoadmaps 
                SET name = @name, description = @desc, mapProvider = @provider,
                    centerLatitude = @lat, centerLongitude = @lng, zoomLevel = @zoom,
                    updatedAt = @time
                WHERE id = @id;";

            cmd.Parameters.AddWithValue("@id", roadmap.Id);
            cmd.Parameters.AddWithValue("@name", roadmap.Name);
            cmd.Parameters.AddWithValue("@desc", roadmap.Description ?? "");
            cmd.Parameters.AddWithValue("@provider", roadmap.MapProvider.ToString());
            cmd.Parameters.AddWithValue("@lat", roadmap.CenterLatitude);
            cmd.Parameters.AddWithValue("@lng", roadmap.CenterLongitude);
            cmd.Parameters.AddWithValue("@zoom", roadmap.ZoomLevel);
            cmd.Parameters.AddWithValue("@time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            return cmd.ExecuteNonQuery() > 0;
        }

        public bool DeleteGeoRoadmap(int id)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            // SQLite автоматично видалить пов'язані записи через ON DELETE CASCADE
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM GeoRoadmaps WHERE id = @id;";
            cmd.Parameters.AddWithValue("@id", id);

            return cmd.ExecuteNonQuery() > 0;
        }

        #endregion

        #region Nodes

        public int AddNode(GeoRoadmapNode node)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO GeoRoadmapNodes 
                (geoRoadmapId, title, description, latitude, longitude, address, 
                 nodeType, iconName, color, eventDate, relatedFiles, orderIndex)
                VALUES (@roadmapId, @title, @desc, @lat, @lng, @addr, @type, @icon, @color, @date, @files, @order);";

            cmd.Parameters.AddWithValue("@roadmapId", node.GeoRoadmapId);
            cmd.Parameters.AddWithValue("@title", node.Title);
            cmd.Parameters.AddWithValue("@desc", node.Description ?? "");
            cmd.Parameters.AddWithValue("@lat", node.Latitude);
            cmd.Parameters.AddWithValue("@lng", node.Longitude);
            cmd.Parameters.AddWithValue("@addr", node.Address ?? "");
            cmd.Parameters.AddWithValue("@type", node.Type.ToString());
            cmd.Parameters.AddWithValue("@icon", node.IconName ?? "");
            cmd.Parameters.AddWithValue("@color", node.Color ?? "#2196F3");
            cmd.Parameters.AddWithValue("@date", node.EventDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@files", node.RelatedFiles ?? "");
            cmd.Parameters.AddWithValue("@order", node.OrderIndex);

            cmd.ExecuteNonQuery();

            cmd.CommandText = "SELECT last_insert_rowid();";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public List<GeoRoadmapNode> GetNodesByRoadmap(int roadmapId)
        {
            var nodes = new List<GeoRoadmapNode>();
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, geoRoadmapId, title, description, latitude, longitude, address,
                       nodeType, iconName, color, eventDate, relatedFiles, orderIndex
                FROM GeoRoadmapNodes 
                WHERE geoRoadmapId = @id 
                ORDER BY orderIndex;";
            cmd.Parameters.AddWithValue("@id", roadmapId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                nodes.Add(new GeoRoadmapNode
                {
                    Id = reader.GetInt32(0),
                    GeoRoadmapId = reader.GetInt32(1),
                    Title = reader.GetString(2),
                    Description = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    Latitude = reader.GetDouble(4),
                    Longitude = reader.GetDouble(5),
                    Address = reader.IsDBNull(6) ? "" : reader.GetString(6),
                    Type = Enum.Parse<NodeType>(reader.GetString(7)),
                    IconName = reader.IsDBNull(8) ? "" : reader.GetString(8),
                    Color = reader.IsDBNull(9) ? "#2196F3" : reader.GetString(9),
                    EventDate = reader.IsDBNull(10) ? null : DateTime.Parse(reader.GetString(10)),
                    RelatedFiles = reader.IsDBNull(11) ? "" : reader.GetString(11),
                    OrderIndex = reader.GetInt32(12)
                });
            }

            return nodes;
        }

        public bool UpdateNode(GeoRoadmapNode node)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE GeoRoadmapNodes 
                SET title = @title, description = @desc, latitude = @lat, longitude = @lng,
                    address = @addr, nodeType = @type, iconName = @icon, color = @color,
                    eventDate = @date, relatedFiles = @files, orderIndex = @order
                WHERE id = @id;";

            cmd.Parameters.AddWithValue("@id", node.Id);
            cmd.Parameters.AddWithValue("@title", node.Title);
            cmd.Parameters.AddWithValue("@desc", node.Description ?? "");
            cmd.Parameters.AddWithValue("@lat", node.Latitude);
            cmd.Parameters.AddWithValue("@lng", node.Longitude);
            cmd.Parameters.AddWithValue("@addr", node.Address ?? "");
            cmd.Parameters.AddWithValue("@type", node.Type.ToString());
            cmd.Parameters.AddWithValue("@icon", node.IconName ?? "");
            cmd.Parameters.AddWithValue("@color", node.Color ?? "#2196F3");
            cmd.Parameters.AddWithValue("@date", node.EventDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@files", node.RelatedFiles ?? "");
            cmd.Parameters.AddWithValue("@order", node.OrderIndex);

            return cmd.ExecuteNonQuery() > 0;
        }

        public bool DeleteNode(int nodeId)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM GeoRoadmapNodes WHERE id = @id;";
            cmd.Parameters.AddWithValue("@id", nodeId);

            return cmd.ExecuteNonQuery() > 0;
        }

        #endregion

        #region Routes

        public int AddRoute(GeoRoadmapRoute route)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO GeoRoadmapRoutes 
                (geoRoadmapId, fromNodeId, toNodeId, label, color, style, strokeWidth)
                VALUES (@roadmapId, @fromId, @toId, @label, @color, @style, @width);";

            cmd.Parameters.AddWithValue("@roadmapId", route.GeoRoadmapId);
            cmd.Parameters.AddWithValue("@fromId", route.FromNodeId);
            cmd.Parameters.AddWithValue("@toId", route.ToNodeId);
            cmd.Parameters.AddWithValue("@label", route.Label ?? "");
            cmd.Parameters.AddWithValue("@color", route.Color ?? "#2196F3");
            cmd.Parameters.AddWithValue("@style", route.Style.ToString());
            cmd.Parameters.AddWithValue("@width", route.StrokeWidth);

            cmd.ExecuteNonQuery();

            cmd.CommandText = "SELECT last_insert_rowid();";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public List<GeoRoadmapRoute> GetRoutesByRoadmap(int roadmapId)
        {
            var routes = new List<GeoRoadmapRoute>();
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, geoRoadmapId, fromNodeId, toNodeId, label, color, style, strokeWidth
                FROM GeoRoadmapRoutes 
                WHERE geoRoadmapId = @id;";
            cmd.Parameters.AddWithValue("@id", roadmapId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                routes.Add(new GeoRoadmapRoute
                {
                    Id = reader.GetInt32(0),
                    GeoRoadmapId = reader.GetInt32(1),
                    FromNodeId = reader.GetInt32(2),
                    ToNodeId = reader.GetInt32(3),
                    Label = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    Color = reader.IsDBNull(5) ? "#2196F3" : reader.GetString(5),
                    Style = Enum.Parse<RouteStyle>(reader.GetString(6)),
                    StrokeWidth = reader.GetInt32(7)
                });
            }

            return routes;
        }

        public bool DeleteRoute(int routeId)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM GeoRoadmapRoutes WHERE id = @id;";
            cmd.Parameters.AddWithValue("@id", routeId);

            return cmd.ExecuteNonQuery() > 0;
        }

        #endregion

        #region Areas

        public int AddArea(GeoRoadmapArea area)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO GeoRoadmapAreas 
                (geoRoadmapId, name, description, polygonCoordinates, fillColor, strokeColor, opacity)
                VALUES (@roadmapId, @name, @desc, @coords, @fill, @stroke, @opacity);";

            cmd.Parameters.AddWithValue("@roadmapId", area.GeoRoadmapId);
            cmd.Parameters.AddWithValue("@name", area.Name);
            cmd.Parameters.AddWithValue("@desc", area.Description ?? "");
            cmd.Parameters.AddWithValue("@coords", area.PolygonCoordinates);
            cmd.Parameters.AddWithValue("@fill", area.FillColor ?? "#2196F3");
            cmd.Parameters.AddWithValue("@stroke", area.StrokeColor ?? "#1976D2");
            cmd.Parameters.AddWithValue("@opacity", area.Opacity);

            cmd.ExecuteNonQuery();

            cmd.CommandText = "SELECT last_insert_rowid();";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public List<GeoRoadmapArea> GetAreasByRoadmap(int roadmapId)
        {
            var areas = new List<GeoRoadmapArea>();
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, geoRoadmapId, name, description, polygonCoordinates, 
                       fillColor, strokeColor, opacity
                FROM GeoRoadmapAreas 
                WHERE geoRoadmapId = @id;";
            cmd.Parameters.AddWithValue("@id", roadmapId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                areas.Add(new GeoRoadmapArea
                {
                    Id = reader.GetInt32(0),
                    GeoRoadmapId = reader.GetInt32(1),
                    Name = reader.GetString(2),
                    Description = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    PolygonCoordinates = reader.GetString(4),
                    FillColor = reader.IsDBNull(5) ? "#2196F3" : reader.GetString(5),
                    StrokeColor = reader.IsDBNull(6) ? "#1976D2" : reader.GetString(6),
                    Opacity = reader.GetDouble(7)
                });
            }

            return areas;
        }

        public bool DeleteArea(int areaId)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM GeoRoadmapAreas WHERE id = @id;";
            cmd.Parameters.AddWithValue("@id", areaId);

            return cmd.ExecuteNonQuery() > 0;
        }

        #endregion

        #region Templates

        public List<GeoRoadmapTemplate> GetAllTemplates()
        {
            var templates = new List<GeoRoadmapTemplate>();
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, name, description, category, templateJson, isBuiltIn, createdAt
                FROM GeoRoadmapTemplates 
                ORDER BY isBuiltIn DESC, name;";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                templates.Add(new GeoRoadmapTemplate
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    Description = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    Category = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    TemplateJson = reader.GetString(4),
                    IsBuiltIn = reader.GetInt32(5) == 1,
                    CreatedAt = DateTime.Parse(reader.GetString(6))
                });
            }

            return templates;
        }

        public int SaveAsTemplate(string name, string description, string category, GeoRoadmap roadmap)
        {
            using var conn = _db.GetConnection();
            conn.Open();

            var templateData = new
            {
                nodes = roadmap.Nodes,
                routes = roadmap.Routes,
                areas = roadmap.Areas,
                mapProvider = roadmap.MapProvider.ToString(),
                zoomLevel = roadmap.ZoomLevel
            };

            string templateJson = JsonSerializer.Serialize(templateData);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO GeoRoadmapTemplates 
                (name, description, category, templateJson, isBuiltIn, createdAt)
                VALUES (@name, @desc, @cat, @json, 0, @time);";

            cmd.Parameters.AddWithValue("@name", name);
            cmd.Parameters.AddWithValue("@desc", description ?? "");
            cmd.Parameters.AddWithValue("@cat", category ?? "Користувацькі");
            cmd.Parameters.AddWithValue("@json", templateJson);
            cmd.Parameters.AddWithValue("@time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            cmd.ExecuteNonQuery();

            cmd.CommandText = "SELECT last_insert_rowid();";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        #endregion
    }
}