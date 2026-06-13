using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace RfcRag.Tools;

[McpServerToolType]
[Description("RFC question-answering tools")]
public static class RfcAskTools
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [McpServerTool(Name = "ask_rfc", ReadOnly = true, OpenWorld = false)]
    [Description("Ask a question about RFCs and get a cited answer generated from the indexed corpus. Runs hybrid search, assembles evidence, and generates an answer using a language model.")]
    public static async Task<CallToolResult> AskRfc(
        IAskService askService,
        [Description("The question to ask about RFCs.")] string question,
        [Description("Optional: maximum number of search results to retrieve (default: 20).")] int? limit = null,
        [Description("Optional: RFC 2119 normative keyword to filter results (e.g., 'MUST', 'SHOULD', 'MAY', 'MUST NOT').")] string? normativeKeyword = null,
        [Description("When true, includes obsoleted RFCs without penalty or warning. Default false demotes and flags obsoleted RFCs.")] bool include_obsolete = false,
        CancellationToken cancellationToken = default)
    {
        GeneratedAnswer answer = await askService.AskAsync(question, limit, normativeKeyword, include_obsolete, cancellationToken)
            .ConfigureAwait(false);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = JsonSerializer.Serialize(answer, JsonOptions) }],
        };
    }
}
