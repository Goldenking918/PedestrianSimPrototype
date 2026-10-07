using System.Collections.Generic;
using System.Globalization;

/// <summary>
/// The results of one scenario run = one row in measurement_results.csv, plus the run's events (one row each in
/// measurement_events.csv). A value of NaN means "not applicable" and is written as an empty cell (Excel's AVERAGE etc.
/// ignore empty cells).
/// </summary>
public class MeasurementResult
{
    public const string CsvHeader =
        "Scenario,Scenario ID,Run,Participant ID,Date Time,App Version,Random Seed,Scenario Settings,Midpoint Flip," +
        "Waiting Time (s),Crossing Duration (s),Closest Vehicle Distance (m),Relative Speed (m/s),TTC (s),Crossing Outcome";

    public string scenario = "";
    public string scenarioId = "";      // stable short code from the scenario file, e.g. S1
    public int run;
    public string participantId = "";
    public string dateTime = "";
    public string appVersion = "";      // Project Settings > Player > Version of the build that recorded the run
    public int randomSeed;              // seed of the traffic random number generator
    public string scenarioSettings = ""; // short code that changes whenever any scenario setting changes
    public string midpointFlip = "";    // On or Off: whether this run used the midpoint pause and view flip

    public float waitingTime = float.NaN;
    public float crossingDuration = float.NaN;
    public float closestVehicleDistance = float.NaN;
    public float relativeSpeed = float.NaN;
    public float timeToCollision = float.NaN;
    public string crossingOutcome = "Incomplete"; // Completed, Incomplete or Collision

    /// <summary>Events of this run in the order they happened.</summary>
    public readonly List<MeasurementEvent> events = new List<MeasurementEvent>();

    public string ToCsvRow()
    {
        return string.Join(",",
            Text(scenario), Text(scenarioId), run.ToString(CultureInfo.InvariantCulture), Text(participantId), Text(dateTime),
            Text(appVersion), randomSeed.ToString(CultureInfo.InvariantCulture), Text(scenarioSettings), Text(midpointFlip),
            Number(waitingTime), Number(crossingDuration), Number(closestVehicleDistance),
            Number(relativeSpeed), Number(timeToCollision), Text(crossingOutcome));
    }

    // Always uses '.' as the decimal point, whatever the computer's regional settings.
    internal static string Number(float value) =>
        float.IsNaN(value) || float.IsInfinity(value) ? "" : value.ToString("F2", CultureInfo.InvariantCulture);

    // Quotes text that contains a comma or quote so it stays in one cell.
    internal static string Text(string value)
    {
        value = value ?? "";
        return value.Contains(",") || value.Contains("\"") ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }
}

/// <summary>
/// Something that happened during a run, with its time = one row in measurement_events.csv.
/// Rows are linked to measurement_results.csv by the Run column.
/// </summary>
public class MeasurementEvent
{
    public const string CsvHeader = "Run,Participant ID,Scenario ID,Time (s),Event,Vehicle ID,Details";

    public float time;          // seconds since the scenario started
    public string name = "";
    public int vehicleId = -1;  // -1 = not about a particular vehicle
    public string details = "";

    public string ToCsvRow(MeasurementResult run)
    {
        return string.Join(",",
            run.run.ToString(CultureInfo.InvariantCulture), MeasurementResult.Text(run.participantId),
            MeasurementResult.Text(run.scenarioId), MeasurementResult.Number(time), MeasurementResult.Text(name),
            vehicleId >= 0 ? vehicleId.ToString(CultureInfo.InvariantCulture) : "", MeasurementResult.Text(details));
    }
}
