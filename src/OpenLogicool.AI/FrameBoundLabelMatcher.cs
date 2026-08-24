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

    public static double Similarity(string observed, string expected)
    {
        var normalizedExpected = Normalize(expected);
        if (normalizedExpected.Length == 0)
        {
            return 0;
        }

        var normalizedObserved = NormalizeObserved(observed, normalizedExpected);
        if (normalizedObserved.Length == 0)
        {
            return 0;
        }

        var previous = Enumerable.Range(0, normalizedExpected.Length + 1).ToArray();
        var current = new int[normalizedExpected.Length + 1];
        for (var observedIndex = 1; observedIndex <= normalizedObserved.Length; observedIndex++)
        {
            current[0] = observedIndex;
            for (var expectedIndex = 1; expectedIndex <= normalizedExpected.Length; expectedIndex++)
            {
                var substitution = previous[expectedIndex - 1]
                    + (normalizedObserved[observedIndex - 1] == normalizedExpected[expectedIndex - 1] ? 0 : 1);
                current[expectedIndex] = Math.Min(
                    Math.Min(previous[expectedIndex] + 1, current[expectedIndex - 1] + 1),
                    substitution);
            }
            (previous, current) = (current, previous);
        }

        var distance = previous[normalizedExpected.Length];
        return 1 - distance / (double)Math.Max(normalizedObserved.Length, normalizedExpected.Length);
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
