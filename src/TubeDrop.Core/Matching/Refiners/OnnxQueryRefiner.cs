using Microsoft.Extensions.Logging;
using TubeDrop.Core.Ingestion;

namespace TubeDrop.Core.Matching.Refiners;

/// <summary>
/// Second rung of the fallback ladder: a local ONNX model that would clean and
/// romanize noisy titles into better search queries.
///
/// STATUS — no-op by design. No permissive, CPU-friendly, ≤100 MB model has been
/// identified that reliably improves on the deterministic refiner for this task,
/// so rather than fake it, the capability flag exists and this refiner is wired
/// into the ladder but returns no extra queries when no model is present (logging
/// why once). When a suitable model is found, load it here
/// (Microsoft.ML.OnnxRuntime, CPU) and emit refined queries; the surrounding
/// pipeline needs no changes. AnyAscii already covers rule-based transliteration.
/// </summary>
public sealed class OnnxQueryRefiner(IModelProvider modelProvider, ILogger<OnnxQueryRefiner> logger) : IQueryRefiner
{
    private bool _warned;

    public string Name => "onnx";

    public Task<IReadOnlyList<string>> RefineAsync(TrackInfo track, CancellationToken ct = default)
    {
        if (!modelProvider.IsModelAvailable)
        {
            if (!_warned)
            {
                _warned = true;
                logger.LogInformation(
                    "ONNX refiner enabled but no model is present — skipping. " +
                    "The deterministic refiner remains active.");
            }

            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        // Reserved for a future bundled/downloaded model. Intentionally empty
        // rather than a fabricated success path (§15).
        return Task.FromResult<IReadOnlyList<string>>([]);
    }
}

/// <summary>Locates the on-demand ONNX model (§8.2). Currently never available — documented no-go.</summary>
public interface IModelProvider
{
    bool IsModelAvailable { get; }
    string ModelDirectory { get; }
}

public sealed class LocalModelProvider : IModelProvider
{
    public LocalModelProvider(string? modelDirectory = null)
    {
        ModelDirectory = modelDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TubeDrop", "models");
    }

    public string ModelDirectory { get; }

    /// <summary>True only when a real model file has been placed in the model directory.</summary>
    public bool IsModelAvailable =>
        Directory.Exists(ModelDirectory) && Directory.EnumerateFiles(ModelDirectory, "*.onnx").Any();
}
