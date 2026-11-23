using Microsoft.AspNetCore.Mvc;
using Transit.Data.Reports;

namespace Transit.Web.Pages.Stops;

public class DetailsModel(ReliabilityReports reports) : ReportPageModel(reports)
{
    public StopReport? Report { get; private set; }

    public async Task<IActionResult> OnGetAsync(string stopId, CancellationToken cancellationToken)
    {
        await LoadPeriodAsync(cancellationToken);

        if (Period is not { } period)
        {
            return Page();
        }

        Report = await Reports.GetStopReportAsync(stopId, period, cancellationToken);
        return Report is null ? NotFound() : Page();
    }
}
