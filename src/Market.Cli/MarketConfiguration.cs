using System.Globalization;
using Alpaca.Markets;

namespace Market.Cli;

internal static class MarketConfiguration
{
    private const int DefaultStaleAfterMinutes = 15;

    public static ConfigurationResult Load(IReadOnlyDictionary<string, string?> environment)
    {
        var diagnostics = new List<DoctorDiagnostic>();
        var apiKey = ReadRequired(environment, "ALPACA_API_KEY", diagnostics);
        var apiSecret = ReadRequired(environment, "ALPACA_API_SECRET", diagnostics);

        var feedText = environment.GetValueOrDefault("ALPACA_DATA_FEED");
        MarketDataFeed? selectedFeed = MarketDataFeed.Iex;
        if (string.IsNullOrWhiteSpace(feedText))
        {
            diagnostics.Add(new DoctorDiagnostic("feed", "ok", "Using the default IEX feed."));
        }
        else if (feedText.Equals("iex", StringComparison.OrdinalIgnoreCase))
        {
            selectedFeed = MarketDataFeed.Iex;
            diagnostics.Add(new DoctorDiagnostic("feed", "ok", "Configured feed is IEX."));
        }
        else if (feedText.Equals("sip", StringComparison.OrdinalIgnoreCase))
        {
            selectedFeed = MarketDataFeed.Sip;
            diagnostics.Add(new DoctorDiagnostic("feed", "ok", "Configured feed is SIP."));
        }
        else
        {
            selectedFeed = null;
            diagnostics.Add(new DoctorDiagnostic("feed", "error", "ALPACA_DATA_FEED must be either iex or sip."));
        }

        var endpoint = Environments.Live.AlpacaDataApi;
        var endpointText = environment.GetValueOrDefault("ALPACA_DATA_URL");
        if (!string.IsNullOrWhiteSpace(endpointText))
        {
            if (TryParseEndpoint(endpointText, out var configuredEndpoint))
            {
                endpoint = configuredEndpoint;
                diagnostics.Add(new DoctorDiagnostic("endpoint", "ok", "Configured data endpoint is a valid HTTPS origin."));
            }
            else
            {
                diagnostics.Add(new DoctorDiagnostic("endpoint", "error",
                    "ALPACA_DATA_URL must be an absolute HTTPS origin without credentials, path, query, or fragment."));
            }
        }
        else
        {
            diagnostics.Add(new DoctorDiagnostic("endpoint", "ok", "Using Alpaca's documented live market-data endpoint."));
        }

        var staleAfterMinutes = DefaultStaleAfterMinutes;
        var staleText = environment.GetValueOrDefault("MARKET_STALE_AFTER_MINUTES");
        if (string.IsNullOrWhiteSpace(staleText))
        {
            diagnostics.Add(new DoctorDiagnostic("freshness", "ok", "Using the 15-minute freshness threshold."));
        }
        else if (int.TryParse(staleText, NumberStyles.None, CultureInfo.InvariantCulture, out staleAfterMinutes)
            && staleAfterMinutes > 0)
        {
            diagnostics.Add(new DoctorDiagnostic("freshness", "ok", "Using the configured positive freshness threshold."));
        }
        else
        {
            staleAfterMinutes = DefaultStaleAfterMinutes;
            diagnostics.Add(new DoctorDiagnostic("freshness", "error",
                "MARKET_STALE_AFTER_MINUTES must be a positive whole number."));
        }

        var error = diagnostics.FirstOrDefault(diagnostic => diagnostic.Status == "error");
        if (error is not null)
        {
            var code = error.Check switch
            {
                "apiKey" or "apiSecret" => "MISSING_CONFIGURATION",
                _ => "INVALID_CONFIGURATION"
            };
            return new ConfigurationResult(null, selectedFeed, diagnostics, new MarketError(code, error.Message));
        }

        return new ConfigurationResult(
            new MarketSettings(apiKey!, apiSecret!, endpoint, selectedFeed!.Value, staleAfterMinutes),
            selectedFeed,
            diagnostics,
            null);
    }

    private static string? ReadRequired(
        IReadOnlyDictionary<string, string?> environment,
        string name,
        ICollection<DoctorDiagnostic> diagnostics)
    {
        var value = environment.GetValueOrDefault(name);
        if (!string.IsNullOrWhiteSpace(value))
        {
            diagnostics.Add(new DoctorDiagnostic(name == "ALPACA_API_KEY" ? "apiKey" : "apiSecret", "ok",
                $"{name} is configured."));
            return value;
        }

        diagnostics.Add(new DoctorDiagnostic(name == "ALPACA_API_KEY" ? "apiKey" : "apiSecret", "error",
            $"{name} is required for authenticated market-data requests."));
        return null;
    }

    private static bool TryParseEndpoint(string value, out Uri endpoint)
    {
        endpoint = Environments.Live.AlpacaDataApi;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        if (parsed.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrWhiteSpace(parsed.Host)
            || !string.IsNullOrEmpty(parsed.UserInfo)
            || parsed.AbsolutePath != "/"
            || !string.IsNullOrEmpty(parsed.Query)
            || !string.IsNullOrEmpty(parsed.Fragment))
        {
            return false;
        }

        endpoint = parsed;
        return true;
    }
}
