using System.Text.Json;
using Playroma.JRPG.FormulaSandbox;

JsonSerializerOptions options = new(JsonSerializerDefaults.Web);
try
{
    string? line = await Console.In.ReadLineAsync();
    if (string.IsNullOrWhiteSpace(line))
    {
        Console.WriteLine(JsonSerializer.Serialize(FormulaSandboxResult.Failure("No formula request was supplied."), options));
        return 2;
    }
    FormulaSandboxRequest? request = JsonSerializer.Deserialize<FormulaSandboxRequest>(line, options);
    FormulaSandboxResult result = request is null
        ? FormulaSandboxResult.Failure("The formula request was invalid.")
        : FormulaSandboxEvaluator.Evaluate(request);
    Console.WriteLine(JsonSerializer.Serialize(result, options));
    return result.Success ? 0 : 1;
}
catch (Exception exception)
{
    Console.WriteLine(JsonSerializer.Serialize(FormulaSandboxResult.Failure(exception.Message), options));
    return 1;
}
