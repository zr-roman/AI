using System.ComponentModel;
using Crm.Core.Domain;
using Crm.Core.Services;
using Crm.Mcp.Formatting;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;

namespace Crm.Mcp.Tools;

[McpServerToolType]
public sealed class DealTools(DealService deals, CrmFormatter format)
{
    [McpServerTool(Name = "list_deals", Title = "Список сделок", ReadOnly = true, OpenWorld = false)]
    [Description("Список сделок, отсортированный по сумме. Без stage возвращает только открытые сделки (не Won и не Lost); чтобы увидеть выигранные или проигранные, передай stage. Сделки конкретного менеджера — через owner, свои — через onlyMine.")]
    public async Task<string> ListDeals(
        [Description("Фильтр по стадии. Не указывай, чтобы получить все открытые сделки")] DealStage? stage = null,
        [Description("true — только сделки текущего пользователя")] bool onlyMine = false,
        [Description("Ответственный: имя, фамилия или email, например «Анна» или «Смирнова». Не передавай вместе с onlyMine")] string? owner = null,
        [Description("Поиск по названию сделки или компании")] string? query = null,
        [Description("Сколько сделок вернуть, от 1 до 100")] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var filter = new DealFilter(stage, onlyMine, query, owner);
        var found = await deals.ListAsync(filter, limit, cancellationToken);
        return format.DealList(found, filter);
    }

    [McpServerTool(Name = "get_deal_card", Title = "Карточка сделки", ReadOnly = true, OpenWorld = false)]
    [Description("Карточка сделки: сумма, стадия, контакт, история активностей и открытые задачи.")]
    public async Task<string> GetDealCard(
        [Description("ID сделки")] int dealId,
        CancellationToken cancellationToken = default)
    {
        var card = await deals.GetCardAsync(dealId, cancellationToken);
        return format.DealCard(card);
    }

