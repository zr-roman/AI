using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Crm.GraphQL.Tests.Infrastructure;

/// <summary>Ответ GraphQL-сервера: HTTP-статус и тело { data, errors }.</summary>
public sealed record GraphQLResponse(HttpStatusCode StatusCode, JsonObject Body)
{
    public JsonNode? Data => Body["data"];

    public JsonArray Errors => Body["errors"] as JsonArray ?? new JsonArray();

    /// <summary>Запрос выполнен без ошибок верхнего уровня (ошибки бизнес-логики мутаций лежат в data и сюда не попадают).</summary>
    public GraphQLResponse EnsureNoErrors()
    {
        Assert.True(StatusCode == HttpStatusCode.OK && Errors.Count == 0, $"HTTP {(int)StatusCode}: {Body.ToJsonString()}");
        return this;
    }
}

public static class GraphQLHttpClientExtensions
{
    public static async Task<GraphQLResponse> PostGraphQLAsync(this HttpClient client, string query, object? variables = null)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var response = await client.PostAsJsonAsync("/graphql", new { query, variables }, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);

        var body = string.IsNullOrWhiteSpace(text) ? new JsonObject() : JsonNode.Parse(text)!.AsObject();
        return new GraphQLResponse(response.StatusCode, body);
    }
}
