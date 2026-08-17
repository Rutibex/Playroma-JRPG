using System.Diagnostics;
using System.Text.Json;

namespace Playroma.JRPG.FormulaSandbox;

public sealed record FormulaBattlerSnapshot(
    string Name,
    int Level,
    double Hp,
    double Mhp,
    double Mp,
    double Mmp,
    double Tp,
    double Atk,
    double Def,
    double Mat,
    double Mdf,
    double Agi,
    double Luk,
    IReadOnlyDictionary<string, double>? Extra = null)
{
    public IReadOnlyDictionary<string, double> ToValues()
    {
        Dictionary<string, double> values = new(StringComparer.OrdinalIgnoreCase)
        {
            ["level"] = Level,
            ["hp"] = Hp,
            ["mhp"] = Mhp,
            ["mp"] = Mp,
            ["mmp"] = Mmp,
            ["tp"] = Tp,
            ["atk"] = Atk,
            ["def"] = Def,
            ["mat"] = Mat,
            ["mdf"] = Mdf,
            ["agi"] = Agi,
            ["luk"] = Luk
        };
        if (Extra is not null)
        {
            foreach ((string key, double value) in Extra) values[key] = value;
        }
        return values;
    }
}

public sealed record FormulaItemSnapshot(
    int Id,
    string Name,
    int DamageType,
    int ElementId,
    int Variance,
    bool Critical,
    IReadOnlyDictionary<string, double>? Extra = null);

public sealed record FormulaSandboxRequest(
    string Formula,
    FormulaBattlerSnapshot User,
    FormulaBattlerSnapshot Target,
    FormulaItemSnapshot Item,
    IReadOnlyDictionary<int, double>? Variables = null,
    bool ApplyCritical = false,
    int SampleCount = 100,
    int Seed = 1);

public sealed record FormulaSandboxResult(
    bool Success,
    string Mode,
    double RawResult,
    double BaseDamage,
    double CriticalDamage,
    double MinimumSample,
    double AverageSample,
    double MaximumSample,
    IReadOnlyList<double> Samples,
    IReadOnlyList<string> ReferencedProperties,
    IReadOnlyList<int> ReferencedVariables,
    string? Error,
    long ElapsedMilliseconds)
{
    public static FormulaSandboxResult Failure(string error, long elapsedMilliseconds = 0) => new(
        false,
        "MV Core Approximation",
        0,
        0,
        0,
        0,
        0,
        0,
        [],
        [],
        [],
        error,
        elapsedMilliseconds);
}

public interface IFormulaSandbox
{
    ValueTask<FormulaSandboxResult> EvaluateAsync(
        FormulaSandboxRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Runs formulas in the dedicated Playroma formula worker. The worker exposes only a numeric
/// expression evaluator; it has no project, filesystem, network, Node, or CLR object surface.
/// </summary>
public sealed class FormulaSandboxClient : IFormulaSandbox
{
    private readonly string _workerPath;
    private readonly TimeSpan _timeout;
    private readonly long _memoryLimitBytes;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public FormulaSandboxClient(
        string workerPath,
        TimeSpan? timeout = null,
        long memoryLimitBytes = 128L * 1024 * 1024)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerPath);
        _workerPath = workerPath;
        _timeout = timeout ?? TimeSpan.FromSeconds(2);
        _memoryLimitBytes = Math.Max(16L * 1024 * 1024, memoryLimitBytes);
    }

    public static FormulaSandboxClient CreateDefault()
    {
        string baseDirectory = AppContext.BaseDirectory;
        string executable = Path.Combine(baseDirectory, "Playroma.JRPG.FormulaWorker.exe");
        if (File.Exists(executable)) return new FormulaSandboxClient(executable);
        string assembly = Path.Combine(baseDirectory, "Playroma.JRPG.FormulaWorker.dll");
        return new FormulaSandboxClient(assembly);
    }

    public async ValueTask<FormulaSandboxResult> EvaluateAsync(
        FormulaSandboxRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Stopwatch stopwatch = Stopwatch.StartNew();
        if (!File.Exists(_workerPath))
        {
            return FormulaSandboxResult.Failure($"Formula worker was not found at '{_workerPath}'. Build Playroma.JRPG.FormulaWorker and place it beside the editor.");
        }

        ProcessStartInfo start = BuildStartInfo(_workerPath);
        using Process process = new() { StartInfo = start, EnableRaisingEvents = true };
        try
        {
            if (!process.Start()) return FormulaSandboxResult.Failure("The formula worker could not be started.");
            string requestJson = JsonSerializer.Serialize(request, JsonOptions);
            await process.StandardInput.WriteLineAsync(requestJson.AsMemory(), cancellationToken).ConfigureAwait(false);
            process.StandardInput.Close();

            Task<string?> outputTask = process.StandardOutput.ReadLineAsync(cancellationToken).AsTask();
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_timeout);
            while (!outputTask.IsCompleted)
            {
                await Task.Delay(25, timeout.Token).ConfigureAwait(false);
                if (!process.HasExited && process.WorkingSet64 > _memoryLimitBytes)
                {
                    process.Kill(entireProcessTree: true);
                    return FormulaSandboxResult.Failure("The formula worker exceeded its memory limit.", stopwatch.ElapsedMilliseconds);
                }
            }

            string? output = await outputTask.ConfigureAwait(false);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(output))
            {
                string error = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                return FormulaSandboxResult.Failure(string.IsNullOrWhiteSpace(error) ? "The formula worker returned no result." : error, stopwatch.ElapsedMilliseconds);
            }
            FormulaSandboxResult? result = JsonSerializer.Deserialize<FormulaSandboxResult>(output, JsonOptions);
            return result ?? FormulaSandboxResult.Failure("The formula worker returned an invalid response.", stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            return FormulaSandboxResult.Failure($"Formula execution exceeded {_timeout.TotalMilliseconds:0} ms.", stopwatch.ElapsedMilliseconds);
        }
        catch (Exception exception)
        {
            TryKill(process);
            return FormulaSandboxResult.Failure(exception.Message, stopwatch.ElapsedMilliseconds);
        }
    }

    private static ProcessStartInfo BuildStartInfo(string workerPath)
    {
        bool isDll = string.Equals(Path.GetExtension(workerPath), ".dll", StringComparison.OrdinalIgnoreCase);
        ProcessStartInfo start = new()
        {
            FileName = isDll ? "dotnet" : workerPath,
            Arguments = isDll ? $"\"{workerPath}\"" : string.Empty,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetTempPath()
        };
        // Preserve PATH so a framework-dependent worker can launch through dotnet.
        start.Environment["DOTNET_EnableDiagnostics"] = "0";
        return start;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }
}
