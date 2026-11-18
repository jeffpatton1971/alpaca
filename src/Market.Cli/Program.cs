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
            var application = new CliApplication(
                MarketConfiguration.ReadEnvironment(),
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
