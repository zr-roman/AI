using Crm.Core.Domain;
using Crm.Core.Services;

namespace Crm.Core.Data;

/// <summary>
/// Демо-данные. Даты считаются от текущего момента, поэтому в любой день есть задачи на сегодня,
/// просроченные задачи и «зависшие» сделки — есть что показать агенту.
/// </summary>
internal static class CrmSeeder
{
    public static async Task SeedAsync(CrmDbContext db, CrmClock clock, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var today = clock.Today;
        DateTimeOffset DaysAgo(double days) => now.AddDays(-days);

        // API-ключи только для локальной разработки
        var anna = new User { FullName = "Анна Смирнова", Email = "anna@crm.local", Role = UserRole.Admin, ApiKeyHash = ApiKeys.Hash("anna-dev-key") };
        var boris = new User { FullName = "Борис Козлов", Email = "boris@crm.local", Role = UserRole.Manager, ApiKeyHash = ApiKeys.Hash("boris-dev-key") };

        var vector = new Company { Name = "ООО «Вектор Логистик»", Industry = "Логистика", Website = "https://vector-logistic.example", CreatedAt = DaysAgo(120) };
        var nordwind = new Company { Name = "АО «Северный ветер»", Industry = "Энергетика", Website = "https://nordwind.example", CreatedAt = DaysAgo(95) };
        var taste = new Company { Name = "ООО «Фабрика вкуса»", Industry = "Пищевое производство", Website = "https://tastefactory.example", CreatedAt = DaysAgo(80) };
        var kuznetsovShop = new Company { Name = "ИП Кузнецов О. В.", Industry = "Розничная торговля", CreatedAt = DaysAgo(70) };

        var ivan = new Contact { FirstName = "Иван", LastName = "Петров", Position = "Генеральный директор", Email = "ivan.petrov@vector-logistic.example", Phone = "+7 900 111-22-33", Company = vector, Owner = anna, CreatedAt = DaysAgo(110) };
        var natalia = new Contact { FirstName = "Наталья", LastName = "Белова", Position = "Финансовый директор", Email = "n.belova@vector-logistic.example", Phone = "+7 900 111-22-44", Company = vector, Owner = anna, CreatedAt = DaysAgo(100) };
        var maria = new Contact { FirstName = "Мария", LastName = "Соколова", Position = "Руководитель закупок", Email = "m.sokolova@nordwind.example", Phone = "+7 912 555-01-02", Company = nordwind, Owner = boris, CreatedAt = DaysAgo(90) };
        var dmitry = new Contact { FirstName = "Дмитрий", LastName = "Волков", Position = "ИТ-директор", Email = "d.volkov@nordwind.example", Company = nordwind, Owner = boris, CreatedAt = DaysAgo(85) };
        var elena = new Contact { FirstName = "Елена", LastName = "Орлова", Position = "Коммерческий директор", Email = "e.orlova@tastefactory.example", Phone = "+7 916 700-80-90", Company = taste, Owner = anna, CreatedAt = DaysAgo(75) };
        var oleg = new Contact { FirstName = "Олег", LastName = "Кузнецов", Position = "Владелец", Email = "oleg.kuznetsov@mail.example", Phone = "+7 925 333-44-55", Company = kuznetsovShop, Owner = boris, CreatedAt = DaysAgo(70) };

        var racks = new Deal { Title = "Складские стеллажи для терминала в Подольске", Amount = 1_250_000m, Stage = DealStage.Proposal, Contact = ivan, Company = vector, Owner = anna, ExpectedCloseDate = today.AddDays(14), CreatedAt = DaysAgo(30), StageChangedAt = DaysAgo(5) };
        var service = new Deal { Title = "Годовое сервисное обслуживание", Amount = 480_000m, Stage = DealStage.Negotiation, Contact = natalia, Company = vector, Owner = anna, ExpectedCloseDate = today.AddDays(7), CreatedAt = DaysAgo(25), StageChangedAt = DaysAgo(3) };
        var serverRoom = new Deal { Title = "Модернизация серверной", Amount = 2_900_000m, Stage = DealStage.Qualified, Contact = dmitry, Company = nordwind, Owner = boris, ExpectedCloseDate = today.AddDays(45), CreatedAt = DaysAgo(20), StageChangedAt = DaysAgo(10) };
        var supplies = new Deal { Title = "Поставка расходных материалов", Amount = 150_000m, Stage = DealStage.Lead, Contact = maria, Company = nordwind, Owner = boris, CreatedAt = DaysAgo(45), StageChangedAt = DaysAgo(41) };
        var packingLine = new Deal { Title = "Линия упаковки", Amount = 3_600_000m, Stage = DealStage.Proposal, Contact = elena, Company = taste, Owner = anna, ExpectedCloseDate = today.AddDays(30), CreatedAt = DaysAgo(40), StageChangedAt = DaysAgo(12) };
        var cashDesk = new Deal { Title = "Касса и учёт для магазина", Amount = 90_000m, Stage = DealStage.Lead, Contact = oleg, Company = kuznetsovShop, Owner = boris, CreatedAt = DaysAgo(65), StageChangedAt = DaysAgo(60) };
        var pilot = new Deal { Title = "Пилотная поставка стеллажей", Amount = 300_000m, Stage = DealStage.Won, Contact = ivan, Company = vector, Owner = anna, CreatedAt = DaysAgo(60), StageChangedAt = DaysAgo(20), ClosedAt = DaysAgo(20) };
        var testBatch = new Deal { Title = "Тестовая партия упаковочной плёнки", Amount = 200_000m, Stage = DealStage.Lost, Contact = elena, Company = taste, Owner = anna, LostReason = "Выбрали поставщика дешевле", CreatedAt = DaysAgo(50), StageChangedAt = DaysAgo(15), ClosedAt = DaysAgo(15) };

        var activities = new[]
        {
            new Activity { Type = ActivityType.Meeting, Subject = "Презентация решения на складе в Подольске", Details = "Показали стеллажи, клиент просит рассрочку на 3 месяца.", OccurredAt = DaysAgo(6), Contact = ivan, Deal = racks, Author = anna },
            new Activity { Type = ActivityType.StageChange, Subject = "Стадия: Qualified → Proposal", OccurredAt = DaysAgo(5), Contact = ivan, Deal = racks, Author = anna },
            new Activity { Type = ActivityType.Email, Subject = "Отправлено КП на 1,25 млн ₽", OccurredAt = DaysAgo(5), Contact = ivan, Deal = racks, Author = anna },
            new Activity { Type = ActivityType.Call, Subject = "Обсудили условия оплаты", Details = "Просят отсрочку 30 дней, юристы смотрят договор.", OccurredAt = DaysAgo(3), Contact = natalia, Deal = service, Author = anna },
            new Activity { Type = ActivityType.Call, Subject = "Уточнили требования к серверной", Details = "Нужен предварительный аудит, бюджет согласован.", OccurredAt = DaysAgo(10), Contact = dmitry, Deal = serverRoom, Author = boris },
            new Activity { Type = ActivityType.Email, Subject = "Отправили прайс на расходные материалы", OccurredAt = DaysAgo(41), Contact = maria, Deal = supplies, Author = boris },
            new Activity { Type = ActivityType.Meeting, Subject = "Встреча на производстве", Details = "Интерес к линии упаковки, ждут КП с монтажом.", OccurredAt = DaysAgo(12), Contact = elena, Deal = packingLine, Author = anna },
            new Activity { Type = ActivityType.Call, Subject = "Первичный звонок", Details = "Попросил перезвонить через месяц.", OccurredAt = DaysAgo(60), Contact = oleg, Deal = cashDesk, Author = boris },
            new Activity { Type = ActivityType.StageChange, Subject = "Стадия: Negotiation → Won", OccurredAt = DaysAgo(20), Contact = ivan, Deal = pilot, Author = anna },
            new Activity { Type = ActivityType.StageChange, Subject = "Стадия: Proposal → Lost", Details = "Причина: Выбрали поставщика дешевле", OccurredAt = DaysAgo(15), Contact = elena, Deal = testBatch, Author = anna },
        };

        var tasks = new[]
        {
            new CrmTask { Title = "Позвонить по КП на стеллажи", DueDate = today, Contact = ivan, Deal = racks, Assignee = anna, CreatedAt = DaysAgo(5) },
            new CrmTask { Title = "Отправить проект договора на сервис", DueDate = today.AddDays(-1), Contact = natalia, Deal = service, Assignee = anna, CreatedAt = DaysAgo(3) },
            new CrmTask { Title = "Подготовить КП на линию упаковки с монтажом", DueDate = today.AddDays(3), Contact = elena, Deal = packingLine, Assignee = anna, CreatedAt = DaysAgo(12) },
            new CrmTask { Title = "Назначить аудит серверной", DueDate = today.AddDays(2), Contact = dmitry, Deal = serverRoom, Assignee = boris, CreatedAt = DaysAgo(10) },
            new CrmTask { Title = "Перезвонить Олегу Кузнецову", DueDate = today.AddDays(-10), Contact = oleg, Deal = cashDesk, Assignee = boris, CreatedAt = DaysAgo(40) },
            new CrmTask { Title = "Согласовать пилотную поставку", DueDate = today.AddDays(-21), Contact = ivan, Deal = pilot, Assignee = anna, IsCompleted = true, CompletedAt = DaysAgo(20), CreatedAt = DaysAgo(30) },
        };

        db.Users.AddRange(anna, boris);
        db.Companies.AddRange(vector, nordwind, taste, kuznetsovShop);
        db.Contacts.AddRange(ivan, natalia, maria, dmitry, elena, oleg);
        db.Deals.AddRange(racks, service, serverRoom, supplies, packingLine, cashDesk, pilot, testBatch);
        db.Activities.AddRange(activities);
        db.Tasks.AddRange(tasks);
        await db.SaveChangesAsync(ct);
    }
}
