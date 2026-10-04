using SqlAnalyst.Core.Guard;

namespace SqlAnalyst.Tests.Guard;

public sealed class PlanInspectorTests
{
    private const string Plan = """
        [{"Plan": {"Node Type": "Limit", "Total Cost": 120.5, "Plan Rows": 101,
          "Plans": [{"Node Type": "Hash Join", "Total Cost": 118.0, "Plan Rows": 600,
            "Plans": [
              {"Node Type": "Seq Scan", "Relation Name": "customers", "Schema": "shop", "Total Cost": 12, "Plan Rows": 600},
              {"Node Type": "Function Scan", "Function Name": "generate_series", "Schema": "pg_catalog", "Total Cost": 10, "Plan Rows": 1000}
            ]}]}}]
        """;

    [Fact]
    public void Collects_relations_functions_and_cost()
    {
        var plan = PlanInspector.Parse(Plan);

        Assert.Equal(120.5, plan.TotalCost);
        Assert.Equal(["shop.customers"], plan.Relations);
        Assert.Equal(["generate_series"], plan.Functions);
        Assert.Null(PlanInspector.Check(plan, maxCost: 1000));
    }

    [Fact]
    public void Rejects_expensive_plans()
    {
        var plan = PlanInspector.Parse(Plan);

        Assert.Contains("слишком тяжёлый", PlanInspector.Check(plan, maxCost: 100));
    }

    [Fact]
    public void Rejects_system_catalogs_even_when_reached_indirectly()
    {
        var plan = new PlanSummary(1, 1, ["pg_catalog.pg_authid"], []);

        Assert.Contains("pg_catalog.pg_authid", PlanInspector.Check(plan, maxCost: 1000));
    }

    [Fact]
    public void Rejects_dangerous_function_scans()
    {
        var plan = new PlanSummary(1, 1, [], ["pg_ls_dir"]);

        Assert.Contains("pg_ls_dir", PlanInspector.Check(plan, maxCost: 1000));
    }
}
