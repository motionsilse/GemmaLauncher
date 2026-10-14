using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using GemmaLauncher.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GemmaLauncher.Tests;

public static class GatewayTests
{
    public static async Task RunAsync()
    {
        var preset = new TaskPreset { Name = "Translation", Prompt = "Preserve every subject.", Glossary = [new("moon", "달")], Temperature = .7 };
        var original = JsonNode.Parse("""{"messages":[{"role":"system","content":"Return JSON"},{"role":"user","content":"Translate this"}],"temperature":0,"stream":true,"response_format":{"type":"json_object"},"tools":[]}""")!.AsObject();
        var transformed = preset.Apply(original, true);
        Assert(transformed["messages"]![1]!["content"]!.ToString().Contains("달"), "Dictionary terms must reach the model as native Unicode, not literal escape sequences.");
        var completionPreset = preset with { MaxTokens = 30 };
        var batch = JsonNode.Parse("""{"prompt":["One","Two"]}""")!.AsObject();
        var completed = completionPreset.Apply(batch, false);
        Assert(completed["prompt"]![1]!.ToString().EndsWith("Two") && completed["max_tokens"]!.GetValue<int>() == 30, "OpenAI text completions must support string batches and their native limit field.");
        Assert(completionPreset.Apply(batch, false, nativeCompletion: true)["n_predict"]!.GetValue<int>() == 30, "Native llama completions must use n_predict.");
        var tokens = JsonNode.Parse("""{"prompt":[1,2,3],"n_predict":5}""")!.AsObject();
        Assert(TaskPreset.Empty.Apply(tokens, false).ToJsonString() == tokens.ToJsonString(), "Without saved instructions, token prompts must pass unchanged.");
        Assert(original["messages"]![1]!["content"]!.ToString() == "Translate this", "Composition must not mutate client objects.");
        Assert(transformed["messages"]![0]!.ToJsonString() == original["messages"]![0]!.ToJsonString(), "Client system instructions must survive.");
        Assert(transformed["temperature"]!.GetValue<int>() == 0 && transformed["response_format"]!.ToJsonString() == original["response_format"]!.ToJsonString(), "Explicit client generation and output constraints win.");
        var multimodal = JsonNode.Parse("""{"messages":[{"role":"user","content":[{"type":"image_url","image_url":{"url":"data:image/png;base64,AA=="}}]}]}""")!.AsObject();
        Assert(preset.Apply(multimodal, true)["messages"]![0]!["content"]!.AsArray().Count == 2, "Image parts must survive prompt insertion.");
        Assert(TaskPreset.ReadGlossary("source,target\n\"Moon, shard\",달의 파편").Single().Source == "Moon, shard", "CSV quotes must be preserved.");
        var observed = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        var aborted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var builder = WebApplication.CreateSlimBuilder(); builder.Logging.ClearProviders(); builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        await using var upstream = builder.Build();
        upstream.Run(async context =>
        {
            if (context.Request.Path == "/v1/chat/completions")
            {
                var body = (await JsonNode.ParseAsync(context.Request.Body))!.AsObject(); observed.TrySetResult(body);
                context.Response.ContentType = "text/event-stream";
                await context.Response.WriteAsync("data: {\"token\":\"hello\"}\n\n", context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
                try { await Task.Delay(Timeout.Infinite, context.RequestAborted); }
                catch (OperationCanceledException) { aborted.TrySetResult(); }
            }
            else { context.Response.StatusCode = 409; await context.Response.WriteAsync("upstream error"); }
        });
        await upstream.StartAsync();
        var backendPort = new Uri(upstream.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()).Port;
        await using var gateway = new RequestGateway();
        await gateway.StartAsync(0, backendPort, preset);
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri($"http://127.0.0.1:{gateway.PublicPort}/"), Timeout = TimeSpan.FromSeconds(10) };
        using (var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions") { Content = new StringContent(original.ToJsonString(), Encoding.UTF8, "application/json") };
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel.Token);
            await using var stream = await response.Content.ReadAsStreamAsync(cancel.Token);
            var buffer = new byte[1024]; var count = await stream.ReadAsync(buffer, cancel.Token);
            Assert(Encoding.UTF8.GetString(buffer, 0, count).Contains("hello"), "SSE must arrive before the upstream request completes.");
            var forwarded = await observed.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert(forwarded["messages"]![1]!["content"]!.ToString().Contains("Preserve every subject."), "API calls must receive saved instructions.");
            cancel.Cancel();
        }
        await aborted.Task.WaitAsync(TimeSpan.FromSeconds(6));
        using var error = await client.GetAsync("props");
        Assert((int)error.StatusCode == 409 && await error.Content.ReadAsStringAsync() == "upstream error", "Upstream error status/body must survive.");
        using var malformed = await client.PostAsync("v1/chat/completions", new StringContent("broken JSON"));
        Assert(malformed.StatusCode == HttpStatusCode.BadRequest, "Invalid JSON must return a useful client error.");
        using var foreignOrigin = new HttpRequestMessage(HttpMethod.Get, "props");
        foreignOrigin.Headers.Add("Origin", "https://example.com");
        using var forbidden = await client.SendAsync(foreignOrigin);
        Assert(forbidden.StatusCode == HttpStatusCode.Forbidden, "Remote browser origins must not access the local gateway.");
        await gateway.StopAsync();
        await gateway.StartAsync(0, backendPort, TaskPreset.Empty);
        Assert(gateway.PublicPort > 0, "Gateway must support stop/start reuse.");
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
