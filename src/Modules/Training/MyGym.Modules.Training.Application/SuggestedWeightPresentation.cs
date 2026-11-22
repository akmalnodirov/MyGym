using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Application;

/// <summary>
/// C1: a suggested/last-working weight carries the unit it was originally <em>logged</em> in. After
/// a <c>PATCH /api/me</c> changes the athlete's <see cref="WeightUnit"/> preference, that can differ
/// from the unit the current request's <c>WeightIncrement</c> was built in. Combining the two
/// directly throws from <see cref="Weight.RoundDownTo"/> (it requires matching units), and reporting
/// the raw logged value next to the current-preference unit label is wrong by
/// <see cref="Weight.PoundsPerKilogram"/>. Both call sites that turn history into a response —
/// <c>SessionMapper.ToResponse</c> and <c>PreviewProgramDayQueryHandler</c> — convert through here
/// before doing anything else with a suggested weight.
/// </summary>
internal static class SuggestedWeightPresentation
{
    /// <summary>Converts a weight into <paramref name="unit"/>, leaving it unchanged if it is
    /// already in that unit.</summary>
    public static Weight ConvertTo(Weight weight, WeightUnit unit)
    {
        if (weight.Unit == unit)
            return weight;

        var kilograms = weight.ToKilograms();
        var converted = unit == WeightUnit.Kilograms ? kilograms : kilograms * Weight.PoundsPerKilogram;
        return Weight.Create(converted, unit).Value;
    }

    /// <summary>Converts a suggestion, if present, into <paramref name="unit"/>; returns null
    /// unchanged so callers can still distinguish "no history" from "zero".</summary>
    public static Weight? ConvertOrNull(Weight? suggested, WeightUnit unit) =>
        suggested is null ? null : ConvertTo(suggested, unit);
}
