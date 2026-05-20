using DoggyDrop.Models;
using DoggyDrop.ViewModels;
using System.Globalization;
using System.Text.Json;

namespace DoggyDrop.Services
{
    public class OsmWalkPlannerService : IOsmWalkPlannerService
    {
        private const string OverpassUrl = "https://overpass-api.de/api/interpreter";
        private const string OsrmBaseUrl = "https://router.project-osrm.org/route/v1/foot/";
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly HttpClient _httpClient;
        private readonly ILogger<OsmWalkPlannerService> _logger;

        public OsmWalkPlannerService(HttpClient httpClient, ILogger<OsmWalkPlannerService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<PlannedWalkRoute?> PlanAsync(
            double startLatitude,
            double startLongitude,
            double targetDistanceKm,
            IReadOnlyList<TrashBin> bins,
            string walkStyle,
            string dogEnergy,
            bool includeBins,
            bool includePark,
            bool includeWater,
            bool includeDogFriendly,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var effectiveDistanceKm = AdjustDistanceForEnergyAndStyle(targetDistanceKm, dogEnergy, walkStyle);
                var radiusMeters = Math.Clamp((int)Math.Round(effectiveDistanceKm * 650), 900, 4500);
                var osmPlaces = await FetchOsmPlacesAsync(startLatitude, startLongitude, radiusMeters, cancellationToken);
                var stops = BuildStops(startLatitude, startLongitude, effectiveDistanceKm, bins, osmPlaces, walkStyle, includeBins, includePark, includeWater, includeDogFriendly);
                var route = await FetchOsrmRouteAsync(stops, cancellationToken) ?? BuildFallbackLoop(startLatitude, startLongitude, effectiveDistanceKm, stops);
                var estimatedDistanceKm = route.DistanceKm > 0 ? route.DistanceKm : EstimateRouteDistance(route.Points);

                return new PlannedWalkRoute
                {
                    Title = $"{effectiveDistanceKm:0.#} km AI walk - Moja lokacija",
                    Summary = BuildSummary(stops, effectiveDistanceKm, walkStyle, dogEnergy, route.UsedOsrm),
                    TargetDistanceKm = effectiveDistanceKm,
                    EstimatedDistanceKm = estimatedDistanceKm,
                    EstimatedMinutes = Math.Max(10, (int)Math.Round(estimatedDistanceKm / GetSpeedKmPerHour(dogEnergy, walkStyle) * 60)),
                    Stops = stops.OrderBy(stop => stop.Order).ToList(),
                    RoutePoints = route.Points
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OSM walk planning failed.");
                return null;
            }
        }

        private async Task<IReadOnlyList<OsmPlace>> FetchOsmPlacesAsync(
            double latitude,
            double longitude,
            int radiusMeters,
            CancellationToken cancellationToken)
        {
            var query = FormattableString.Invariant($"""
                [out:json][timeout:8];
                (
                  node(around:{radiusMeters},{latitude},{longitude})["leisure"="park"];
                  way(around:{radiusMeters},{latitude},{longitude})["leisure"="park"];
                  relation(around:{radiusMeters},{latitude},{longitude})["leisure"="park"];
                  node(around:{radiusMeters},{latitude},{longitude})["amenity"="drinking_water"];
                  node(around:{radiusMeters},{latitude},{longitude})["amenity"="fountain"];
                  node(around:{radiusMeters},{latitude},{longitude})["shop"="pet"];
                  node(around:{radiusMeters},{latitude},{longitude})["amenity"="cafe"];
                  node(around:{radiusMeters},{latitude},{longitude})["dog"];
                  way(around:{radiusMeters},{latitude},{longitude})["dog"];
                );
                out center tags 40;
                """);

            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["data"] = query
            });
            using var response = await _httpClient.PostAsync(OverpassUrl, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Overpass returned {StatusCode}.", response.StatusCode);
                return [];
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var result = await JsonSerializer.DeserializeAsync<OverpassResponse>(stream, JsonOptions, cancellationToken);
            if (result?.Elements == null)
            {
                return [];
            }

            return result.Elements
                .Select(element => ToOsmPlace(element, latitude, longitude))
                .Where(place => place != null)
                .Select(place => place!)
                .GroupBy(place => $"{place.Type}:{place.Name}", StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderBy(place => place.DistanceKm).First())
                .OrderBy(place => place.DistanceKm)
                .Take(30)
                .ToList();
        }

