using System.Text.Json;

namespace AncosSantiye.Events;

/// <summary>One JSON shape for the log, the API and the browser: camelCase, nulls left out.</summary>
public static class EventJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };
}
