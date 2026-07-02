using System.Text.Json;
using System.Text.Json.Serialization;

namespace LiveSportsTicker.Services;

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // IMPORTANT: without this, enums (MatchEventType) serialize as plain
        // integers (0, 1, 2...) instead of strings, which breaks the
        // JavaScript "ev.type.toLowerCase()" call on the client and silently
        // kills the whole event-feed rendering (caught by the try/catch).
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}
