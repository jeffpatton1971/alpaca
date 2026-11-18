using System.Text.RegularExpressions;

namespace Market.Cli;

internal static partial class SymbolParser
{
    public const int MaximumSymbols = 1000;

    [GeneratedRegex("\\A(?=.{1,10}\\z)[A-Z0-9]+(?:[.-][A-Z0-9]+)*\\z", RegexOptions.CultureInvariant)]
    private static partial Regex SymbolPattern();

    public static IReadOnlyList<SymbolInput> Parse(IEnumerable<string> arguments)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var symbols = new List<SymbolInput>();
        foreach (var argument in arguments)
        {
            var symbol = argument.Trim().ToUpperInvariant();
            if (seen.Add(symbol))
            {
                symbols.Add(new SymbolInput(symbol, SymbolPattern().IsMatch(symbol)));
            }
        }

        return symbols;
    }
}
