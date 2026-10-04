namespace Crm.Core.Domain;

public sealed class Contact
{
    public int Id { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Position { get; set; }

    public int? CompanyId { get; set; }
    public Company? Company { get; set; }

    /// <summary>Ответственный менеджер.</summary>
    public int OwnerId { get; set; }
    public User Owner { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }

    public List<Deal> Deals { get; set; } = [];
    public List<Activity> Activities { get; set; } = [];
    public List<CrmTask> Tasks { get; set; } = [];
}
