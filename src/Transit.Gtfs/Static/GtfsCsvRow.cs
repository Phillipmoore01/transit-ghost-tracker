using System.Globalization;
using CsvHelper;

namespace Transit.Gtfs.Static;

internal sealed class GtfsCsvRow(string fileName, CsvParser parser, IReadOnlyDictionary<string, int> columns)
{
    public string Required(string column) =>
        Optional(column) ?? throw Error($"required field '{column}' is missing or empty");

    public string? Optional(string column)
    {
        var record = parser.Record!;
        if (!columns.TryGetValue(column, out var index) || index >= record.Length)
        {
            return null;
        }

        var value = record[index].Trim();
        return value.Length == 0 ? null : value;
    }

    public int RequiredInt(string column) => ParseInt(column, Required(column));

    public int? OptionalInt(string column) => Optional(column) is { } value ? ParseInt(column, value) : null;

    public double? OptionalDouble(string column)
    {
        if (Optional(column) is not { } value)
        {
            return null;
        }

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number
            : throw Error($"field '{column}' value '{value}' is not a number");
    }

    public bool RequiredFlag(string column) =>
        RequiredInt(column) switch
        {
            0 => false,
            1 => true,
            var other => throw Error($"field '{column}' value '{other}' is not 0 or 1"),
        };

    public DateOnly RequiredDate(string column) => ParseDate(column, Required(column));

    public DateOnly? OptionalDate(string column) => Optional(column) is { } value ? ParseDate(column, value) : null;

    public GtfsTime? OptionalTime(string column)
    {
        if (Optional(column) is not { } value)
        {
            return null;
        }

        try
        {
            return GtfsTime.Parse(value);
        }
        catch (FormatException ex)
        {
            throw Error(ex.Message.TrimEnd('.'));
        }
    }

    public FormatException Error(string message) => new($"{fileName} row {parser.Row}: {message}.");

    private int ParseInt(string column, string value) =>
        int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number)
            ? number
            : throw Error($"field '{column}' value '{value}' is not an integer");

    private DateOnly ParseDate(string column, string value) =>
        DateOnly.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw Error($"field '{column}' value '{value}' is not a yyyyMMdd date");
}