        private static OsmPlace? ToOsmPlace(OverpassElement element, double startLatitude, double startLongitude)
        {
            var latitude = element.Lat ?? element.Center?.Lat;
            var longitude = element.Lon ?? element.Center?.Lon;
            if (latitude is null || longitude is null)
            {
                return null;
            }

            var tags = element.Tags ?? new Dictionary<string, string>();
            var type = ResolveType(tags);
            if (type == null)
            {
                return null;
            }

            var name = tags.TryGetValue("name", out var rawName) && !string.IsNullOrWhiteSpace(rawName)
                ? rawName.Trim()
                : type switch
                {
                    "park" => "Zelena točka v bližini",
                    "water" => "Voda v bližini",
                    "shop" => "Pet shop v bližini",
                    "cafe" => "Dog-friendly postanek",
                    _ => "Postanek v bližini"
                };

            return new OsmPlace(
                name,
                type,
                latitude.Value,
                longitude.Value,
                GetDistanceKm(startLatitude, startLongitude, latitude.Value, longitude.Value),
                tags);
        }

        private static string? ResolveType(IReadOnlyDictionary<string, string> tags)
        {
            if (tags.TryGetValue("leisure", out var leisure) && leisure == "park")
            {
                return "park";
            }

            if (tags.TryGetValue("amenity", out var amenity) && amenity is "drinking_water" or "fountain")
            {
                return "water";
            }

            if (tags.TryGetValue("shop", out var shop) && shop == "pet")
            {
                return "shop";
            }

            if (tags.TryGetValue("amenity", out amenity) && amenity == "cafe")
            {
                return tags.TryGetValue("dog", out var dog) && dog is "yes" or "leashed" ? "cafe" : null;
            }

            if (tags.TryGetValue("dog", out var dogValue) && dogValue is "yes" or "leashed")
            {
                return "park";
            }

            return null;
        }

