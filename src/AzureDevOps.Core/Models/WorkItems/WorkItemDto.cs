using System.Text.Json;
using System.Text.Json.Serialization;

namespace AzureDevOps.Core.Models.WorkItems;

public class WorkItemListResponse
{
    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("value")]
    public List<WorkItemDto> Value { get; set; } = new();
}

public class WorkItemDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("rev")]
    public int Rev { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("fields")]
    public Dictionary<string, object> Fields { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Safely retrieves a typed value or string representation from the fields dictionary.
    /// </summary>
    public T? GetFieldValue<T>(string fieldName)
    {
        if (!Fields.TryGetValue(fieldName, out var value) || value == null)
        {
            return default;
        }

        if (value is JsonElement element)
        {
            if (typeof(T) == typeof(string))
            {
                if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("displayName", out var disp))
                {
                    return (T)(object)disp.GetString()!;
                }
                return (T)(object)element.ToString();
            }
            if (typeof(T) == typeof(int) || typeof(T) == typeof(int?))
            {
                if (element.TryGetInt32(out var intVal)) return (T)(object)intVal;
            }
            if (typeof(T) == typeof(decimal) || typeof(T) == typeof(decimal?))
            {
                if (element.TryGetDecimal(out var decVal)) return (T)(object)decVal;
            }
            if (typeof(T) == typeof(double) || typeof(T) == typeof(double?))
            {
                if (element.TryGetDouble(out var dblVal)) return (T)(object)dblVal;
            }
            if (typeof(T) == typeof(DateTime) || typeof(T) == typeof(DateTime?))
            {
                if (element.TryGetDateTime(out var dtVal)) return (T)(object)dtVal.ToUniversalTime();
            }
            if (typeof(T) == typeof(bool) || typeof(T) == typeof(bool?))
            {
                return (T)(object)element.GetBoolean();
            }
        }

        if (value is T direct) return direct;

        try
        {
            var json = JsonSerializer.Serialize(value);
            return JsonSerializer.Deserialize<T>(json);
        }
        catch
        {
            return default;
        }
    }

    public string? GetIdentityName(string fieldName)
    {
        if (!Fields.TryGetValue(fieldName, out var val) || val == null) return null;
        if (val is JsonElement el)
        {
            if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("displayName", out var disp))
                return disp.GetString();
            if (el.ValueKind == JsonValueKind.String)
                return el.GetString();
        }
        return val.ToString();
    }

    public string? GetIdentityUniqueName(string fieldName)
    {
        if (!Fields.TryGetValue(fieldName, out var val) || val == null) return null;
        if (val is JsonElement el)
        {
            if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("uniqueName", out var unq))
                return unq.GetString();
            if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("displayName", out var disp))
                return disp.GetString();
            if (el.ValueKind == JsonValueKind.String)
                return el.GetString();
        }
        return val.ToString();
    }
}
