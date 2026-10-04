namespace Crm.Core.Domain;

/// <summary>Задача менеджера (follow-up). Названа CrmTask, чтобы не конфликтовать с System.Threading.Tasks.Task.</summary>
public sealed class CrmTask
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public string? Details { get; set; }
    public DateOnly DueDate { get; set; }
    public bool IsCompleted { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public int AssigneeId { get; set; }
    public User Assignee { get; set; } = null!;

    public int? ContactId { get; set; }
    public Contact? Contact { get; set; }
    public int? DealId { get; set; }
    public Deal? Deal { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
