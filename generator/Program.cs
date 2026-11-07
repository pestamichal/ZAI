using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

Console.OutputEncoding = System.Text.Encoding.UTF8;

if (args.Length == 0 || args[0] is not ("send" or "seed"))
{
    Console.WriteLine("""
        Usage:
          generator send --api URL --key KEY [--mode backfill|live] [--count N] [--step 1h] [--interval 10s]
                         [--source random|sine|walk|open-meteo] [--min 0] [--max 30] [--period 24h]
                         [--variable temperature_2m] [--lat 52.23] [--lon 21.01]
          generator seed --api URL --user USER --pass PASSWORD [--hours 48] [--lat 52.23] [--lon 21.01]

        The sensor key can also be passed in the SENSOR_API_KEY environment variable.
        """);
    return 1;
}

var options = new Dictionary<string, string>();
for (var i = 1; i < args.Length - 1; i += 2)
    options[args[i].TrimStart('-')] = args[i + 1];

string Opt(string name, string? fallback = null) =>
    options.TryGetValue(name, out var v) ? v : fallback ?? throw new ArgumentException($"Missing --{name}");
double Num(string name, double fallback) =>
    options.TryGetValue(name, out var v) ? double.Parse(v, CultureInfo.InvariantCulture) : fallback;

var api = Opt("api").TrimEnd('/');
var lat = Num("lat", 52.23);
var lon = Num("lon", 21.01);
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

return args[0] == "send" ? await Send() : await Seed();

async Task<int> Send()
{
    var key = options.GetValueOrDefault("key") ?? Environment.GetEnvironmentVariable("SENSOR_API_KEY")
              ?? throw new ArgumentException("Missing --key or SENSOR_API_KEY");
    var mode = Opt("mode", "backfill");
    var source = Opt("source", "random");
    var min = Num("min", 0);
    var max = Num("max", 30);
    var period = ParseDuration(Opt("period", "24h"));
    int? count = options.ContainsKey("count") ? int.Parse(options["count"]) : mode == "backfill" ? 48 : null;
    var random = new Random();
    var walk = (min + max) / 2;

    double Next(DateTimeOffset t)
    {
        double v = source switch
        {
            "random" => min + random.NextDouble() * (max - min),
            "sine" => (min + max) / 2 + (max - min) / 2 * 0.9 * Math.Sin(2 * Math.PI * t.ToUnixTimeSeconds() / period.TotalSeconds),
            "walk" => walk = Math.Clamp(walk + (random.NextDouble() - 0.5) * (max - min) * 0.1, min, max),
            _ => throw new ArgumentException($"Unknown source {source}")
        };
        return Math.Round(v, 2);
    }

    if (mode == "backfill")
    {
        List<(DateTimeOffset, double)> points;
        if (source == "open-meteo")
        {
            var variable = Opt("variable", "temperature_2m");
            var days = (int)Math.Ceiling(count!.Value / 24.0) + 1;
            var data = await OpenMeteo.FetchHourly(http, lat, lon, Math.Min(days, 92), variable);
            points = data[variable].TakeLast(count.Value).ToList();
        }
        else
        {
            var step = ParseDuration(Opt("step", "1h"));
            var now = DateTimeOffset.UtcNow;
            points = Enumerable.Range(0, count!.Value)
                .Select(i => now - step * (count.Value - 1 - i))
                .Select(t => (t, Next(t)))
                .ToList();
        }

        var ok = 0;
        foreach (var (t, v) in points)
            if (await Post(key, v, t)) ok++;
        Console.WriteLine($"Sent {ok}/{points.Count} measurements");
        return 0;
    }

    var interval = ParseDuration(Opt("interval", "10s"));
    for (var i = 0; count is null || i < count; i++)
    {
        if (i > 0) await Task.Delay(interval);
        var now = DateTimeOffset.UtcNow;
        double value;
        if (source == "open-meteo")
        {
            var variable = Opt("variable", "temperature_2m");
            value = (await OpenMeteo.FetchHourly(http, lat, lon, 0, variable))[variable][^1].Value;
        }
        else value = Next(now);
        await Post(key, value, null);
    }
    return 0;
}

