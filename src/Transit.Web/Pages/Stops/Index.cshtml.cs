using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Transit.Data.Reports;

namespace Transit.Web.Pages.Stops;

public class IndexModel(ReliabilityReports reports) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Query { get; set; }

    public IReadOnlyList<StopMatch> Matches { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(Query))
        {
            Matches = await reports.SearchStopsAsync(Query, cancellationToken);
        }
    }
}
