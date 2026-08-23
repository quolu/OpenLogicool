namespace OpenLogicool.AI;

/// <summary>同一frame内の意味ラベルとOCR文字列を比較するpure matcher。</summary>
public static class FrameBoundLabelMatcher
{
    public static string Normalize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new string(value
            .Where(character => char.IsLetterOrDigit(character)
                || character is '.' or '-' or '_')
            .Select(char.ToLowerInvariant)
            .ToArray());
    }

    public static bool Equals(string observed, string expected)
    {
        var normalizedExpected = Normalize(expected);
        return normalizedExpected.Length > 0
            && string.Equals(NormalizeObserved(observed, normalizedExpected), normalizedExpected, StringComparison.Ordinal);
    }

    public static bool Contains(string observed, string expected)
    {
        var normalizedExpected = Normalize(expected);
        return normalizedExpected.Length > 0
            && NormalizeObserved(observed, normalizedExpected).Contains(normalizedExpected, StringComparison.Ordinal);
    }

    private static string NormalizeObserved(string observed, string normalizedExpected)
    {
        var expectsAscii = normalizedExpected.Any(character => character <= 0x7F && char.IsLetterOrDigit(character));
        var expectsNonAscii = normalizedExpected.Any(character => character > 0x7F && char.IsLetterOrDigit(character));
        return new string(Normalize(observed)
            .Where(character => !char.IsLetterOrDigit(character)
                || (character <= 0x7F ? expectsAscii : expectsNonAscii))
            .ToArray());
    }
}
