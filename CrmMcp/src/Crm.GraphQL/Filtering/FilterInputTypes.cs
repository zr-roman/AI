using Crm.Core.Domain;
using HotChocolate.Data.Filters;

namespace Crm.GraphQL.Filtering;

// По умолчанию Hot Chocolate строит фильтр из всех свойств CLR-типа, включая навигационные.
// Для User это означало бы where: { owner: { apiKeyHash: { startsWith: "a" } } } — перебор хэша
// API-ключа по одному символу. Поэтому поля фильтров перечислены явно (белый список), а типы
// привязаны к сущностям глобально в AddCrmGraphQL: вложенный фильтр по owner тоже возьмёт этот список.

public sealed class UserFilterInputType : FilterInputType<User>
{
    protected override void Configure(IFilterInputTypeDescriptor<User> descriptor)
    {
        descriptor.Name("UserFilterInput");
        descriptor.BindFieldsExplicitly();
        descriptor.Field(u => u.Id);
        descriptor.Field(u => u.FullName);
        descriptor.Field(u => u.Email);
        descriptor.Field(u => u.Role);
    }
}

public sealed class CompanyFilterInputType : FilterInputType<Company>
{
    protected override void Configure(IFilterInputTypeDescriptor<Company> descriptor)
    {
        descriptor.Name("CompanyFilterInput");
        descriptor.BindFieldsExplicitly();
        descriptor.Field(c => c.Id);
        descriptor.Field(c => c.Name);
        descriptor.Field(c => c.Industry);
        descriptor.Field(c => c.Website);
        descriptor.Field(c => c.CreatedAt);
    }
}

public sealed class ContactFilterInputType : FilterInputType<Contact>
{
    protected override void Configure(IFilterInputTypeDescriptor<Contact> descriptor)
    {
        descriptor.Name("ContactFilterInput");
        descriptor.BindFieldsExplicitly();
        descriptor.Field(c => c.Id);
        descriptor.Field(c => c.FirstName);
        descriptor.Field(c => c.LastName);
        descriptor.Field(c => c.Email);
        descriptor.Field(c => c.Phone);
        descriptor.Field(c => c.Position);
        descriptor.Field(c => c.CreatedAt);
        descriptor.Field(c => c.CompanyId);
        descriptor.Field(c => c.OwnerId);
        descriptor.Field(c => c.Company).Type<CompanyFilterInputType>();
        descriptor.Field(c => c.Owner).Type<UserFilterInputType>();
    }
}

public sealed class DealFilterInputType : FilterInputType<Deal>
{
    protected override void Configure(IFilterInputTypeDescriptor<Deal> descriptor)
    {
        descriptor.Name("DealFilterInput");
        descriptor.BindFieldsExplicitly();
        descriptor.Field(d => d.Id);
        descriptor.Field(d => d.Title);
        descriptor.Field(d => d.Amount);
        descriptor.Field(d => d.Stage);
        descriptor.Field(d => d.ExpectedCloseDate);
        descriptor.Field(d => d.CreatedAt);
        descriptor.Field(d => d.StageChangedAt);
        descriptor.Field(d => d.ClosedAt);
        descriptor.Field(d => d.ContactId);
        descriptor.Field(d => d.CompanyId);
        descriptor.Field(d => d.OwnerId);
        descriptor.Field(d => d.Contact).Type<ContactFilterInputType>();
        descriptor.Field(d => d.Company).Type<CompanyFilterInputType>();
        descriptor.Field(d => d.Owner).Type<UserFilterInputType>();
    }
}

public sealed class AuditEntryFilterInputType : FilterInputType<AuditEntry>
{
    protected override void Configure(IFilterInputTypeDescriptor<AuditEntry> descriptor)
    {
        // Arguments (jsonb) в фильтр не входит: строковые операции над jsonb PostgreSQL не выполнит
        descriptor.Name("AuditEntryFilterInput");
        descriptor.BindFieldsExplicitly();
        descriptor.Field(e => e.Id);
        descriptor.Field(e => e.OccurredAt);
        descriptor.Field(e => e.UserId);
        descriptor.Field(e => e.Channel);
        descriptor.Field(e => e.Operation);
        descriptor.Field(e => e.Succeeded);
        descriptor.Field(e => e.DurationMs);
    }
}
