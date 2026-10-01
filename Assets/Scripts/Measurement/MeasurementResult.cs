using System.Globalization;

/// <summary>
/// The results of one scenario run = one row in the measurement CSV.
/// A value of NaN means "not applicable" and is written as an empty cell (Excel's AVERAGE etc. ignore empty cells).
/// </summary>
public class MeasurementResult
{
    public const string CsvHeader =
        "Scenario,Run,Participant ID,Date Time,Waiting Time (s),Crossing Duration (s)," +
        "Closest Vehicle Distance (m),Relative Speed (m/s),TTC (s),Crossing Outcome";

    public string scenario = "";
    public int run;
    public string participantId = "";
    public string dateTime = "";

    public float waitingTime = float.NaN;
    public float crossingDuration = float.NaN;
    public float closestVehicleDistance = float.NaN;
    public float relativeSpeed = float.NaN;
    public float timeToCollision = float.NaN;
    public string crossingOutcome = "Incomplete"; // Completed, Incomplete or Collision

    public string ToCsvRow()
    {
        return string.Join(",",
            Text(scenario), run.ToString(CultureInfo.InvariantCulture), Text(participantId), Text(dateTime),
            Number(waitingTime), Number(crossingDuration), Number(closestVehicleDistance),
            Number(relativeSpeed), Number(timeToCollision), Text(crossingOutcome));
    }

    // Always uses '.' as the decimal point, whatever the computer's regional settings.
    static string Number(float value) =>
        float.IsNaN(value) || float.IsInfinity(value) ? "" : value.ToString("F2", CultureInfo.InvariantCulture);

    // Quotes text that contains a comma or quote so it stays in one cell.
    static string Text(string value)
    {
        value = value ?? "";
        return value.Contains(",") || value.Contains("\"") ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }
}
