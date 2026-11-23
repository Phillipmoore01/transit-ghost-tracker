using Microsoft.AspNetCore.Mvc;
using Transit.Data.Reports;

namespace Transit.Web.Pages.Routes;

public class DetailsModel(ReliabilityReports reports) : ReportPageModel(reports)
{
    public RouteReport? Report { get; private set; }

    public TimeZoneInfo TimeZone { get; private set; } = TimeZoneInfo.Utc;

    public async Task<IActionResult> OnGetAsync(string routeId, CancellationToken cancellationToken)
    {
        await LoadPeriodAsync(cancellationToken);

        if (Period is not { } period)
        {
            return Page();
        }

        TimeZone = await Reports.GetAgencyTimeZoneAsync(cancellationToken) ?? TimeZoneInfo.Utc;
        Report = await Reports.GetRouteReportAsync(routeId, period, TimeZone, cancellationToken);

        return Report is null ? NotFound() : Page();
    }

    public TripCounts CellFor(DayType dayType, TimeOfDay timeOfDay) =>
        Report?.Cells.FirstOrDefault(c => c.DayType == dayType && c.TimeOfDay == timeOfDay)?.Trips
        ?? new TripCounts(0, 0);
}
