using System.Diagnostics;
using System.Text.Json;
using Playroma.Text.Core.AI;

namespace Playroma.Text.Generators.AI;

/// <summary>
/// Opt-in local-provider bridge. The configured executable receives one JSON request on stdin
/// and must return one JSON NeuralTextResponse-shaped object (or {"text":"..."}) on stdout.
/// No shell is involved and no network/API dependency is built into Playroma.
/// </summary>
public sealed class ProcessNeuralTextProvider : INeuralTextProvider
{
    private readonly string _executablePath;
    private readonly string _arguments;
    private readonly string? _workingDirectory;
    private readonly TimeSpan _timeout;

    public ProcessNeuralTextProvider(string id, string displayName, string executablePath, string? arguments = null, string? workingDirectory = null, TimeSpan? timeout = null)
    {
        Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("Provider id is required.", nameof(id)) : id.Trim();
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? Id : displayName.Trim();
        _executablePath = string.IsNullOrWhiteSpace(executablePath) ? throw new ArgumentException("Executable path is required.", nameof(executablePath)) : Path.GetFullPath(executablePath);
        _arguments = arguments ?? string.Empty;
        _workingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? null : Path.GetFullPath(workingDirectory);
        _timeout = timeout is { } t ? TimeSpan.FromSeconds(Math.Clamp(t.TotalSeconds, 1, 300)) : TimeSpan.FromSeconds(60);
    }

    public string Id { get; }
    public string DisplayName { get; }
    public NeuralProviderCapabilities Capabilities { get; } = new(SupportsSeed: true, SupportsSystemPrompt: true, MaximumPromptCharacters: 32_000, MaximumOutputCharacters: 8_000);

    public async ValueTask<NeuralTextResponse> GenerateAsync(NeuralTextRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!File.Exists(_executablePath)) throw new FileNotFoundException("Configured AI provider executable was not found.", _executablePath);
        if (request.Prompt.Length > Capabilities.MaximumPromptCharacters) throw new InvalidOperationException($"Prompt exceeds provider limit of {Capabilities.MaximumPromptCharacters} characters.");

        var psi = new ProcessStartInfo
        {
            FileName = _executablePath,
            Arguments = _arguments,
            WorkingDirectory = _workingDirectory ?? Path.GetDirectoryName(_executablePath) ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        if (!process.Start()) throw new InvalidOperationException("Could not start configured AI provider process.");
        var json = JsonSerializer.Serialize(request, Playroma.Text.Core.Generation.RecipeJson.Options);
        await process.StandardInput.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
        process.StandardInput.Close();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_timeout);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
        await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        if (process.ExitCode != 0) throw new InvalidOperationException($"AI provider exited with code {process.ExitCode}. {stderr}".Trim());
        if (string.IsNullOrWhiteSpace(stdout)) throw new InvalidOperationException("AI provider returned no output.");

        string text;
        string? model = null;
        try
        {
            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;
            text = root.ValueKind == JsonValueKind.String ? root.GetString() ?? string.Empty :
                root.TryGetProperty("text", out var t) ? t.GetString() ?? string.Empty : string.Empty;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String) model = m.GetString();
        }
        catch (JsonException)
        {
            text = stdout.Trim();
        }
        var max = Math.Clamp(request.MaximumOutputCharacters, 1, Capabilities.MaximumOutputCharacters);
        if (text.Length > max) text = text[..max];
        return new NeuralTextResponse(text, Id, model);
    }
}
