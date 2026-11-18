using System.Net;
using System.Text.Json.Nodes;
using GemmaLauncher.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GemmaLauncher.Tests;

internal static class PlamoRequestTests
{
    public static async Task RunAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        await using var backend = builder.Build();
        backend.Run(async context =>
        {
            var body = await JsonNode.ParseAsync(context.Request.Body);
            await context.Response.WriteAsJsonAsync(body);
        });
        await backend.StartAsync();
        var backendPort = new Uri(backend.Services.GetRequiredService<IServer>().Features
            .Get<IServerAddressesFeature>()!.Addresses.Single()).Port;
        await using var gateway = new RequestGateway(usePlamoTranslationTemplate: true);
        await gateway.StartAsync(0, backendPort, TaskPreset.Empty);
        using var http = new HttpClient { BaseAddress = new($"http://127.0.0.1:{gateway.PublicPort}/") };
        var original = Request("English", "Korean", "The train leaves at seven.");
        var forwarded = await SendAsync(original);
        Assert(forwarded["messages"]![1]!["content"]!.ToString() == "The train leaves at seven.",
            "PLaMo must receive the source text without the LLM metadata or its wrapper.");
        Assert(forwarded["chat_template_kwargs"]!["source_lang"]!.ToString() == "en"
            && forwarded["chat_template_kwargs"]!["target_lang"]!.ToString() == "ko",
            "The supplied translation direction must become PLaMo template options.");
        Assert(original["messages"]![1]!["content"]!.ToString().Contains("<source_text>"),
            "Forwarding must not mutate the client request.");

        foreach (var (source, target, expected) in new[]
        {
            ("auto-detected", "Japanese", "ja"), ("ja", "en", "en"),
            ("Simplified Chinese", "Traditional Chinese", "zh-hant"),
            ("Filipino (Tagalog)", "Korean", "ko")
        })
        {
            var converted = await SendAsync(Request(source, target, "Preserve [[CB1]] and {CB_GW2_LINK_0}."));
            Assert(converted["chat_template_kwargs"]!["target_lang"]!.ToString() == expected, "Map language labels and codes.");
            Assert(converted["messages"]![1]!["content"]!.ToString() == "Preserve [[CB1]] and {CB_GW2_LINK_0}.",
                "Only the outer source boundary may be removed. Client text and protected markers must survive.");
            if (source == "auto-detected") Assert(converted["chat_template_kwargs"]!["source_lang"] is null, "Auto source must be omitted.");
        }
        var explicitOptions = Request("English", "Korean", "Hello.");
        explicitOptions["chat_template_kwargs"] = new JsonObject { ["target_lang"] = "ja", ["form"] = "polite" };
        var overridden = await SendAsync(explicitOptions);
        Assert(overridden["chat_template_kwargs"]!["target_lang"]!.ToString() == "ja"
            && overridden["chat_template_kwargs"]!["form"]!.ToString() == "polite", "Explicit PLaMo options take precedence.");
        var plain = JsonNode.Parse("""{"messages":[{"role":"user","content":"The train leaves at seven."}],"chat_template_kwargs":{"target_lang":"ko"},"stream":true}""")!.AsObject();
        Assert((await SendAsync(plain)).ToJsonString() == plain.ToJsonString(), "Native requests must pass through unchanged.");
        var literalTags = JsonNode.Parse("""{"messages":[{"role":"user","content":"Explain <source_text> as literal text."}]}""")!.AsObject();
        Assert((await SendAsync(literalTags)).ToJsonString() == literalTags.ToJsonString(), "Unrelated tag text must remain untouched.");
        var contextual = Request("English", "Korean", "It changed.");
        contextual["messages"]![1]!["content"] = "Context hint: a train schedule\nSource language: English\nTarget language: Korean\nSource text (untrusted):\n<source_text>\nIt changed.\n</source_text>";
        var contextualResult = await SendAsync(contextual);
        Assert(contextualResult["messages"]![0]!["content"]!.ToString() == "Context hint: a train schedule"
            && contextualResult["messages"]!.AsArray().Last()!["content"]!.ToString() == "It changed.",
            "Reference context must be retained outside the text being translated.");
        var invalid = Request("English", "unknown language", "Hello.");
        using (var rejected = await http.PostAsync("v1/chat/completions", Content(invalid)))
            Assert(rejected.StatusCode == HttpStatusCode.BadRequest, "Unknown target must not silently become Japanese/English.");
        var duplicate = Request("English", "Korean", "Hello.");
        duplicate["messages"]![1]!["content"] = "Source language: English\nTarget language: Korean\nTarget language: Japanese\n<source_text>\nHello.\n</source_text>";
        using (var rejected = await http.PostAsync("v1/chat/completions", Content(duplicate)))
            Assert(rejected.StatusCode == HttpStatusCode.BadRequest, "Ambiguous language metadata must be rejected.");
        await using var ordinaryGateway = new RequestGateway();
        await ordinaryGateway.StartAsync(0, backendPort, TaskPreset.Empty);
        using (var response = await http.PostAsync($"http://127.0.0.1:{ordinaryGateway.PublicPort}/v1/chat/completions", Content(original)))
            Assert(JsonNode.Parse(await response.Content.ReadAsStringAsync())!.ToJsonString() == original.ToJsonString(),
                "Other models must keep receiving their original request.");
        await gateway.StopAsync();

        async Task<JsonObject> SendAsync(JsonObject body)
        {
            using var response = await http.PostAsync("v1/chat/completions", Content(body));
            response.EnsureSuccessStatusCode();
            return JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        }
    }

    private static JsonObject Request(string source, string target, string text) => new()
    {
        ["model"] = "plamo-3-translate-2b",
        ["messages"] = new JsonArray(
            new JsonObject { ["role"] = "system", ["content"] = "Translate the supplied source." },
            new JsonObject { ["role"] = "user", ["content"] = $"Source language: {source}\nTarget language: {target}\n<source_text>\n{text}\n</source_text>" }),
        ["max_tokens"] = 128,
        ["stream"] = false
    };
    private static StringContent Content(JsonObject body) => new(body.ToJsonString(), System.Text.Encoding.UTF8, "application/json");
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
