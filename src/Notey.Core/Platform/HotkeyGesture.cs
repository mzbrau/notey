namespace Notey.Core.Platform;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8
}

public sealed record HotkeyGesture(HotkeyModifiers Modifiers, string Key)
{
    public static HotkeyGesture Parse(string gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture))
        {
            throw new ArgumentException("Hotkey gesture cannot be empty.", nameof(gesture));
        }

        var parts = gesture
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static part => !string.IsNullOrWhiteSpace(part))
            .ToArray();

        if (parts.Length < 2)
        {
            throw new FormatException("Hotkey gesture must include at least one modifier and one key.");
        }

        var modifiers = HotkeyModifiers.None;
        string? key = null;

        foreach (var part in parts)
        {
            if (TryParseModifier(part, out var modifier))
            {
                if ((modifiers & modifier) == modifier)
                {
                    throw new FormatException($"Hotkey modifier '{part}' is duplicated.");
                }

                modifiers |= modifier;
                continue;
            }

            if (key is not null)
            {
                throw new FormatException("Hotkey gesture can only contain one non-modifier key.");
            }

            key = NormalizeKey(part);
        }

        if (modifiers == HotkeyModifiers.None || key is null)
        {
            throw new FormatException("Hotkey gesture must include at least one modifier and one key.");
        }

        return new HotkeyGesture(modifiers, key);
    }

    private static bool TryParseModifier(string value, out HotkeyModifiers modifier)
    {
        modifier = value.ToUpperInvariant() switch
        {
            "CTRL" or "CONTROL" => HotkeyModifiers.Control,
            "ALT" or "OPTION" => HotkeyModifiers.Alt,
            "SHIFT" => HotkeyModifiers.Shift,
            "WIN" or "WINDOWS" or "META" or "CMD" or "COMMAND" => HotkeyModifiers.Windows,
            _ => HotkeyModifiers.None
        };

        return modifier != HotkeyModifiers.None;
    }

    private static string NormalizeKey(string value)
    {
        var normalized = value.Trim().ToUpperInvariant();

        if (normalized.Length == 1 && char.IsLetterOrDigit(normalized[0]))
        {
            return normalized;
        }

        if (normalized.Length is >= 2 and <= 3
            && normalized[0] == 'F'
            && int.TryParse(normalized[1..], out var functionKey)
            && functionKey is >= 1 and <= 24)
        {
            return normalized;
        }

        return normalized switch
        {
            "ESC" => "ESCAPE",
            "RETURN" => "ENTER",
            "SPACEBAR" => "SPACE",
            "DEL" => "DELETE",
            _ => normalized
        };
    }
}
