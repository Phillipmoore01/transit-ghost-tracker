using System.Net;

namespace Transit.Web.Tests;

public sealed class ReportPageTests(ReportSiteFixture site) : IClassFixture<ReportSiteFixture>
{
    [Fact]
    public async Task Route_list_shows_each_tracked_route_with_its_figures()
    {
        var page = await GetPageAsync("/");

        var red = page.IndexOf("Red Line", StringComparison.Ordinal);
        var bus = page.IndexOf("42 – Forest Hills - Dudley", StringComparison.Ordinal);
        Assert.True(red >= 0 && bus > red, "Rail routes are listed before bus routes.");
        Assert.Contains("33% <span class=\"text-body-secondary\">(1)</span>", page);
        Assert.Contains("+30 s", page);
        Assert.Contains("May 3 – Jun 1, 2026", page);
    }

    [Fact]
    public async Task Route_page_breaks_down_trips_that_did_not_run()
    {
        var page = await GetPageAsync("/routes/42");

        Assert.Contains("of tracked trips did not run (1 of 3)", page);
        Assert.Contains("50% (1 of 2)", page);
        Assert.Contains("0% (0 of 1)", page);
        Assert.Contains("1 unreported trips were scheduled while the realtime feed was unavailable", page);
        Assert.Contains("Mon Jun 1, 2026", page);
        Assert.Contains("8:30 AM", page);
        Assert.Contains("Dudley", page);
        Assert.Contains("<code>t2</code>", page);
    }

    [Fact]
    public async Task Stop_page_shows_arrivals_per_route_including_child_platforms()
    {
        var station = await GetPageAsync("/stops/place-x");
        var stop = await GetPageAsync("/stops/B");

        Assert.Contains("Central Station", station);
        Assert.Contains("Red Line", station);
        Assert.Contains("42 – Forest Hills - Dudley", stop);
        Assert.Contains("+3 min 35 s", stop);
    }

    [Fact]
    public async Task Stop_search_matches_names_and_hides_platforms_of_stations()
    {
        var alpha = await GetPageAsync("/stops?q=alpha");
        var central = await GetPageAsync("/stops?q=central");

        Assert.Contains("Alpha St", alpha);
        Assert.Contains("Station", central);
        Assert.DoesNotContain("Central Station - Red Line", central);
    }

    [Theory]
    [InlineData("/routes/unknown")]
    [InlineData("/stops/unknown")]
    public async Task Unknown_routes_and_stops_are_not_found(string path)
    {
        using var response = await site.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Health_reports_unavailable_without_feed_data()
    {
        using var response = await site.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private async Task<string> GetPageAsync(string path)
    {
        using var response = await site.CreateClient().GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }
}
