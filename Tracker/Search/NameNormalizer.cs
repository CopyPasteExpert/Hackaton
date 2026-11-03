using System.Text;

namespace Tracker.Search;

// Brings a typed name to a comparable form: lower case, no punctuation,
// single spaces, Latin transliterated to Bulgarian Cyrillic.
public static class NameNormalizer
{
    // Multi-letter combinations must be checked before single letters.
    private static readonly (string Latin, string Cyrillic)[] Combinations =
    [
        ("sht", "щ"), ("dzh", "дж"), ("zh", "ж"), ("ts", "ц"),
        ("ch", "ч"), ("sh", "ш"), ("yu", "ю"), ("ya", "я")
    ];

    private static readonly Dictionary<char, string> Letters = new()
    {
        ['a'] = "а", ['b'] = "б", ['c'] = "к", ['d'] = "д", ['e'] = "е",
        ['f'] = "ф", ['g'] = "г", ['h'] = "х", ['i'] = "и", ['j'] = "дж",
        ['k'] = "к", ['l'] = "л", ['m'] = "м", ['n'] = "н", ['o'] = "о",
        ['p'] = "п", ['q'] = "к", ['r'] = "р", ['s'] = "с", ['t'] = "т",
        ['u'] = "у", ['v'] = "в", ['w'] = "в", ['x'] = "кс", ['y'] = "й",
        ['z'] = "з"
    };

    public static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var lower = input.ToLowerInvariant();
        var builder = new StringBuilder(lower.Length);

        for (var i = 0; i < lower.Length;)
        {
            var combination = Combinations.FirstOrDefault(c =>
                string.CompareOrdinal(lower, i, c.Latin, 0, c.Latin.Length) == 0);

            if (combination.Latin != null)
            {
                builder.Append(combination.Cyrillic);
                i += combination.Latin.Length;
                continue;
            }

            var ch = lower[i];

            if (Letters.TryGetValue(ch, out var cyrillic))
            {
                builder.Append(cyrillic);
            }
            else if (char.IsLetterOrDigit(ch) || ch == '-')
            {
                builder.Append(ch);
            }
            else
            {
                builder.Append(' ');
            }

            i++;
        }

        return string.Join(' ',
            builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
