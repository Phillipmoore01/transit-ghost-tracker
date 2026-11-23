using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Transit.Data.Reports;

namespace Transit.Web.Pages;

public abstract class ReportPageModel(ReliabilityReports reports) : PageModel
{
    public static readonly int[] DayOptions = [7, 30, 90];

    private const int DefaultDays = 30;

    [BindProperty(SupportsGet = true)]
    public int Days { get; set; } = DefaultDays;

    public ReportPeriod? Period { get; private set; }

    protected ReliabilityReports Reports { get; } = reports;

    protected async Task LoadPeriodAsync(CancellationToken cancellationToken)
    {
        if (!DayOptions.Contains(Days))
        {
            Days = DefaultDays;
        }

        Period = await Reports.GetLatestPeriodAsync(Days, cancellationToken);
    }
}
