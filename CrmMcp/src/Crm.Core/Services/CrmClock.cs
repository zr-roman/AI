using Microsoft.Extensions.Options;

namespace Crm.Core.Services;

/// <summary>
/// Время CRM. В БД всё хранится в UTC (Npgsql пишет в timestamptz только DateTimeOffset с нулевым смещением),
/// а «сегодня» и даты для людей считаются в часовом поясе отдела продаж.
/// </summary>
public sealed class CrmClock(TimeProvider timeProvider, IOptions<CrmOptions> options)
{
    private readonly TimeZoneInfo _zone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZone);

    public DateTimeOffset UtcNow => timeProvider.GetUtcNow();

    public DateTimeOffset LocalNow => TimeZoneInfo.ConvertTime(UtcNow, _zone);

    public DateOnly Today => DateOnly.FromDateTime(LocalNow.DateTime);

    public DateTimeOffset ToLocal(DateTimeOffset value) => TimeZoneInfo.ConvertTime(value, _zone);

    /// <summary>Начало текущего месяца по местному времени, в UTC.</summary>
    public DateTimeOffset StartOfMonthUtc
    {
        get
        {
            var local = LocalNow;
            var monthStart = new DateTime(local.Year, local.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
            return new DateTimeOffset(monthStart, _zone.GetUtcOffset(monthStart)).ToUniversalTime();
        }
    }
}
