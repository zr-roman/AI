namespace Crm.Core.Domain;

/// <summary>Запись в истории взаимодействий: звонок, письмо, встреча, заметка.</summary>
public sealed class Activity
{
    public int Id { get; set; }
    public ActivityType Type { get; set; }
    public required string Subject { get; set; }
    public string? Details { get; set; }
    public DateTimeOffset OccurredAt { get; set; }

    public int? ContactId { get; set; }
    public Contact? Contact { get; set; }
    public int? DealId { get; set; }
    public Deal? Deal { get; set; }

    public int AuthorId { get; set; }
    public User Author { get; set; } = null!;
}

public enum ActivityType
{
    Call,
    Email,
    Meeting,
    Note,

    /// <summary>Создаётся системой при смене стадии сделки.</summary>
    StageChange,
}

public static class ActivityTypeExtensions
{
    public static string DisplayName(this ActivityType type) => type switch
    {
        ActivityType.Call => "Звонок",
        ActivityType.Email => "Письмо",
        ActivityType.Meeting => "Встреча",
        ActivityType.Note => "Заметка",
        ActivityType.StageChange => "Смена стадии",
        _ => type.ToString(),
    };
}
