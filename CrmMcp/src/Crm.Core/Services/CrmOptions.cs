namespace Crm.Core.Services;

/// <summary>Настройки из секции "Crm" в appsettings.json.</summary>
public sealed class CrmOptions
{
    public const string SectionName = "Crm";

    /// <summary>Часовой пояс отдела продаж: от него зависят «сегодня», просрочка задач и формат дат.</summary>
    public string TimeZone { get; set; } = "Europe/Moscow";

    /// <summary>Заполнить пустую БД демо-данными при старте.</summary>
    public bool SeedDemoData { get; set; } = true;

    /// <summary>Сколько дней без движения считать сделку зависшей (для сводки по воронке).</summary>
    public int StaleDealDays { get; set; } = 30;
}
