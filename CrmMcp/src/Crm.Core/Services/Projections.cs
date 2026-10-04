using System.Linq.Expressions;
using Crm.Core.Domain;

namespace Crm.Core.Services;

/// <summary>Проекции для EF Core: в SQL уходят только нужные колонки, без загрузки графа сущностей.</summary>
internal static class Projections
{
    public static readonly Expression<Func<Deal, bool>> IsOpenDeal =
        d => d.Stage != DealStage.Won && d.Stage != DealStage.Lost;

    public static readonly Expression<Func<Contact, ContactListItem>> ToContactListItem = c => new ContactListItem(
        c.Id,
        c.FirstName + " " + c.LastName,
        c.Position,
        c.CompanyId,
        c.Company != null ? c.Company.Name : null,
        c.Email,
        c.Phone,
        c.Owner.FullName,
        c.CreatedAt);

    public static readonly Expression<Func<Deal, DealListItem>> ToDealListItem = d => new DealListItem(
        d.Id,
        d.Title,
        d.Amount,
        d.Stage,
        d.CompanyId,
        d.Company != null ? d.Company.Name : null,
        d.ContactId,
        d.Contact != null ? d.Contact.FirstName + " " + d.Contact.LastName : null,
        d.Owner.FullName,
        d.ExpectedCloseDate,
        d.StageChangedAt);

    public static readonly Expression<Func<Activity, ActivityItem>> ToActivityItem = a => new ActivityItem(
        a.Id,
        a.Type,
        a.Subject,
        a.Details,
        a.OccurredAt,
        a.Author.FullName,
        a.ContactId,
        a.Contact != null ? a.Contact.FirstName + " " + a.Contact.LastName : null,
        a.DealId,
        a.Deal != null ? a.Deal.Title : null);

    public static readonly Expression<Func<CrmTask, TaskItem>> ToTaskItem = t => new TaskItem(
        t.Id,
        t.Title,
        t.Details,
        t.DueDate,
        t.IsCompleted,
        t.Assignee.FullName,
        t.ContactId,
        t.Contact != null ? t.Contact.FirstName + " " + t.Contact.LastName : null,
        t.DealId,
        t.Deal != null ? t.Deal.Title : null);
}
