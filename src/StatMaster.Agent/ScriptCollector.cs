using System.Diagnostics;
using System.Text;

namespace StatMaster.Agent;

public sealed class ScriptCollector
{
    public async Task<string> RunAsync(ScriptMetricDefinition def, CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = def.FileName,
            Arguments = def.Arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };

        if (!process.Start())
            throw new InvalidOperationException("script-start-failed");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(def.TimeoutMs);

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        Task waitTask = process.WaitForExitAsync(timeoutCts.Token);

        try
        {
            await waitTask;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException("script-timeout");
        }

        string stdout = await stdoutTask;
        string stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            string err = Limit(stderr, def.MaxOutputChars).Trim();
            throw new InvalidOperationException($"script-exit-code:{process.ExitCode}|{err}");
        }

        return Limit(stdout, def.MaxOutputChars).Trim();
    }

    private static string Limit(string text, int maxChars)
    {
        if (string.IsNullOrEmpty(text) || maxChars <= 0)
            return string.Empty;

        return text.Length <= maxChars ? text : text[..maxChars];
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // best effort
        }
    }
}
