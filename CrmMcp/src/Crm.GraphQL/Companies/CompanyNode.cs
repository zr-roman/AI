using Crm.Core.Domain;
using Crm.GraphQL.DataLoaders;
using HotChocolate;
using HotChocolate.CostAnalysis.Types;
using HotChocolate.Types;

namespace Crm.GraphQL.Companies;

[ObjectType<Company>]
public static partial class CompanyNode
{
    static partial void Configure(IObjectTypeDescriptor<Company> descriptor)
    {
        descriptor.Description("Компания клиента.");

        // Навигационные свойства EF не публикуем: связи отдают резолверы ниже через DataLoader
        descriptor.BindFieldsExplicitly();
        descriptor.Field(c => c.Id);
        descriptor.Field(c => c.Name);
        descriptor.Field(c => c.Industry);
        descriptor.Field(c => c.Website);
        descriptor.Field(c => c.CreatedAt);
    }

    [GraphQLDescription("Контакты компании.")]
    [ListSize(AssumedSize = 20, RequireOneSlicingArgument = false)]
    public static async Task<Contact[]> GetContactsAsync(
        [Parent] Company company,
        IContactsByCompanyIdDataLoader contactsByCompanyId,
        CancellationToken cancellationToken)
        => await contactsByCompanyId.LoadAsync(company.Id, cancellationToken) ?? [];

    [GraphQLDescription("Сделки компании, сначала крупные. По умолчанию только открытые.")]
    [ListSize(AssumedSize = 20, RequireOneSlicingArgument = false)]
    public static async Task<Deal[]> GetDealsAsync(
        [Parent] Company company,
        IDealsByCompanyIdDataLoader dealsByCompanyId,
        CancellationToken cancellationToken,
        [GraphQLDescription("true — вместе с выигранными и проигранными")] bool includeClosed = false)
    {
        var deals = await dealsByCompanyId.LoadAsync(company.Id, cancellationToken) ?? [];
        return includeClosed ? deals : deals.Where(d => !d.Stage.IsClosed()).ToArray();
    }
}
