using System.Diagnostics;
using System.Text.Json;

namespace TubeDrop.Core.Fingerprint;

public sealed record AudioFingerprint(int DurationSeconds, string Fingerprint);

/// <summary>Computes a Chromaprint acoustic fingerprint for an audio file.</summary>
public interface IAudioFingerprinter
{
    /// <summary>True when the fpcalc (Chromaprint) tool is available.</summary>
    bool IsAvailable { get; }

    Task<AudioFingerprint?> ComputeAsync(string path, CancellationToken ct = default);
}

/// <summary>
/// Runs the Chromaprint <c>fpcalc</c> tool (shelled out) to fingerprint a file.
/// fpcalc is not bundled — resolved from an explicit path, the app folder, or
/// PATH. When it can't be found, <see cref="IsAvailable"/> is false and callers
/// skip fingerprinting (documented no-op, never a fake result).
/// </summary>
public sealed class FpcalcFingerprinter : IAudioFingerprinter
{
    private readonly string? _fpcalcPath;

    public FpcalcFingerprinter(string? explicitPath = null)
    {
        _fpcalcPath = Resolve(explicitPath);
    }

    public bool IsAvailable => _fpcalcPath is not null;

    public async Task<AudioFingerprint?> ComputeAsync(string path, CancellationToken ct = default)
    {
        if (_fpcalcPath is null || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var psi = new ProcessStartInfo(_fpcalcPath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-json");
            psi.ArgumentList.Add(path);

            using var process = Process.Start(psi);
            if (process is null)
            {
                return null;
            }

            var stdout = await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            if (process.ExitCode != 0 || stdout.Length == 0)
            {
                return null;
            }

            return Parse(stdout);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Parses fpcalc -json output: { "duration": 245.0, "fingerprint": "AQAB..." }.</summary>
    internal static AudioFingerprint? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("fingerprint", out var fp) || fp.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var duration = root.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number
                ? (int)Math.Round(d.GetDouble())
                : 0;
            return new AudioFingerprint(duration, fp.GetString()!);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Resolve(string? explicitPath)
    {
        if (!string.IsNullOrEmpty(explicitPath) && File.Exists(explicitPath))
        {
            return explicitPath;
        }

        var exeName = OperatingSystem.IsWindows() ? "fpcalc.exe" : "fpcalc";

        // Next to the app.
        var beside = Path.Combine(AppContext.BaseDirectory, exeName);
        if (File.Exists(beside))
        {
            return beside;
        }

        // On PATH.
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir, exeName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (Exception)
            {
                // ignore malformed PATH entries
            }
        }

        return null;
    }
}
