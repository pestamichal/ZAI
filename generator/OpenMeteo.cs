using System.Globalization;
using System.Text.Json;

public static class OpenMeteo
{
    public static async Task<Dictionary<string, List<(DateTimeOffset Time, double Value)>>> FetchHourly(
        HttpClient http, double lat, double lon, int pastDays, params string[] variables)
    {
        var url = "https://api.open-meteo.com/v1/forecast" +
                  $"?latitude={lat.ToString(CultureInfo.InvariantCulture)}&longitude={lon.ToString(CultureInfo.InvariantCulture)}" +
                  $"&hourly={string.Join(',', variables)}&past_days={pastDays}&forecast_days=1&timezone=GMT";

        using var doc = JsonDocument.Parse(await http.GetStringAsync(url));
        var hourly = doc.RootElement.GetProperty("hourly");
        var times = hourly.GetProperty("time").EnumerateArray()
            .Select(t => DateTimeOffset.Parse(t.GetString() + "Z", CultureInfo.InvariantCulture))
            .ToList();
        var now = DateTimeOffset.UtcNow;

        var result = new Dictionary<string, List<(DateTimeOffset, double)>>();
        foreach (var variable in variables)
        {
            var values = hourly.GetProperty(variable).EnumerateArray().ToList();
            result[variable] = times.Zip(values)
                .Where(p => p.First <= now && p.Second.ValueKind == JsonValueKind.Number)
                .Select(p => (p.First, p.Second.GetDouble()))
                .ToList();
        }
        return result;
    }
}