        private static IReadOnlyList<PlannedWalkRouteStop> BuildStops(
            double startLatitude,
            double startLongitude,
            double effectiveDistanceKm,
            IReadOnlyList<TrashBin> bins,
            IReadOnlyList<OsmPlace> osmPlaces,
            string walkStyle,
            bool includeBins,
            bool includePark,
            bool includeWater,
            bool includeDogFriendly)
        {
            var stops = new List<PlannedWalkRouteStop>
            {
                new()
                {
                    Name = "Start: moja lokacija",
                    Type = "start",
                    Label = "Start",
                    Reason = "Začetna točka iz GPS lokacije.",
                    Latitude = startLatitude,
                    Longitude = startLongitude,
                    Order = 1
                }
            };
            var order = 2;
            var maxCandidateDistanceKm = Math.Max(1.2, effectiveDistanceKm * 0.75);
            var requireBins = includeBins || walkStyle == "quick";

            if (requireBins)
            {
                var binsToTake = walkStyle == "long" || effectiveDistanceKm >= 5.5 ? 2 : 1;
                foreach (var bin in bins
                    .Select(bin => new
                    {
                        Bin = bin,
                        DistanceKm = GetDistanceKm(startLatitude, startLongitude, bin.Latitude, bin.Longitude)
                    })
                    .Where(item => item.DistanceKm <= maxCandidateDistanceKm)
                    .OrderBy(item => item.DistanceKm * 0.72 - GetBinReliabilityScore(item.Bin) / 320d)
                    .Take(binsToTake))
                {
                    stops.Add(new PlannedWalkRouteStop
                    {
                        Name = bin.Bin.Name,
                        Type = "bin",
                        Label = "Pasji koš",
                        Reason = bin.DistanceKm <= 0.7
                            ? $"Najbližji zanesljiv koš. Zanesljivost: {GetBinReliabilityLabel(bin.Bin)}."
                            : $"Koš je vključen, ker lepo sede v krog. Zanesljivost: {GetBinReliabilityLabel(bin.Bin)}.",
                        Latitude = bin.Bin.Latitude,
                        Longitude = bin.Bin.Longitude,
                        Order = order++
                    });
                }
            }

            if (includePark || walkStyle == "park")
            {
                AddBestOsmStop(stops, osmPlaces, "park", "Park/zelena točka", "Zelena točka iz OpenStreetMap za vohanje in mirnejši tempo.", maxCandidateDistanceKm, ref order);
            }

            if (includeWater || walkStyle == "city")
            {
                AddBestOsmStop(stops, osmPlaces, "water", "Voda", "Postanek za hidracijo iz OpenStreetMap podatkov.", maxCandidateDistanceKm, ref order);
            }

            if (includeDogFriendly && effectiveDistanceKm >= 3)
            {
                AddBestOsmStop(stops, osmPlaces, "shop", "Pet shop", "Dog-friendly praktičen postanek iz OpenStreetMap.", maxCandidateDistanceKm, ref order);
                AddBestOsmStop(stops, osmPlaces, "cafe", "Dog-friendly", "Socialni dog-friendly postanek iz OpenStreetMap.", maxCandidateDistanceKm, ref order);
            }

            stops.Add(new PlannedWalkRouteStop
            {
                Name = "Cilj: moja lokacija",
                Type = "finish",
                Label = "Cilj",
                Reason = "Zaključek krožne poti.",
                Latitude = startLatitude,
                Longitude = startLongitude,
                Order = order
            });

            return OrderStopsByAngle(stops, startLatitude, startLongitude);
        }

        private static void AddBestOsmStop(
            List<PlannedWalkRouteStop> stops,
            IReadOnlyList<OsmPlace> places,
            string type,
            string label,
            string reason,
            double maxDistanceKm,
            ref int order)
        {
            var existing = stops
                .Where(stop => stop.Type is not "start" and not "finish")
                .Select(stop => (stop.Latitude, stop.Longitude))
                .ToList();
            var place = places
                .Where(candidate => candidate.Type == type && candidate.DistanceKm <= maxDistanceKm)
                .Where(candidate => !existing.Any(existingStop => GetDistanceKm(existingStop.Latitude, existingStop.Longitude, candidate.Latitude, candidate.Longitude) < 0.08))
                .OrderBy(candidate => Math.Abs(candidate.DistanceKm - maxDistanceKm * 0.55))
                .ThenBy(candidate => candidate.DistanceKm)
                .FirstOrDefault();

            if (place == null)
            {
                return;
            }

            stops.Add(new PlannedWalkRouteStop
            {
                Name = place.Name,
                Type = place.Type,
                Label = label,
                Reason = reason,
                Latitude = place.Latitude,
                Longitude = place.Longitude,
                Order = order++
            });
        }

        private static IReadOnlyList<PlannedWalkRouteStop> OrderStopsByAngle(
            IReadOnlyList<PlannedWalkRouteStop> stops,
            double startLatitude,
            double startLongitude)
        {
            var orderedStops = new List<PlannedWalkRouteStop>();
            var middle = stops
                .Where(stop => stop.Type is not "start" and not "finish")
                .OrderBy(stop => Math.Atan2(stop.Latitude - startLatitude, stop.Longitude - startLongitude))
                .ToList();

            var order = 1;
            var start = stops.First(stop => stop.Type == "start");
            start.Order = order++;
            orderedStops.Add(start);

            foreach (var stop in middle)
            {
                stop.Order = order++;
                orderedStops.Add(stop);
            }

            var finish = stops.First(stop => stop.Type == "finish");
            finish.Order = order;
            orderedStops.Add(finish);
            return orderedStops;
        }

