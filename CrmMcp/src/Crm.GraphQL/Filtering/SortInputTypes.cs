using Crm.Core.Domain;
using HotChocolate.Data.Sorting;

namespace Crm.GraphQL.Filtering;

// Сортировка — тоже по белому списку полей, как и фильтры (см. FilterInputTypes.cs).
// Стадию в сортировку не включаем: в БД она хранится строкой, и ORDER BY дал бы алфавитный порядок,
// а не порядок воронки.

public sealed class UserSortInputType : SortInputType<User>
{
    protected override void Configure(ISortInputTypeDescriptor<User> descriptor)
    {
        descriptor.Name("UserSortInput");
        descriptor.BindFieldsExplicitly();
        descriptor.Field(u => u.Id);
        descriptor.Field(u => u.FullName);
        descriptor.Field(u => u.Email);
    }
}

public sealed class CompanySortInputType : SortInputType<Company>
{
    protected override void Configure(ISortInputTypeDescriptor<Company> descriptor)
    {
        descriptor.Name("CompanySortInput");
        descriptor.BindFieldsExplicitly();
        descriptor.Field(c => c.Id);
        descriptor.Field(c => c.Name);
        descriptor.Field(c => c.Industry);
        descriptor.Field(c => c.CreatedAt);
    }
}

public sealed class ContactSortInputType : SortInputType<Contact>
{
    protected override void Configure(ISortInputTypeDescriptor<Contact> descriptor)
    {
        descriptor.Name("ContactSortInput");
        descriptor.BindFieldsExplicitly();
        descriptor.Field(c => c.Id);
        descriptor.Field(c => c.FirstName);
        descriptor.Field(c => c.LastName);
        descriptor.Field(c => c.Email);
        descriptor.Field(c => c.Position);
        descriptor.Field(c => c.CreatedAt);
        descriptor.Field(c => c.Company).Type<CompanySortInputType>();
        descriptor.Field(c => c.Owner).Type<UserSortInputType>();
    }
}

public sealed class DealSortInputType : SortInputType<Deal>
{
    protected override void Configure(ISortInputTypeDescriptor<Deal> descriptor)
    {
        descriptor.Name("DealSortInput");
        descriptor.BindFieldsExplicitly();
        descriptor.Field(d => d.Id);
        descriptor.Field(d => d.Title);
        descriptor.Field(d => d.Amount);
        descriptor.Field(d => d.ExpectedCloseDate);
        descriptor.Field(d => d.CreatedAt);
        descriptor.Field(d => d.StageChangedAt);
        descriptor.Field(d => d.ClosedAt);
        descriptor.Field(d => d.Company).Type<CompanySortInputType>();
        descriptor.Field(d => d.Owner).Type<UserSortInputType>();
    }
}

public sealed class AuditEntrySortInputType : SortInputType<AuditEntry>
{
    protected override void Configure(ISortInputTypeDescriptor<AuditEntry> descriptor)
    {
        descriptor.Name("AuditEntrySortInput");
        descriptor.BindFieldsExplicitly();
        descriptor.Field(e => e.Id);
        descriptor.Field(e => e.OccurredAt);
        descriptor.Field(e => e.Operation);
        descriptor.Field(e => e.DurationMs);
    }
}
