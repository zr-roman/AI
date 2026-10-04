namespace Crm.Core.Domain;

/// <summary>Сотрудник отдела продаж — пользователь CRM.</summary>
public sealed class User
{
    public int Id { get; set; }
    public required string FullName { get; set; }
    public required string Email { get; set; }
    public UserRole Role { get; set; } = UserRole.Manager;

    /// <summary>SHA-256 от API-ключа. Сам ключ не храним.</summary>
    public string? ApiKeyHash { get; set; }
}

public enum UserRole
{
    Manager,
    Admin,
}
