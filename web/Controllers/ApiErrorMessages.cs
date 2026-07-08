using System.Text.Json;

namespace web.Controllers;

// SmsService.SendAsync embeds the api's JSON error body after "Body: " in the exception message;
// surface its `message` field to the user instead of a generic banner or a crash to the
// unhandled-exception page.
internal static class ApiErrorMessages
{
    private const string BodyMarker = "Body: ";

    public static string Extract(this HttpRequestException ex, string fallback)
    {
        var markerIndex = ex.Message.IndexOf(BodyMarker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return fallback;
        }

        var json = ex.Message[(markerIndex + BodyMarker.Length)..];
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("message", out var messageProp) && messageProp.GetString() is { } message)
            {
                return message;
            }
        }
        catch (JsonException)
        {
        }

        return fallback;
    }
}
