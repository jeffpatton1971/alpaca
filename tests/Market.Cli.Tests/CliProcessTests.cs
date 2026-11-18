using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace Market.Cli.Tests;

public sealed class CliProcessTests
{
    [Theory]
    [InlineData("--help", "Usage:")]
    [InlineData("--version", "0.1.0")]
    public async Task StartupIgnoresDifferentlyCasedUnrelatedEnvironmentNames(string command, string expected)
    {
        var startInfo = StartInfo(command);
        startInfo.Environment["HTTP_PROXY"] = "http://127.0.0.1:1";
        startInfo.Environment["http_proxy"] = "http://127.0.0.1:2";
        var exit = await RunAsync(startInfo);
        Assert.Equal(0, exit.Code);
        Assert.Contains(expected, exit.Output);
        Assert.Empty(exit.Error);
    }

    [Fact]
    public async Task UnixSigintDuringBlockedDoctorReturnsJsonAndExit130()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // Windows caller cancellation is covered in CliApplicationTests.
        }

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var startInfo = StartInfo("doctor", "--check-connection", "--json");
        startInfo.Environment["ALPACA_API_KEY"] = "synthetic-key";
        startInfo.Environment["ALPACA_API_SECRET"] = "synthetic-secret";
        startInfo.Environment["ALPACA_DATA_URL"] = $"https://{listener.LocalEndpoint}";
        startInfo.Environment["NO_PROXY"] = "127.0.0.1";
        startInfo.Environment["no_proxy"] = "127.0.0.1";
        using var process = Process.Start(startInfo)!;
        try
        {
            // Hold the TLS handshake open so the actual CLI has a read in flight.
            using var connection = await listener.AcceptTcpClientAsync(deadline.Token);
            var signal = new ProcessStartInfo("kill") { UseShellExecute = false };
            signal.ArgumentList.Add("-INT");
            signal.ArgumentList.Add(process.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            using var sender = Process.Start(signal)!;
            await sender.WaitForExitAsync(deadline.Token);
            Assert.Equal(0, sender.ExitCode);

            var exit = await ReadExitAsync(process, deadline.Token);
            Assert.Equal(130, exit.Code);
            using var json = JsonDocument.Parse(exit.Output);
            Assert.Contains(json.RootElement.GetProperty("diagnostics").EnumerateArray(),
                diagnostic => diagnostic.GetProperty("message").GetString()!.StartsWith("REQUEST_CANCELLED:", StringComparison.Ordinal));
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
        }
    }

    private static ProcessStartInfo StartInfo(params string[] args)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(typeof(Market.Cli.Program).Assembly.Location);
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }
        start.Environment.Remove("ALPACA_API_KEY");
        start.Environment.Remove("ALPACA_API_SECRET");
        return start;
    }

    private static async Task<(int Code, string Output, string Error)> RunAsync(ProcessStartInfo start)
    {
        using var process = Process.Start(start)!;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            return await ReadExitAsync(process, deadline.Token);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
        }
    }

    private static async Task<(int Code, string Output, string Error)> ReadExitAsync(Process process, CancellationToken token)
    {
        var output = process.StandardOutput.ReadToEndAsync(token);
        var error = process.StandardError.ReadToEndAsync(token);
        await process.WaitForExitAsync(token);
        return (process.ExitCode, await output, await error);
    }
}
