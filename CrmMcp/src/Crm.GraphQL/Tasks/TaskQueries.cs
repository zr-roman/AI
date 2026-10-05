using Crm.Core.Domain;
using Crm.Core.Services;
using Crm.GraphQL.DataLoaders;
using GreenDonut;
using HotChocolate;
using HotChocolate.CostAnalysis.Types;
using HotChocolate.Types;

namespace Crm.GraphQL.Tasks;

[QueryType]
public static partial class TaskQueries
{
    // «Мои задачи» считает сервис — та же логика просрочки и «сегодня», что у REST и MCP (list_my_tasks)
    [GraphQLDescription("Мои невыполненные задачи: все (OPEN), просроченные (OVERDUE), на сегодня (TODAY) или на неделю (WEEK).")]
    [ListSize(AssumedSize = 20, RequireOneSlicingArgument = false)]
    public static async Task<IReadOnlyList<CrmTask>> GetMyTasksAsync(
        TaskService tasks,
        ITaskByIdDataLoader taskById,
        CancellationToken cancellationToken,
        TaskFilter filter = TaskFilter.Open)
    {
        var mine = await tasks.ListMineAsync(filter, cancellationToken);
        return await taskById.LoadRequiredAsync(mine.Select(t => t.Id).ToArray(), cancellationToken);
    }

    [GraphQLDescription("Задача по ID.")]
    public static async Task<CrmTask?> GetTaskAsync(
        int id,
        ITaskByIdDataLoader taskById,
        CancellationToken cancellationToken)
        => await taskById.LoadAsync(id, cancellationToken);
}