        private async Task<RouteGeometry?> FetchOsrmRouteAsync(
            IReadOnlyList<PlannedWalkRouteStop> stops,
            CancellationToken cancellationToken)
        {
            if (stops.Count < 2)
            {
                return null;
            }

            var coordinates = string.Join(";", stops.Select(stop =>
                FormattableString.Invariant($"{stop.Longitude:0.######},{stop.Latitude:0.######}")));
            var url = $"{OsrmBaseUrl}{coordinates}?overview=full&geometries=geojson&steps=false";
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogInformation("OSRM returned {StatusCode}.", response.StatusCode);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var result = await JsonSerializer.DeserializeAsync<OsrmResponse>(stream, JsonOptions, cancellationToken);
            var route = result?.Routes?.FirstOrDefault();
            var coordinatesResult = route?.Geometry?.Coordinates;
            if (coordinatesResult == null || coordinatesResult.Count < 2)
            {
                return null;
            }

            return new RouteGeometry(
                coordinatesResult
                    .Where(point => point.Count >= 2)
                    .Select(point => new PlannedWalkPoint { Latitude = point[1], Longitude = point[0] })
                    .ToList(),
                (route?.Distance ?? 0) / 1000d,
                true);
        }

        private static RouteGeometry BuildFallbackLoop(
            double latitude,
            double longitude,
            double targetDistanceKm,
            IReadOnlyList<PlannedWalkRouteStop> stops)
        {
            var radiusKm = Math.Max(0.25, targetDistanceKm / (2 * Math.PI));
            var latDelta = radiusKm / 111.0;
            var lngDelta = radiusKm / (111.0 * Math.Cos(ToRadians(latitude)));
            var generated = new List<PlannedWalkPoint>
            {
                new() { Latitude = latitude, Longitude = longitude }
            };

            foreach (var stop in stops.Where(stop => stop.Type is not "start" and not "finish"))
            {
                generated.Add(new PlannedWalkPoint { Latitude = stop.Latitude, Longitude = stop.Longitude });
            }

            generated.AddRange([
                new PlannedWalkPoint { Latitude = latitude + latDelta, Longitude = longitude + lngDelta * 0.45 },
                new PlannedWalkPoint { Latitude = latitude + latDelta * 0.2, Longitude = longitude + lngDelta },
                new PlannedWalkPoint { Latitude = latitude - latDelta * 0.85, Longitude = longitude + lngDelta * 0.4 },
                new PlannedWalkPoint { Latitude = latitude - latDelta * 0.65, Longitude = longitude - lngDelta * 0.65 },
                new PlannedWalkPoint { Latitude = latitude + latDelta * 0.35, Longitude = longitude - lngDelta },
                new PlannedWalkPoint { Latitude = latitude, Longitude = longitude }
            ]);

            return new RouteGeometry(generated, EstimateRouteDistance(generated), false);
        }

        private static string BuildSummary(
            IReadOnlyList<PlannedWalkRouteStop> stops,
            double targetDistanceKm,
            string walkStyle,
            string dogEnergy,
            bool usedOsrm)
        {
            var parts = new List<string>();
            if (stops.Any(stop => stop.Type == "bin"))
            {
                parts.Add("pasji koš");
            }

            if (stops.Any(stop => stop.Type == "park"))
            {
                parts.Add("OSM zelena točka");
            }

            if (stops.Any(stop => stop.Type == "water"))
            {
                parts.Add("voda");
            }

            if (stops.Any(stop => stop.Type is "shop" or "cafe"))
            {
                parts.Add("dog-friendly postanek");
            }

            var intro = walkStyle switch
            {
                "quick" => "Hiter krog iz tvoje lokacije",
                "park" => "Sproščen sprehod z več vohanja",
                "city" => "Mestni krog z uporabnimi postanki",
                "long" => "Daljši raziskovalni sprehod",
                _ => "Uravnotežen krog iz tvoje lokacije"
            };
            var energyNote = dogEnergy switch
            {
                "low" => "Tempo je mirnejši.",
                "high" => "Tempo je bolj aktiven.",
                _ => "Tempo je srednje živahen."
            };
            var routeNote = usedOsrm ? " Pot je zrisana z OSRM peš routingom." : " Pot je začasni krog, ker routing ni odgovoril.";
            return parts.Count == 0
                ? $"{intro} {targetDistanceKm:0.#} km. {energyNote}{routeNote}"
                : $"{intro} {targetDistanceKm:0.#} km: {string.Join(", ", parts)}. {energyNote}{routeNote}";
        }

