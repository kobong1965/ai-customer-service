namespace AgentDesk.Core;

public static class SizingRecommendationEngine
{
    public static SizingMatchResult Evaluate(
        ProductSizingProfile profile,
        CustomerMeasurements measurements)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(measurements);

        var required = RequiredMeasurements(profile.Rows);
        var missing = new List<string>();
        if (required.Height && measurements.HeightCm is null) missing.Add("身高");
        if (required.Weight && measurements.WeightKg is null) missing.Add("体重");
        if (required.Waist && measurements.WaistCm is null) missing.Add("腰围");
        if (required.Bust && measurements.BustCm is null) missing.Add("胸围");
        if (missing.Count > 0)
        {
            return new SizingMatchResult(
                SizingMatchStatus.MissingMeasurements,
                null,
                $"还需要：{string.Join("、", missing)}");
        }

        var matches = profile.Rows.Where(row => Matches(row, measurements)).ToArray();
        return matches.Length switch
        {
            1 => new SizingMatchResult(
                SizingMatchStatus.Matched,
                matches[0],
                $"唯一命中 {matches[0].Size}；建议仍以商品尺码表和穿着偏好为准。"),
            0 => new SizingMatchResult(
                SizingMatchStatus.NoMatch,
                null,
                "没有命中任何尺码行，请核对数据或转人工。"),
            _ => new SizingMatchResult(
                SizingMatchStatus.MultipleMatches,
                null,
                $"同时命中 {string.Join("、", matches.Select(row => row.Size))}，需结合穿着偏好后由人工确认。")
        };
    }

    private static (bool Height, bool Weight, bool Waist, bool Bust) RequiredMeasurements(
        IReadOnlyList<SizeRecommendationRow> rows) => (
        rows.Any(row => row.MinHeightCm is not null || row.MaxHeightCm is not null),
        rows.Any(row => row.MinWeightKg is not null || row.MaxWeightKg is not null),
        rows.Any(row => row.MinWaistCm is not null || row.MaxWaistCm is not null),
        rows.Any(row => row.MinBustCm is not null || row.MaxBustCm is not null));

    private static bool Matches(SizeRecommendationRow row, CustomerMeasurements values) =>
        Within(values.HeightCm, row.MinHeightCm, row.MaxHeightCm)
        && Within(values.WeightKg, row.MinWeightKg, row.MaxWeightKg)
        && Within(values.WaistCm, row.MinWaistCm, row.MaxWaistCm)
        && Within(values.BustCm, row.MinBustCm, row.MaxBustCm);

    private static bool Within(double? value, double? minimum, double? maximum)
    {
        if (minimum is null && maximum is null)
        {
            return true;
        }

        return value is not null
            && (minimum is null || value >= minimum)
            && (maximum is null || value <= maximum);
    }
}
