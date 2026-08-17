using Playroma.JRPG.FormulaSandbox;

FormulaBattlerSnapshot user = new("Hero", 10, 500, 500, 100, 100, 0, 80, 40, 70, 50, 45, 25);
FormulaBattlerSnapshot target = new("Slime", 1, 200, 200, 20, 20, 0, 30, 20, 20, 15, 10, 10);
FormulaSandboxRequest request = new(
    "Math.max(a.atk * 4 - b.def * 2, 0)",
    user,
    target,
    new FormulaItemSnapshot(1, "Attack", 1, -1, 20, true),
    ApplyCritical: true,
    SampleCount: 32,
    Seed: 1234);

int failures = 0;
try
{
    FormulaSandboxResult inProcess = FormulaSandboxEvaluator.Evaluate(request);
    Assert(inProcess.Success, inProcess.Error ?? "Direct evaluator failed.");
    Assert(inProcess.MinimumSample <= inProcess.AverageSample && inProcess.AverageSample <= inProcess.MaximumSample, "Variance order was invalid.");
    Console.WriteLine("PASS Direct formula evaluator");
}
catch (Exception exception)
{
    failures++;
    Console.Error.WriteLine("FAIL Direct formula evaluator: " + exception);
}

try
{
    FormulaSandboxClient client = FormulaSandboxClient.CreateDefault();
    FormulaSandboxResult isolated = await client.EvaluateAsync(request).ConfigureAwait(false);
    Assert(isolated.Success, isolated.Error ?? "Isolated formula worker failed.");
    Assert(isolated.RawResult == 280, $"Expected 280; actual {isolated.RawResult}.");
    Console.WriteLine("PASS Isolated formula worker");
}
catch (Exception exception)
{
    failures++;
    Console.Error.WriteLine("FAIL Isolated formula worker: " + exception);
}

Console.WriteLine(failures == 0 ? "2/2 Phase 9B formula host tests passed." : $"{2 - failures}/2 Phase 9B formula host tests passed.");
return failures == 0 ? 0 : 1;

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