    [McpServerTool(Name = "create_deal", Title = "Создать сделку", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Создаёт сделку от имени текущего пользователя. Если указан контакт, компания подставится из его карточки.")]
    public async Task<string> CreateDeal(
        [Description("Название сделки, например «Поставка оборудования для склада»")] string title,
        [Description("Сумма в рублях")] decimal amount,
        [Description("ID контакта")] int? contactId = null,
        [Description("ID компании, если контакт не указан или работает в другой компании")] int? companyId = null,
        [Description("Ожидаемая дата закрытия, YYYY-MM-DD")] DateOnly? expectedCloseDate = null,
        [Description("Начальная стадия")] OpenDealStage stage = OpenDealStage.Lead,
        CancellationToken cancellationToken = default)
    {
        var deal = await deals.CreateAsync(
            new NewDeal(title, amount, contactId, companyId, expectedCloseDate, stage.ToDealStage()),
            cancellationToken);

        return format.DealCreated(deal);
    }

    [McpServerTool(Name = "update_deal", Title = "Изменить сделку", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Меняет название, сумму и/или ожидаемую дату закрытия открытой сделки. Передавай только те поля, которые нужно изменить. Стадию меняет move_deal_stage. Что изменилось, записывается в историю сделки. Менять сделку может её ответственный или администратор.")]
    public async Task<string> UpdateDeal(
        [Description("ID сделки")] int dealId,
        [Description("Новое название сделки")] string? title = null,
        [Description("Новая сумма в рублях")] decimal? amount = null,
        [Description("Новая ожидаемая дата закрытия, YYYY-MM-DD")] DateOnly? expectedCloseDate = null,
        [Description("true — убрать ожидаемую дату закрытия. Не передавай вместе с expectedCloseDate")] bool clearExpectedCloseDate = false,
        CancellationToken cancellationToken = default)
    {
        var result = await deals.UpdateAsync(
            dealId,
            new DealUpdate(title, amount, expectedCloseDate, clearExpectedCloseDate),
            cancellationToken);

        return format.DealUpdated(result);
    }

    [McpServerTool(Name = "move_deal_stage", Title = "Сменить стадию сделки", Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Переводит сделку на другую стадию воронки и записывает смену в историю. Для Lost обязательна причина. Менять сделку может её ответственный или администратор.")]
    public async Task<string> MoveDealStage(
        [Description("ID сделки")] int dealId,
        [Description("Новая стадия")] DealStage stage,
        [Description("Причина проигрыша — обязательна для Lost")] string? lostReason = null,
        CancellationToken cancellationToken = default)
    {
        var result = await deals.MoveStageAsync(dealId, stage, lostReason, cancellationToken);
        return format.StageChanged(result);
    }

    [McpServerTool(Name = "get_pipeline_summary", Title = "Сводка по воронке", ReadOnly = true, OpenWorld = false)]
    [Description("Сводка по воронке: количество и сумма открытых сделок по стадиям, взвешенный прогноз, выигранное за месяц и число зависших сделок.")]
    public async Task<string> GetPipelineSummary(
        [Description("true — только сделки текущего пользователя")] bool onlyMine = false,
        CancellationToken cancellationToken = default)
    {
        var summary = await deals.GetPipelineSummaryAsync(onlyMine, cancellationToken);
        return format.Pipeline(summary, onlyMine);
    }

    [McpServerTool(Name = "list_stale_deals", Title = "Зависшие сделки", ReadOnly = true, OpenWorld = false)]
    [Description("Открытые сделки, по которым давно нет движения: стадия не менялась и в истории не было записей дольше inactiveDays дней. Сначала самые давно забытые, для каждой — сколько дней без движения. Без inactiveDays берётся тот же порог, что в get_pipeline_summary. Только чтение, доступно всем.")]
    public async Task<string> ListStaleDeals(
        [Description("Сколько дней без движения считать зависанием, от 7 до 365. Не указывай, чтобы взять порог из сводки по воронке")] int? inactiveDays = null,
        [Description("true — только сделки текущего пользователя")] bool onlyMine = false,
        [Description("Ответственный: имя, фамилия или email. Не передавай вместе с onlyMine")] string? owner = null,
        CancellationToken cancellationToken = default)
    {
        var list = await deals.ListStaleAsync(inactiveDays, onlyMine, owner, cancellationToken);
        return format.StaleDealList(list);
    }

    // Пример инструмента, который вообще не виден обычным менеджерам в HTTP-режиме:
    // [Authorize] проверяется фильтрами MCP SDK (AddAuthorizationFilters) и убирает инструмент из tools/list.
    // Права дополнительно проверяет сервис — так они соблюдаются и в stdio, где HTTP-авторизации нет.
    [Authorize(Roles = nameof(UserRole.Admin))]
    [McpServerTool(Name = "close_stale_deals", Title = "Закрыть зависшие сделки", Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Массово переводит в Lost открытые сделки, по которым не было движения дольше inactiveDays дней. Работает в два шага: вызов без confirmationToken только показывает список и выдаёт токен; изменения применяются при повторном вызове с этим токеном. Перед вторым вызовом обязательно покажи список пользователю и получи явное согласие. Чтобы просто посмотреть зависшие сделки, используй list_stale_deals.")]
    public async Task<string> CloseStaleDeals(
        [Description("Сколько дней без движения считать зависанием, от 7 до 365")] int inactiveDays = 30,
        [Description("Токен из превью. При первом вызове не передавай")] string? confirmationToken = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(confirmationToken))
        {
            var preview = await deals.PreviewStaleDealsAsync(inactiveDays, cancellationToken);
            return format.StaleDealsPreview(preview);
        }

        var closed = await deals.CloseStaleDealsAsync(inactiveDays, confirmationToken, cancellationToken);
        return format.StaleDealsClosed(closed);
    }
}

/// <summary>Стадии, в которых можно создать сделку. Отдельный enum — чтобы схема инструмента не предлагала Won/Lost.</summary>
public enum OpenDealStage
{
    Lead,
    Qualified,
    Proposal,
    Negotiation,
}

internal static class OpenDealStageExtensions
{
    public static DealStage ToDealStage(this OpenDealStage stage) => stage switch
    {
        OpenDealStage.Lead => DealStage.Lead,
        OpenDealStage.Qualified => DealStage.Qualified,
        OpenDealStage.Proposal => DealStage.Proposal,
        OpenDealStage.Negotiation => DealStage.Negotiation,
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Неизвестная стадия."),
    };
}