        private static double AdjustDistanceForEnergyAndStyle(double targetDistanceKm, string dogEnergy, string walkStyle)
        {
            var distance = targetDistanceKm;

            if (dogEnergy == "low")
            {
                distance -= 0.4;
            }
            else if (dogEnergy == "high")
            {
                distance += 0.8;
            }

            if (walkStyle == "quick")
            {
                distance = Math.Min(distance, 2.8);
            }
            else if (walkStyle == "long")
            {
                distance += 0.6;
            }

            return Math.Clamp(distance, 1, 12);
        }

        private static double GetSpeedKmPerHour(string dogEnergy, string walkStyle)
        {
            var baseSpeed = dogEnergy switch
            {
                "low" => 3.8,
                "high" => 5.0,
                _ => 4.5
            };

            return walkStyle switch
            {
                "park" => baseSpeed - 0.4,
                "long" => baseSpeed + 0.2,
                _ => baseSpeed
            };
        }

        private static int GetBinReliabilityScore(TrashBin bin)
        {
            var score = 60;
            score += Math.Min(18, bin.UsefulVotes * 3);
            score += Math.Min(15, bin.UsedCount);
            score -= Math.Min(28, bin.FullReports * 8);
            score -= Math.Min(35, bin.MissingReports * 12);
            return Math.Clamp(score, 0, 100);
        }

        private static string GetBinReliabilityLabel(TrashBin bin)
        {
            var score = GetBinReliabilityScore(bin);
            if (score >= 80)
            {
                return "zelo dobra";
            }

            if (score >= 60)
            {
                return "dobra";
            }

            if (score >= 40)
            {
                return "srednja";
            }

            return "nižja";
        }

        private static double EstimateRouteDistance(IReadOnlyList<PlannedWalkPoint> points)
        {
            if (points.Count < 2)
            {
                return 0;
            }

            var distance = 0.0;
            for (var i = 1; i < points.Count; i++)
            {
                distance += GetDistanceKm(points[i - 1].Latitude, points[i - 1].Longitude, points[i].Latitude, points[i].Longitude);
            }

            return distance;
        }

        private static double GetDistanceKm(double lat1, double lon1, double lat2, double lon2)
        {
            const double earthRadiusKm = 6371;
            var dLat = ToRadians(lat2 - lat1);
            var dLon = ToRadians(lon2 - lon1);
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return earthRadiusKm * c;
        }

        private static double ToRadians(double degrees) => degrees * Math.PI / 180;

        private sealed record OsmPlace(
            string Name,
            string Type,
            double Latitude,
            double Longitude,
            double DistanceKm,
            IReadOnlyDictionary<string, string> Tags);

        private sealed record RouteGeometry(IReadOnlyList<PlannedWalkPoint> Points, double DistanceKm, bool UsedOsrm);

        private sealed class OverpassResponse
        {
            public List<OverpassElement>? Elements { get; set; }
        }

        private sealed class OverpassElement
        {
            public double? Lat { get; set; }

            public double? Lon { get; set; }

            public OverpassCenter? Center { get; set; }

            public Dictionary<string, string>? Tags { get; set; }
        }

        private sealed class OverpassCenter
        {
            public double Lat { get; set; }

            public double Lon { get; set; }
        }

        private sealed class OsrmResponse
        {
            public List<OsrmRoute>? Routes { get; set; }
        }

        private sealed class OsrmRoute
        {
            public double Distance { get; set; }

            public OsrmGeometry? Geometry { get; set; }
        }

        private sealed class OsrmGeometry
        {
            public List<List<double>>? Coordinates { get; set; }
        }
    }
}
