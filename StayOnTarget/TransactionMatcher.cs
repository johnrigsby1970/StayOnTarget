using System.Text.RegularExpressions;
using FuzzySharp;

namespace StayOnTarget;

public class TransactionMatcher {
    //AI helped
    // Common noise words to clean out during normalization
    private static readonly Regex BusinessSuffixes = new Regex(
        @"\b(store|inc|co|llc|corp|corporation)\b", 
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex NonAlphanumeric = new Regex(
        @"[^\w\s]", 
        RegexOptions.Compiled
    );

    private static readonly Regex MultipleSpaces = new Regex(
        @"\s+", 
        RegexOptions.Compiled
    );

    /// <summary>
    /// Cleans incoming transaction text by removing noise, store suffixes, and punctuation.
    /// </summary>
    public static string NormalizeName(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName)) 
            return string.Empty;

        // 1. Lowercase
        string result = rawName.ToLowerInvariant();

        // 2. Remove non-alphanumeric chars (keep letters, digits, whitespace)
        result = NonAlphanumeric.Replace(result, " ");

        // 3. Remove common suffix/noise words
        result = BusinessSuffixes.Replace(result, " ");

        // 4. Collapse multiple spaces into one and trim
        result = MultipleSpaces.Replace(result, " ").Trim();

        return result;
    }

    /// <summary>
    /// Calculates a similarity percentage between an imported record and a manual record.
    /// Returns an integer score between 0 and 100.
    /// </summary>
    public static int GetMatchScore(string importedName, string manualName)
    {
        string cleanImported = NormalizeName(importedName);
        string cleanManual = NormalizeName(manualName);

        // Instant 100% score for exact normalized match
        if (cleanImported.Equals(cleanManual, StringComparison.OrdinalIgnoreCase))
            return 100;

        // TokenSetRatio works best for cases like "Lowe's #1897" vs "Lowe's"
        return Fuzz.TokenSetRatio(cleanImported, cleanManual);
    }
    
    public static bool IsMatch(string manual, string imported)
    {
        string cleanManual = NormalizeName(manual);
        string cleanImported = NormalizeName(imported);

        // 1. Both-way substring check (Instant 100% match if one is inside the other)
        if (cleanManual.Contains(cleanImported) || cleanImported.Contains(cleanManual))
        {
            return true; 
        }

        // 2. Fallback to Levenshtein for typos (like "Lowes" vs "Lwoes")
        double similarity = GetLevenshteinSimilarity(cleanManual, cleanImported);
        return similarity >= 0.80; // 80% similarity threshold
    }
    
    public static double GetLevenshteinSimilarity(string s, string t)
    {
        if (string.IsNullOrEmpty(s)) return string.IsNullOrEmpty(t) ? 1.0 : 0.0;
        if (string.IsNullOrEmpty(t)) return 0.0;

        int distance = LevenshteinDistance(s, t);
        int maxLength = Math.Max(s.Length, t.Length);

        // Symmetric percentage calculation
        return 1.0 - ((double)distance / maxLength);
    }
    
    public static int LevenshteinDistance(string source, string target)
    {
        if (string.IsNullOrEmpty(source)) return target?.Length ?? 0;
        if (string.IsNullOrEmpty(target)) return source.Length;

        int sourceLength = source.Length;
        int targetLength = target.Length;

        var distance = new int[sourceLength + 1, targetLength + 1];

        // Initialize rows and columns
        for (int i = 0; i <= sourceLength; distance[i, 0] = i++) { }
        for (int j = 0; j <= targetLength; distance[0, j] = j++) { }

        // Calculate matrix
        for (int i = 1; i <= sourceLength; i++)
        {
            for (int j = 1; j <= targetLength; j++)
            {
                int cost = (target[j - 1] == source[i - 1]) ? 0 : 1;

                distance[i, j] = Math.Min(
                    Math.Min(distance[i - 1, j] + 1, distance[i, j - 1] + 1),
                    distance[i - 1, j - 1] + cost
                );
            }
        }

        return distance[sourceLength, targetLength];
    }
}