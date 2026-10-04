namespace Crm.Core.Domain;

public sealed class Company
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Industry { get; set; }
    public string? Website { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public List<Contact> Contacts { get; set; } = [];
    public List<Deal> Deals { get; set; } = [];
}
