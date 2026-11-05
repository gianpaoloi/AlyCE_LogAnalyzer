using LogAnalyzer.Services;
using Xunit;

namespace LogAnalyzer.Tests;

public class GrafanaLogQlTests
{
    private static LokiQuery Query(
        IReadOnlyList<LokiFieldMatch>? fields = null, IReadOnlyList<LokiLineFilter>? lines = null) =>
        new("https://teamsystem.grafana.net", "uid", "tse", "tse-webapi", "prod", TimeSpan.FromDays(7),
            Fields: fields, LineFilters: lines);

    [Fact]
    public void Without_filters_the_query_is_just_the_selector()
    {
        var query = Query();

        Assert.Equal(GrafanaLokiClient.BuildSelector(query), GrafanaLokiClient.BuildQuery(query));
    }

    [Fact]
    public void Line_field_and_level_filters_follow_the_selector_in_that_order()
    {
        var query = Query(
            fields: new[] { new LokiFieldMatch("detected_level", "error"), new LokiFieldMatch("cid", "axoteam") },
            lines: new[] { new LokiLineFilter("TeamSystem.AlyCE.PolicyManager", CaseInsensitive: true) });

        Assert.Equal(
            "{team=\"tse\", service_name=\"tse-webapi\", environment=\"prod\"} " +
            "|~ \"(?i)TeamSystem\\\\.AlyCE\\\\.PolicyManager\" | json | detected_level=\"error\" | cid=\"axoteam\"",
            GrafanaLokiClient.BuildQuery(query));
    }

    [Fact]
    public void A_case_sensitive_line_filter_uses_contains()
    {
        var query = Query(lines: new[] { new LokiLineFilter("ERR") });

        Assert.Contains("|= \"ERR\"", GrafanaLokiClient.BuildQuery(query));
    }

    [Fact]
    public void A_field_name_that_is_not_a_label_is_refused()
    {
        var query = Query(fields: new[] { new LokiFieldMatch("bad name\" | x", "1") });

        Assert.Throws<ArgumentException>(() => GrafanaLokiClient.BuildQuery(query));
    }

    [Fact]
    public void Quotes_in_filter_values_are_escaped()
    {
        var query = Query(fields: new[] { new LokiFieldMatch("cid", "a\"b") });

        Assert.EndsWith("| json | cid=\"a\\\"b\"", GrafanaLokiClient.BuildQuery(query));
    }
}
