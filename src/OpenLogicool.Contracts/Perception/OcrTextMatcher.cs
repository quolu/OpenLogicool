using System.Text;

namespace OpenLogicool.Contracts.Perception;

public static class OcrTextMatcher
{
    public const double DefaultMinimumSimilarity = 0.55;

    public static string Normalize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return string.Concat(value
            .Normalize(NormalizationForm.FormKC)
            .Where(char.IsLetterOrDigit))
            .ToUpperInvariant();
    }

    public static double Similarity(string left, string right)
    {
        var normalizedLeft = Normalize(left);
        var normalizedRight = Normalize(right);
        if (normalizedLeft.Length == 0 || normalizedRight.Length == 0)
        {
            return 0;
        }
        var previous = Enumerable.Range(0, normalizedRight.Length + 1).ToArray();
        var current = new int[normalizedRight.Length + 1];
        for (var leftIndex = 1; leftIndex <= normalizedLeft.Length; leftIndex++)
        {
            current[0] = leftIndex;
            for (var rightIndex = 1; rightIndex <= normalizedRight.Length; rightIndex++)
            {
                var substitution = previous[rightIndex - 1]
                    + (normalizedLeft[leftIndex - 1] == normalizedRight[rightIndex - 1] ? 0 : 1);
                current[rightIndex] = Math.Min(
                    Math.Min(previous[rightIndex] + 1, current[rightIndex - 1] + 1),
                    substitution);
            }
            (previous, current) = (current, previous);
        }
        return 1 - previous[normalizedRight.Length]
            / (double)Math.Max(normalizedLeft.Length, normalizedRight.Length);
    }

    public static bool IsSimilar(
        string left,
        string right,
        double minimumSimilarity = DefaultMinimumSimilarity) =>
        Similarity(left, right) >= minimumSimilarity;

    public static bool PreferObserved(string saved, string observed) =>
        IsSimilar(saved, observed)
        && Plausibility(observed) > Plausibility(saved);

    private static int Plausibility(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormKC);
        var letters = normalized.Count(char.IsLetter);
        var digits = normalized.Count(char.IsDigit);
        var noise = normalized.Count(character =>
            !char.IsLetterOrDigit(character) && !char.IsWhiteSpace(character));
        var repeated = normalized.Zip(normalized.Skip(1), (left, right) => left == right).Count(equal => equal);
        return letters * 4 + digits * 2 - noise * 5 - repeated * 2;
    }
}
