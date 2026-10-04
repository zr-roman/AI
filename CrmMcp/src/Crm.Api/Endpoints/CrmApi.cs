using Crm.Core.Domain;
using Crm.Core.Services;

namespace Crm.Api.Endpoints;

/// <summary>
/// Обычный REST API поверх тех же сервисов, что и MCP-инструменты:
/// бизнес-логика одна, адаптеров два.
/// </summary>
public static class CrmApi
{
    public static RouteGroupBuilder MapCrmApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();

        api.MapGet("/contacts", (ContactService contacts, string? q, int? limit, CancellationToken ct) =>
            contacts.SearchAsync(q, limit ?? 20, ct));

        api.MapGet("/contacts/{id:int}", (ContactService contacts, int id, CancellationToken ct) =>
            contacts.GetCardAsync(id, ct));

        api.MapPost("/contacts", async (ContactService contacts, NewContact body, CancellationToken ct) =>
        {
            var created = await contacts.CreateAsync(body, ct);
            return TypedResults.Created($"/api/contacts/{created.Contact.Id}", created);
        });

        api.MapGet("/deals", (DealService deals, DealStage? stage, bool? mine, string? owner, string? q, int? limit, CancellationToken ct) =>
            deals.ListAsync(new DealFilter(stage, mine ?? false, q, owner), limit ?? 50, ct));

        api.MapGet("/deals/stale", (DealService deals, int? days, bool? mine, string? owner, CancellationToken ct) =>
            deals.ListStaleAsync(days, mine ?? false, owner, ct));

        api.MapGet("/deals/{id:int}", (DealService deals, int id, CancellationToken ct) =>
            deals.GetCardAsync(id, ct));

        api.MapPost("/deals", async (DealService deals, NewDeal body, CancellationToken ct) =>
        {
            var deal = await deals.CreateAsync(body, ct);
            return TypedResults.Created($"/api/deals/{deal.Id}", deal);
        });

        api.MapPatch("/deals/{id:int}", (DealService deals, int id, DealUpdate body, CancellationToken ct) =>
            deals.UpdateAsync(id, body, ct));

        api.MapPost("/deals/{id:int}/stage", (DealService deals, int id, MoveDealStage body, CancellationToken ct) =>
            deals.MoveStageAsync(id, body.Stage, body.LostReason, ct));

        api.MapGet("/pipeline", (DealService deals, bool? mine, CancellationToken ct) =>
            deals.GetPipelineSummaryAsync(mine ?? false, ct));

        api.MapPost("/activities", (ActivityService activities, NewActivity body, CancellationToken ct) =>
            activities.LogAsync(body, ct));

        api.MapGet("/tasks", (TaskService tasks, TaskFilter? filter, CancellationToken ct) =>
            tasks.ListMineAsync(filter ?? TaskFilter.Open, ct));

        api.MapPost("/tasks", (TaskService tasks, NewTask body, CancellationToken ct) =>
            tasks.CreateAsync(body, ct));

        api.MapPatch("/tasks/{id:int}", (TaskService tasks, int id, TaskUpdate body, CancellationToken ct) =>
            tasks.UpdateAsync(id, body, ct));

        api.MapPost("/tasks/{id:int}/complete", (TaskService tasks, int id, CompleteTask? body, CancellationToken ct) =>
            tasks.CompleteAsync(id, body?.ResultNote, ct));

        return api;
    }
}
