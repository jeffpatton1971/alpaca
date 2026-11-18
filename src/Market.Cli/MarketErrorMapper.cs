using System.Net;
using System.Net.Sockets;
using Alpaca.Markets;

namespace Market.Cli;

internal static class MarketErrorMapper
{
    public static bool IsProviderFailure(Exception exception) =>
        exception is RestClientErrorException or RequestValidationException or HttpRequestException
            or SocketException or TimeoutException or IOException or Newtonsoft.Json.JsonException
            or UnexpectedProviderResponseException;

    public static MarketError FromProviderException(Exception exception, bool timedOut = false)
    {
        if (timedOut)
        {
            return new MarketError("NETWORK_TIMEOUT", "The Alpaca request timed out.");
        }

        return exception switch
        {
            RestClientErrorException restException => FromStatusCode(restException.HttpStatusCode),
            HttpRequestException requestException when requestException.StatusCode.HasValue =>
                FromStatusCode(requestException.StatusCode),
            HttpRequestException or SocketException or IOException =>
                new MarketError("NETWORK_ERROR", "The Alpaca request could not be completed."),
            RequestValidationException =>
                new MarketError("UNEXPECTED_PROVIDER_RESPONSE", "The Alpaca request could not be validated."),
            Newtonsoft.Json.JsonException or UnexpectedProviderResponseException =>
                new MarketError("UNEXPECTED_PROVIDER_RESPONSE", "Alpaca returned an invalid response."),
            OperationCanceledException =>
                new MarketError("REQUEST_CANCELLED", "The market-data request was cancelled."),
            TimeoutException =>
                new MarketError("NETWORK_TIMEOUT", "The Alpaca request timed out."),
            _ => new MarketError("PROVIDER_UNAVAILABLE", "The Alpaca service could not complete the request.")
        };
    }

    public static int ExitCode(MarketError error) =>
        error.Code switch
        {
            "MISSING_CONFIGURATION" or "INVALID_CONFIGURATION" => 3,
            "INVALID_SYMBOL" or "INVALID_ARGUMENTS" => 2,
            "TOO_MANY_SYMBOLS" => 2,
            "REQUEST_CANCELLED" => 130,
            "AUTHENTICATION_FAILED" or "UNAUTHORIZED_DATA_FEED" or "RATE_LIMITED"
                or "NETWORK_ERROR" or "NETWORK_TIMEOUT" or "PROVIDER_UNAVAILABLE"
                or "UNEXPECTED_PROVIDER_RESPONSE" => 4,
            _ => 1
        };

    private static MarketError FromStatusCode(HttpStatusCode? statusCode) =>
        statusCode switch
        {
            HttpStatusCode.Unauthorized => new MarketError("AUTHENTICATION_FAILED", "Market data authentication failed."),
            HttpStatusCode.Forbidden => new MarketError("UNAUTHORIZED_DATA_FEED", "The configured Alpaca data feed is not authorized."),
            HttpStatusCode.TooManyRequests => new MarketError("RATE_LIMITED", "The Alpaca request was rate limited."),
            >= HttpStatusCode.InternalServerError =>
                new MarketError("PROVIDER_UNAVAILABLE", "The Alpaca service is temporarily unavailable."),
            _ => new MarketError("PROVIDER_UNAVAILABLE", "The Alpaca service could not complete the request.")
        };
}

internal sealed class UnexpectedProviderResponseException : Exception;
