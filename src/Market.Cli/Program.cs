namespace Market.Cli;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        using var cancellationSource = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellationSource.Cancel();
        };
        Console.CancelKeyPress += handler;

        try
        {
            var environment = Environment.GetEnvironmentVariables()
                .Cast<System.Collections.DictionaryEntry>()
                .Where(entry => entry.Key is string)
                .ToDictionary(
                    entry => (string)entry.Key,
                    entry => entry.Value?.ToString(),
                    StringComparer.OrdinalIgnoreCase);
            var application = new CliApplication(
                environment,
                Console.Out,
                Console.Error,
                settings => new AlpacaMarketDataProvider(settings),
                TimeProvider.System);
            return await application.RunAsync(args, cancellationSource.Token).ConfigureAwait(false);
        }
        finally
        {
            Console.CancelKeyPress -= handler;
        }
    }
}
