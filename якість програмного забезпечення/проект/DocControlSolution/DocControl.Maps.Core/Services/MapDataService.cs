using DocControl.Maps.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DocControl.Maps.Core.Services
{
    /// <summary>
    /// Сервіс для роботи з геооб'єктами (точки, маршрути, області)
    /// </summary>
    public class MapDataService
    {
        private readonly List<GeoPoint> _points = new List<GeoPoint>();
        private readonly List<GeoRoute> _routes = new List<GeoRoute>();
        private readonly List<GeoArea> _areas = new List<GeoArea>();

        public event EventHandler DataChanged;

        #region Points

        public void AddPoint(GeoPoint point)
        {
            if (point == null) throw new ArgumentNullException(nameof(point));

            _points.Add(point);
            DataChanged?.Invoke(this, EventArgs.Empty);
        }

        public void RemovePoint(GeoPoint point)
        {
            _points.Remove(point);
            DataChanged?.Invoke(this, EventArgs.Empty);
        }

        public IReadOnlyList<GeoPoint> GetAllPoints() => _points.AsReadOnly();

        #endregion

        #region Routes

        public void AddRoute(GeoRoute route)
        {
            if (route == null) throw new ArgumentNullException(nameof(route));

            route.CalculateDistance();
            _routes.Add(route);
            DataChanged?.Invoke(this, EventArgs.Empty);
        }

        public void RemoveRoute(GeoRoute route)
        {
            _routes.Remove(route);
            DataChanged?.Invoke(this, EventArgs.Empty);
        }

        public IReadOnlyList<GeoRoute> GetAllRoutes() => _routes.AsReadOnly();

        public GeoRoute CreateRouteFromPoints(List<GeoPoint> points, string name = null)
        {
            var route = new GeoRoute
            {
                Name = name ?? $"Route {_routes.Count + 1}",
                Points = new List<GeoPoint>(points)
            };

            route.CalculateDistance();
            return route;
        }

        public GeoRoute OptimizeRoute(GeoRoute route)
        {
            if (route == null || route.Points.Count < 3)
                return route;

            // Nearest neighbor algorithm
            var optimized = new List<GeoPoint> { route.Points[0] };
            var remaining = new List<GeoPoint>(route.Points.Skip(1));

            while (remaining.Count > 0)
            {
                var current = optimized.Last();
                GeoPoint nearest = null;
                double minDist = double.MaxValue;

                foreach (var point in remaining)
                {
                    var dist = CalculateDistance(current, point);
                    if (dist < minDist)
                    {
                        minDist = dist;
                        nearest = point;
                    }
                }

                if (nearest != null)
                {
                    optimized.Add(nearest);
                    remaining.Remove(nearest);
                }
            }

            var optimizedRoute = new GeoRoute
            {
                Name = route.Name + " (Optimized)",
                Points = optimized,
                Color = route.Color,
                StrokeWidth = route.StrokeWidth
            };

            optimizedRoute.CalculateDistance();
            return optimizedRoute;
        }

        #endregion

        #region Areas

        public void AddArea(GeoArea area)
        {
            if (area == null) throw new ArgumentNullException(nameof(area));

            _areas.Add(area);
            DataChanged?.Invoke(this, EventArgs.Empty);
        }

        public void RemoveArea(GeoArea area)
        {
            _areas.Remove(area);
            DataChanged?.Invoke(this, EventArgs.Empty);
        }

        public IReadOnlyList<GeoArea> GetAllAreas() => _areas.AsReadOnly();

        #endregion

        #region Calculations

        public double CalculateDistance(GeoPoint p1, GeoPoint p2)
        {
            const double R = 6371; // км

            var dLat = ToRadians(p2.Latitude - p1.Latitude);
            var dLon = ToRadians(p2.Longitude - p1.Longitude);

            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(ToRadians(p1.Latitude)) * Math.Cos(ToRadians(p2.Latitude)) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return R * c;
        }

        public (double minLat, double minLon, double maxLat, double maxLon) GetBounds(List<GeoPoint> points)
        {
            if (points == null || points.Count == 0)
                return (0, 0, 0, 0);

            double minLat = points.Min(p => p.Latitude);
            double minLon = points.Min(p => p.Longitude);
            double maxLat = points.Max(p => p.Latitude);
            double maxLon = points.Max(p => p.Longitude);

            return (minLat, minLon, maxLat, maxLon);
        }

        public (double centerLat, double centerLon) GetCenter(List<GeoPoint> points)
        {
            if (points == null || points.Count == 0)
                return (0, 0);

            double centerLat = points.Average(p => p.Latitude);
            double centerLon = points.Average(p => p.Longitude);

            return (centerLat, centerLon);
        }

        private double ToRadians(double degrees) => degrees * Math.PI / 180.0;

        #endregion

        public void ClearAll()
        {
            _points.Clear();
            _routes.Clear();
            _areas.Clear();
            DataChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}