async Task<bool> Post(string key, double value, DateTimeOffset? timestamp)
{
    using var request = new HttpRequestMessage(HttpMethod.Post, $"{api}/api/measurements")
    {
        Content = JsonContent.Create(timestamp is null ? new { value } : (object)new { value, timestamp })
    };
    request.Headers.Add("X-API-Key", key);
    using var response = await http.SendAsync(request);
    var when = (timestamp ?? DateTimeOffset.UtcNow).ToString("u");

    switch (response.StatusCode)
    {
        case HttpStatusCode.Created:
            Console.WriteLine($"✔ {when}  {value}");
            return true;
        case HttpStatusCode.UnprocessableEntity:
            Console.WriteLine($"✘ Rejected (422): {await Detail(response)}");
            return false;
        case HttpStatusCode.Unauthorized:
            Console.WriteLine("✘ Key invalid or sensor unregistered");
            return false;
        default:
            Console.WriteLine($"✘ {(int)response.StatusCode}: {await Detail(response)}");
            return false;
    }
}

async Task<int> Seed()
{
    var login = await http.PostAsJsonAsync($"{api}/api/auth/login", new { username = Opt("user"), password = Opt("pass") });
    if (!login.IsSuccessStatusCode)
    {
        Console.WriteLine($"✘ Login failed ({(int)login.StatusCode})");
        return 1;
    }
    var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    var hours = (int)Num("hours", 48);
    var definitions = new[]
    {
        (Name: "Temperature", Min: -40.0, Max: 50.0, Color: "#e4572e", Unit: "°C", Icon: "thermometer", Variable: "temperature_2m"),
        (Name: "Humidity", Min: 0.0, Max: 100.0, Color: "#17bebb", Unit: "%", Icon: "droplet", Variable: "relative_humidity_2m"),
        (Name: "Pressure", Min: 900.0, Max: 1100.0, Color: "#4c6ef5", Unit: "hPa", Icon: "gauge", Variable: "surface_pressure"),
    };

    var weather = await OpenMeteo.FetchHourly(http, lat, lon, (int)Math.Ceiling(hours / 24.0) + 1,
        definitions.Select(d => d.Variable).ToArray());
    var existing = await http.GetFromJsonAsync<JsonElement>($"{api}/api/series");

    foreach (var d in definitions)
    {
        var seriesId = existing.EnumerateArray()
            .Where(s => s.GetProperty("name").GetString() == d.Name)
            .Select(s => (int?)s.GetProperty("id").GetInt32())
            .FirstOrDefault();

        if (seriesId is null)
        {
            var created = await http.PostAsJsonAsync($"{api}/api/series",
                new { name = d.Name, minValue = d.Min, maxValue = d.Max, color = d.Color, unit = d.Unit, icon = d.Icon });
            created.EnsureSuccessStatusCode();
            seriesId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        }

        var sensor = await http.PostAsJsonAsync($"{api}/api/sensors", new { name = $"{d.Name} sensor", seriesId });
        sensor.EnsureSuccessStatusCode();
        var key = (await sensor.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("apiKey").GetString()!;
        Console.WriteLine($"{d.Name}: series {seriesId}, sensor key {key}");

        var ok = 0;
        var points = weather[d.Variable].TakeLast(hours).ToList();
        foreach (var (t, v) in points)
            if (await Post(key, v, t)) ok++;
        Console.WriteLine($"{d.Name}: sent {ok}/{points.Count} measurements");
    }
    return 0;
}

static async Task<string> Detail(HttpResponseMessage response)
{
    var body = await response.Content.ReadAsStringAsync();
    try
    {
        var json = JsonDocument.Parse(body).RootElement;
        if (json.TryGetProperty("detail", out var detail)) return detail.GetString() ?? body;
        if (json.TryGetProperty("title", out var title)) return title.GetString() ?? body;
    }
    catch (JsonException) { }
    return body;
}

static TimeSpan ParseDuration(string text)
{
    var number = double.Parse(text[..^1], CultureInfo.InvariantCulture);
    return text[^1] switch
    {
        's' => TimeSpan.FromSeconds(number),
        'm' => TimeSpan.FromMinutes(number),
        'h' => TimeSpan.FromHours(number),
        'd' => TimeSpan.FromDays(number),
        _ => throw new ArgumentException($"Invalid duration {text}, use e.g. 10s, 5m, 1h")
    };
}
