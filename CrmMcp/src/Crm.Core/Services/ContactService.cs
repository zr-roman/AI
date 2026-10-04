using Crm.Core.Data;
using Crm.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Crm.Core.Services;

public sealed class ContactService(CrmDbContext db, ICurrentUser currentUser, CrmClock clock)
{
    public async Task<IReadOnlyList<ContactListItem>> SearchAsync(string? query, int limit = 10, CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 50);
        var contacts = db.Contacts.AsNoTracking();

        if (Text.Normalize(query) is { } term)
        {
            var pattern = Text.ContainsPattern(term);
            contacts = contacts.Where(c =>
                EF.Functions.ILike(c.FirstName + " " + c.LastName, pattern) ||
                EF.Functions.ILike(c.LastName + " " + c.FirstName, pattern) ||
                (c.Email != null && EF.Functions.ILike(c.Email, pattern)) ||
                (c.Phone != null && EF.Functions.ILike(c.Phone, pattern)) ||
                (c.Company != null && EF.Functions.ILike(c.Company.Name, pattern)));
        }

        return await contacts
            .OrderBy(c => c.LastName).ThenBy(c => c.FirstName)
            .Take(limit)
            .Select(Projections.ToContactListItem)
            .ToListAsync(ct);
    }

    public async Task<ContactCard> GetCardAsync(int contactId, CancellationToken ct = default)
    {
        var contact = await db.Contacts.AsNoTracking()
            .Where(c => c.Id == contactId)
            .Select(Projections.ToContactListItem)
            .FirstOrDefaultAsync(ct)
            ?? throw new CrmNotFoundException($"Контакт #{contactId} не найден.");

        var openDeals = await db.Deals.AsNoTracking()
            .Where(d => d.ContactId == contactId)
            .Where(Projections.IsOpenDeal)
            .OrderByDescending(d => d.Amount)
            .Select(Projections.ToDealListItem)
            .ToListAsync(ct);

        var activities = await db.Activities.AsNoTracking()
            .Where(a => a.ContactId == contactId)
            .OrderByDescending(a => a.OccurredAt)
            .Take(10)
            .Select(Projections.ToActivityItem)
            .ToListAsync(ct);

        var tasks = await db.Tasks.AsNoTracking()
            .Where(t => t.ContactId == contactId && !t.IsCompleted)
            .OrderBy(t => t.DueDate)
            .Select(Projections.ToTaskItem)
            .ToListAsync(ct);

        return new ContactCard(contact, openDeals, activities, tasks);
    }

    public async Task<ContactCreated> CreateAsync(NewContact input, CancellationToken ct = default)
    {
        var firstName = Text.Required(input.FirstName, "Имя", 100);
        var lastName = Text.Required(input.LastName, "Фамилия", 100);
        var email = Text.Optional(input.Email, "Email", 320)?.ToLowerInvariant();

        if (email is not null)
        {
            if (!Text.LooksLikeEmail(email))
            {
                throw new CrmValidationException($"Некорректный email: {email}.");
            }

            var duplicate = await db.Contacts.AsNoTracking()
                .Where(c => c.Email != null && c.Email.ToLower() == email)
                .Select(c => new { c.Id, Name = c.FirstName + " " + c.LastName })
                .FirstOrDefaultAsync(ct);

            if (duplicate is not null)
            {
                throw new CrmConflictException($"Контакт с email {email} уже есть: #{duplicate.Id} {duplicate.Name}.");
            }
        }

        Company? company = null;
        var companyCreated = false;
        if (Text.Optional(input.CompanyName, "Компания", 200) is { } companyName)
        {
            company = await db.Companies.FirstOrDefaultAsync(c => c.Name.ToLower() == companyName.ToLower(), ct);
            if (company is null)
            {
                company = new Company { Name = companyName, CreatedAt = clock.UtcNow };
                db.Companies.Add(company);
                companyCreated = true;
            }
        }

        var contact = new Contact
        {
            FirstName = firstName,
            LastName = lastName,
            Email = email,
            Phone = Text.Optional(input.Phone, "Телефон", 50),
            Position = Text.Optional(input.Position, "Должность", 150),
            Company = company,
            OwnerId = currentUser.Id,
            CreatedAt = clock.UtcNow,
        };

        db.Contacts.Add(contact);
        await db.SaveChangesAsync(ct);

        var created = await db.Contacts.AsNoTracking()
            .Where(c => c.Id == contact.Id)
            .Select(Projections.ToContactListItem)
            .FirstAsync(ct);

        return new ContactCreated(created, companyCreated);
    }
}
