using GemmaLauncher.Tests;
using GemmaLauncher.Core;

try
{
    if (args.Contains("--check-failure")) throw new InvalidOperationException("Deliberate test runner failure for exit-boundary verification.");
    if (await SessionTests.TryRunChildAsync(args)) return;
    Localization.Current.SetLanguage("ko");
    if (args.Length == 2 && args[0] == "--inspect-gguf") { var info = GgufMetadata.Read(args[1]); Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(info with { ChatTemplate = "" })); return; }
    if (args.Contains("--hf-smoke")) { await HuggingFaceTests.SmokeAsync(); return; }
    await HuggingFaceTests.RunAsync();
    Console.WriteLine("Hugging Face tests passed.");
    if (args.Contains("--localization-tests")) { await LocalizationTests.RunAsync(); Console.WriteLine("Localization tests passed."); return; }
    if (args.Contains("--real-model-smoke")) { await RealModelSmoke.RunAsync(); return; }
    if (args.Contains("--session-tests")) { await SessionTests.RunAsync(); Console.WriteLine("Session tests passed."); return; }
    if (args.Contains("--library-tests")) { await LibraryTests.RunAsync(); Console.WriteLine("Library tests passed."); return; }
    if (args.Contains("--gateway-tests")) { await GatewayTests.RunAsync(); Console.WriteLine("Gateway tests passed."); return; }
    await GatewayTests.RunAsync();
    Console.WriteLine("Gateway tests passed.");
    await LibraryTests.RunAsync();
    Console.WriteLine("Library tests passed.");
    await InstallationTests.RunAsync();
    Console.WriteLine("Installation tests passed.");
    await SessionTests.RunAsync();
    Console.WriteLine("Session tests passed.");
    await CatalogTests.RunAsync();
    Console.WriteLine("Catalog and settings tests passed.");
    await MetricsTests.RunAsync();
    Console.WriteLine("Process memory tests passed.");
    await PrerequisiteTests.RunAsync();
    Console.WriteLine("Runtime prerequisite tests passed.");
    await LocalizationTests.RunAsync();
    Console.WriteLine("Localization tests passed.");
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    Environment.ExitCode = 1;
}
