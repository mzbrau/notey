using System.Globalization;
using System.Text.Json.Nodes;

namespace Notey.PipelineSteps;

internal static class StepConfigurationReader
{
    public static string? GetString(JsonObject? configuration, string key)
    {
        if (configuration is null || !configuration.TryGetPropertyValue(key, out var value))
        {
            return null;
        }

        if (value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var text))
        {
            return text.Trim();
        }

        return value?.ToString().Trim();
    }

    public static bool GetBoolean(JsonObject? configuration, string key, bool defaultValue = false)
    {
        if (configuration is null || !configuration.TryGetPropertyValue(key, out var value) || value is null)
        {
            return defaultValue;
        }

        if (value is JsonValue jsonValue && jsonValue.TryGetValue<bool>(out var booleanValue))
        {
            return booleanValue;
        }

        var text = value.GetValue<string>();
        return bool.TryParse(text, out var parsed) ? parsed : defaultValue;
    }

    public static int? GetInt32(JsonObject? configuration, string key)
    {
        if (configuration is null || !configuration.TryGetPropertyValue(key, out var value) || value is null)
        {
            return null;
        }

        if (value is JsonValue jsonValue && jsonValue.TryGetValue<int>(out var integerValue))
        {
            return integerValue;
        }

        var text = value.GetValue<string>();
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    public static double? GetDouble(JsonObject? configuration, string key)
    {
        if (configuration is null || !configuration.TryGetPropertyValue(key, out var value) || value is null)
        {
            return null;
        }

        if (value is JsonValue jsonValue && jsonValue.TryGetValue<double>(out var doubleValue))
        {
            return doubleValue;
        }

        var text = value.GetValue<string>();
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }
}
