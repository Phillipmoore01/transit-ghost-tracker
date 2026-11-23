using Transit.Data.Reports;

namespace Transit.Web.Pages;

public class IndexModel(ReliabilityReports reports) : ReportPageModel(reports)
{
    public IReadOnlyList<RouteSummary> Routes { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadPeriodAsync(cancellationToken);

        if (Period is { } period)
        {
            Routes = await Reports.GetRouteSummariesAsync(period, cancellationToken);
        }
    }
}
