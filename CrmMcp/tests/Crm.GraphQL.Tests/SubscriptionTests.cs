using System.Buffers;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using Crm.GraphQL.Tests.Infrastructure;

namespace Crm.GraphQL.Tests;

/// <summary>
/// Подписка по WebSocket (протокол graphql-transport-ws, /graphql/ws): мутация публикует событие,
/// сервер рассылает его подписчикам.
/// </summary>
public sealed class SubscriptionTests(CrmApiFactory api) : IClassFixture<CrmApiFactory>
{
    [Fact]
    public async Task MoveDealStage_event_is_delivered_to_websocket_subscriber()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = api.CreateClient(CrmApiFactory.AnnaKey);

        // Своя сделка, чтобы тест не зависел от остальных
        var created = await client.PostGraphQLAsync(
            """mutation { createDeal(input: { title: "Сделка для подписки", amount: 1000 }) { deal { id } } }""");
        var dealId = created.EnsureNoErrors().Data!["createDeal"]!["deal"]!["id"]!.GetValue<int>();

        var socketClient = api.Server.CreateWebSocketClient();
        socketClient.SubProtocols.Add("graphql-transport-ws");
        socketClient.ConfigureRequest = request => request.Headers.Authorization = $"Bearer {CrmApiFactory.AnnaKey}";
        using var socket = await socketClient.ConnectAsync(new Uri(api.Server.BaseAddress, "graphql/ws"), cancellationToken);

        await SendAsync(socket, new { type = "connection_init" }, cancellationToken);
        Assert.Equal("connection_ack", (await ReceiveAsync(socket, cancellationToken))["type"]!.GetValue<string>());

        await SendAsync(
            socket,
            new
            {
                id = "1",
                type = "subscribe",
                payload = new { query = "subscription { onDealStageChanged { previousStage stage changedBy { fullName } deal { id title } } }" },
            },
            cancellationToken);

        // Подтверждения подписки в протоколе нет: двигаем сделку туда-обратно, пока не придёт событие
        var pending = ReceiveAsync(socket, cancellationToken);
        string[] stages = ["QUALIFIED", "LEAD"];
        for (var attempt = 0; attempt < 20 && !pending.IsCompleted; attempt++)
        {
            await client.PostGraphQLAsync(
                $$"""mutation { moveDealStage(input: { dealId: {{dealId}}, stage: {{stages[attempt % 2]}} }) { deal { stage } } }""");
            await Task.WhenAny(pending, Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken));
        }

        Assert.True(pending.IsCompleted, "Событие о смене стадии не пришло подписчику.");
        var message = await pending;
        Assert.Equal("next", message["type"]!.GetValue<string>());

        var change = message["payload"]!["data"]!["onDealStageChanged"]!;
        Assert.Equal(dealId, change["deal"]!["id"]!.GetValue<int>());
        Assert.Equal("Сделка для подписки", change["deal"]!["title"]!.GetValue<string>());
        Assert.Equal("Анна Смирнова", change["changedBy"]!["fullName"]!.GetValue<string>());
        Assert.NotEqual(change["previousStage"]!.GetValue<string>(), change["stage"]!.GetValue<string>());
    }

    [Fact]
    public async Task WebSocket_without_api_key_is_rejected()
    {
        var socketClient = api.Server.CreateWebSocketClient();
        socketClient.SubProtocols.Add("graphql-transport-ws");

        await Assert.ThrowsAnyAsync<Exception>(() =>
            socketClient.ConnectAsync(new Uri(api.Server.BaseAddress, "graphql/ws"), TestContext.Current.CancellationToken));
    }

    private static Task SendAsync(WebSocket socket, object message, CancellationToken cancellationToken) =>
        socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(message), WebSocketMessageType.Text, endOfMessage: true, cancellationToken);

    private static async Task<JsonObject> ReceiveAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        while (true)
        {
            var buffer = new ArrayBufferWriter<byte>();
            ValueWebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer.GetMemory(4096), cancellationToken);
                buffer.Advance(result.Count);
            }
            while (!result.EndOfMessage);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new InvalidOperationException($"Сервер закрыл соединение: {socket.CloseStatus} {socket.CloseStatusDescription}");
            }

            var message = JsonNode.Parse(buffer.WrittenSpan)!.AsObject();
            switch (message["type"]?.GetValue<string>())
            {
                // Keep-alive протокола: отвечаем и ждём дальше
                case "ping":
                    await SendAsync(socket, new { type = "pong" }, cancellationToken);
                    continue;
                case "pong":
                    continue;
                default:
                    return message;
            }
        }
    }
}
