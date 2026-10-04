namespace Crm.Core.Domain;

public sealed class Deal
{
    public int Id { get; set; }
    public required string Title { get; set; }

    /// <summary>Сумма в рублях.</summary>
    public decimal Amount { get; set; }

    public DealStage Stage { get; set; } = DealStage.Lead;
    public DateOnly? ExpectedCloseDate { get; set; }
    public string? LostReason { get; set; }

    public int? ContactId { get; set; }
    public Contact? Contact { get; set; }
    public int? CompanyId { get; set; }
    public Company? Company { get; set; }

    public int OwnerId { get; set; }
    public User Owner { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset StageChangedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }

    public List<Activity> Activities { get; set; } = [];
    public List<CrmTask> Tasks { get; set; } = [];
}
