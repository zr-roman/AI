namespace Crm.Core.Domain;

/// <summary>Стадии воронки. Имена — часть контракта API и MCP, не переименовывай без миграции данных.</summary>
public enum DealStage
{
    Lead,
    Qualified,
    Proposal,
    Negotiation,
    Won,
    Lost,
}

public static class DealStageExtensions
{
    public static bool IsClosed(this DealStage stage) => stage is DealStage.Won or DealStage.Lost;

    /// <summary>Вероятность закрытия для взвешенного прогноза.</summary>
    public static decimal Probability(this DealStage stage) => stage switch
    {
        DealStage.Lead => 0.10m,
        DealStage.Qualified => 0.25m,
        DealStage.Proposal => 0.50m,
        DealStage.Negotiation => 0.75m,
        DealStage.Won => 1m,
        _ => 0m,
    };

    public static string DisplayName(this DealStage stage) => stage switch
    {
        DealStage.Lead => "Лид",
        DealStage.Qualified => "Квалифицирован",
        DealStage.Proposal => "КП отправлено",
        DealStage.Negotiation => "Переговоры",
        DealStage.Won => "Выиграна",
        DealStage.Lost => "Проиграна",
        _ => stage.ToString(),
    };

    public static string Description(this DealStage stage) => stage switch
    {
        DealStage.Lead => "Первичный интерес, потребность ещё не подтверждена.",
        DealStage.Qualified => "Есть бюджет, потребность и лицо, принимающее решение.",
        DealStage.Proposal => "Клиенту отправлено коммерческое предложение.",
        DealStage.Negotiation => "Согласование цены, условий и договора.",
        DealStage.Won => "Договор подписан. Сделка закрыта успешно.",
        DealStage.Lost => "Сделка проиграна. Обязательно указывается причина.",
        _ => "",
    };
}
