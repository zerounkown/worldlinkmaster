using System.Globalization;
using System.Text.Json;

namespace WorldLinkMaster.Web.Services;

// Free geocoding via OpenStreetMap's Nominatim service — no API key/billing, but its usage
// policy (https://operations.osmfoundation.org/policies/nominatim/) requires a descriptive
// User-Agent (set once on the HttpClient in Program.cs) and caps usage at 1 request/second.
// Proxying through our own server keeps that identification consistent and lets callers
// (the public store locator and the admin "Autofill from address" button) share one
// implementation instead of each talking to Nominatim directly from the browser.
public class NominatimGeocodingService : IGeocodingService
{
    private readonly HttpClient _httpClient;

    public NominatimGeocodingService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<GeocodeResult?> GeocodeAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var url = $"search?q={Uri.EscapeDataString(query)}&format=jsonv2&limit=1";
        using var response = await _httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
        {
            return null;
        }

        var first = doc.RootElement[0];
        var lat = decimal.Parse(first.GetProperty("lat").GetString()!, CultureInfo.InvariantCulture);
        var lon = decimal.Parse(first.GetProperty("lon").GetString()!, CultureInfo.InvariantCulture);
        var displayName = first.TryGetProperty("display_name", out var nameProp) ? nameProp.GetString() ?? query : query;

        return new GeocodeResult(lat, lon, displayName);
    }
}
