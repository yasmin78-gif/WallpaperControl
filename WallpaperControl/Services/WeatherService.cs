using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WallpaperControl
{
    internal sealed class WeatherSnapshot
    {
        public string LocationName { get; init; } = "";
        public string Country { get; init; } = "";
        public double TemperatureC { get; init; }
        public double ApparentTemperatureC { get; init; }
        public double RelativeHumidityPercent { get; init; }
        public double WindSpeedKmh { get; init; }
        public double PrecipitationMm { get; init; }
        public int WeatherCode { get; init; }
        public IReadOnlyList<WeatherDaySnapshot> Days { get; init; } = Array.Empty<WeatherDaySnapshot>();
    }

    internal sealed class WeatherDaySnapshot
    {
        public DateTime Date { get; init; }
        public int WeatherCode { get; init; }
        public double MaxTemperatureC { get; init; }
        public double MinTemperatureC { get; init; }
    }

    internal sealed class WeatherService : IDisposable
    {
        private readonly HttpClient httpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(12)
        };

        /// <summary>
        /// Resolves the location and downloads its current weather and forecast in the requested language.
        /// </summary>
        /// <param name="locationName">The city or location query used for weather lookup.</param>
        /// <param name="languageCode">The language code used for localized text.</param>
        /// <param name="cancellationToken">The token used to cancel the operation.</param>
        /// <returns>A task whose result contains the resolved location, current conditions, and forecast.</returns>
        public async Task<WeatherSnapshot> FetchAsync(string locationName, string languageCode, CancellationToken cancellationToken)
        {
            string query = (locationName ?? "").Trim();
            if (query.Length == 0)
                throw new InvalidOperationException("Weather location is empty.");

            string language = NormalizeLanguage(languageCode);
            JsonElement place = await ResolveLocationAsync(query, language, cancellationToken).ConfigureAwait(false);
            double latitude = place.GetProperty("latitude").GetDouble();
            double longitude = place.GetProperty("longitude").GetDouble();
            string resolvedName = GetString(place, "name") ?? query;
            string country = GetString(place, "country") ?? "";

            string forecastUrl =
                "https://api.open-meteo.com/v1/forecast" +
                $"?latitude={latitude.ToString(CultureInfo.InvariantCulture)}" +
                $"&longitude={longitude.ToString(CultureInfo.InvariantCulture)}" +
                "&current=temperature_2m,apparent_temperature,relative_humidity_2m,weather_code,wind_speed_10m,precipitation" +
                "&daily=weather_code,temperature_2m_max,temperature_2m_min" +
                "&forecast_days=4&timezone=auto";

            using JsonDocument forecastDocument = await GetJsonAsync(forecastUrl, cancellationToken).ConfigureAwait(false);
            JsonElement root = forecastDocument.RootElement;
            JsonElement current = root.GetProperty("current");

            List<WeatherDaySnapshot> days = new();
            if (root.TryGetProperty("daily", out JsonElement daily))
            {
                JsonElement[] dates = daily.GetProperty("time").EnumerateArray().ToArray();
                JsonElement[] codes = daily.GetProperty("weather_code").EnumerateArray().ToArray();
                JsonElement[] maxTemps = daily.GetProperty("temperature_2m_max").EnumerateArray().ToArray();
                JsonElement[] minTemps = daily.GetProperty("temperature_2m_min").EnumerateArray().ToArray();

                int count = new[] { dates.Length, codes.Length, maxTemps.Length, minTemps.Length }.Min();
                for (int i = 0; i < count; i++)
                {
                    if (!DateTime.TryParse(dates[i].GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date))
                        continue;

                    days.Add(new WeatherDaySnapshot
                    {
                        Date = date,
                        WeatherCode = codes[i].GetInt32(),
                        MaxTemperatureC = maxTemps[i].GetDouble(),
                        MinTemperatureC = minTemps[i].GetDouble()
                    });
                }
            }

            return new WeatherSnapshot
            {
                LocationName = resolvedName,
                Country = country,
                TemperatureC = GetDouble(current, "temperature_2m"),
                ApparentTemperatureC = GetDouble(current, "apparent_temperature"),
                RelativeHumidityPercent = GetDouble(current, "relative_humidity_2m"),
                WindSpeedKmh = GetDouble(current, "wind_speed_10m"),
                PrecipitationMm = GetDouble(current, "precipitation"),
                WeatherCode = current.GetProperty("weather_code").GetInt32(),
                Days = days
            };
        }


        /// <summary>
        /// Finds geographic coordinates and display details for the requested location.
        /// </summary>
        /// <param name="query">The city or location search text.</param>
        /// <param name="language">The requested language or culture code.</param>
        /// <param name="cancellationToken">The token used to cancel the operation.</param>
        /// <returns>A task whose result contains the matched location and geographic coordinates.</returns>
        private async Task<JsonElement> ResolveLocationAsync(string query, string language, CancellationToken cancellationToken)
        {
            // Open-Meteo uses exact matching for two-character searches. Some Japanese
            // city names are indexed with their administrative suffix (e.g. 東京都),
            // so a natural input such as 東京 can otherwise return no result.
            // This must depend on the entered text, not on the UI language.
            List<string> candidates = new() { query };
            if (IsJapaneseText(query) && query.Length <= 2)
            {
                candidates.Add(query + "市");
                candidates.Add(query + "都");
                candidates.Add(query + "府");
                candidates.Add(query + "県");
            }

            foreach (string candidate in candidates.Distinct())
            {
                string geoUrl =
                    "https://geocoding-api.open-meteo.com/v1/search" +
                    $"?name={Uri.EscapeDataString(candidate)}&count=1&language={language}&format=json";

                using JsonDocument geoDocument = await GetJsonAsync(geoUrl, cancellationToken).ConfigureAwait(false);
                JsonElement geoRoot = geoDocument.RootElement;
                if (geoRoot.TryGetProperty("results", out JsonElement results) &&
                    results.ValueKind == JsonValueKind.Array &&
                    results.GetArrayLength() > 0)
                {
                    // Clone because the JsonDocument is disposed when this method returns.
                    return results[0].Clone();
                }
            }

            throw new InvalidOperationException("Weather location was not found.");
        }

        /// <summary>
        /// Detects Japanese character ranges used when choosing a geocoding language.
        /// </summary>
        /// <param name="value">The text to inspect for Japanese characters.</param>
        /// <returns>True when the text contains a character in the checked Japanese or CJK ranges.</returns>
        private static bool IsJapaneseText(string value)
        {
            foreach (char c in value)
            {
                if ((c >= '\u3040' && c <= '\u30ff') || // Hiragana / Katakana
                    (c >= '\u3400' && c <= '\u4dbf') || // CJK Extension A
                    (c >= '\u4e00' && c <= '\u9fff'))   // CJK Unified Ideographs
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Downloads a weather-service response and parses its JSON while honoring cancellation.
        /// </summary>
        /// <param name="url">The service URL to download.</param>
        /// <param name="cancellationToken">The token used to cancel the operation.</param>
        /// <returns>A task whose result is a JSON document that the caller must dispose.</returns>
        private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellationToken)
        {
            using HttpResponseMessage response = await httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Maps a language preference to a supported weather-service language.
        /// </summary>
        /// <param name="languageCode">The language code used for localized text.</param>
        /// <returns>A supported weather language code, or en when the input is unsupported.</returns>
        private static string NormalizeLanguage(string? languageCode)
        {
            string value = (languageCode ?? "en").Trim().ToLowerInvariant();
            int dash = value.IndexOf('-');
            if (dash > 0) value = value[..dash];
            return value is "de" or "en" or "fr" or "es" or "ja" ? value : "en";
        }

        /// <summary>
        /// Reads a string property from a JSON object with the service&apos;s missing-value fallback.
        /// </summary>
        /// <param name="element">The JSON object containing the requested property.</param>
        /// <param name="propertyName">The name of the JSON property to read.</param>
        /// <returns>The string property value, or null when it is missing or not a string.</returns>
        private static string? GetString(JsonElement element, string propertyName) =>
            element.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        /// <summary>
        /// Reads a numeric property from a JSON object with the service&apos;s missing-value fallback.
        /// </summary>
        /// <param name="element">The JSON object containing the requested property.</param>
        /// <param name="propertyName">The name of the JSON property to read.</param>
        /// <returns>The numeric property value, or zero when it is missing or not numeric.</returns>
        private static double GetDouble(JsonElement element, string propertyName) =>
            element.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : 0d;

        /// <summary>
        /// Disposes the weather service&apos;s HTTP client.
        /// </summary>
        public void Dispose() => httpClient.Dispose();
    }
}
