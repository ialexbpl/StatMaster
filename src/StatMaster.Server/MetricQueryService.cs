using System.Text;
using StatMaster.Protocol;

namespace StatMaster.Server;

public sealed class MetricQueryService
{
    private readonly MetricQueryTimeout _options;

    public MetricQueryService(MetricQueryTimeout options)
    {
        _options = options;
    }

    //method to query the metric for the given key
    //metric means the data we are collecting and storing in the registry
    public async Task<string> QueryMetricAsync(Stream stream, string key, CancellationToken cancellationToken = default)
    {
        int attempts = Math.Max(1, _options.MaxRetries + 1);

        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(_options.TimeoutMsPerMetric);

                byte[] askPayload = Encoding.UTF8.GetBytes(key);
                await FrameCodec.SendFrameAsync(stream, MessageType.AskForMetric, askPayload, timeoutCts.Token);
                Console.WriteLine($"[Server] AskForMetric sent: {key} (attempt {attempt}/{attempts})");

                ProtocolFrame response = await FrameCodec.ReceiveFrameAsync(stream, timeoutCts.Token);
                while (response.Type == MessageType.Heartbeat)
                {
                    string heartbeatAgentId = Encoding.UTF8.GetString(response.Payload);
                    Console.WriteLine($"[Server] Heartbeat received from '{heartbeatAgentId}'.");
                    response = await FrameCodec.ReceiveFrameAsync(stream, timeoutCts.Token);
                }

                if (response.Type != MessageType.ResponseMetric)
                {
                    throw new InvalidOperationException($"Unexpected frame type: {response.Type}");
                }

                return Encoding.UTF8.GetString(response.Payload);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt >= attempts)
                    throw new TimeoutException($"Metric query timeout for key '{key}' after {attempts} attempt(s).");

                Console.WriteLine($"[Server] Timeout for key '{key}', retrying...");
                await Task.Delay(_options.RetryDelayMs, cancellationToken);
            }
            catch (Exception ex) when (attempt < attempts)
            {
                Console.WriteLine($"[Server] Query failed for key '{key}' (attempt {attempt}/{attempts}): {ex.Message}");
                await Task.Delay(_options.RetryDelayMs, cancellationToken);
            }
        }

        throw new InvalidOperationException($"Metric query failed for key '{key}' after all attempts.");
    }

    //query multiple metrics in one session/stream to pętla po kluczach i wysylam po kolei zapytania ona korzysta z metody QueryMetricAsync
    public async Task<Dictionary<string, string>> QueryMetricsAsync(
        Stream stream,
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in keys)
        {
            string response = await QueryMetricAsync(stream, key, cancellationToken);
            results[key] = response;
        }

        return results;
    }
}

// Responsibility of file: protocol request/response exchange with timeout/retry policy.