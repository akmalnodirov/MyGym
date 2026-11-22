using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using MyGym.Modules.Training.Domain.ValueObjects;

namespace MyGym.Shared.Infrastructure.Persistence;

/// <summary>
/// RepRange and RestPeriod store as compact delimited text, and WarmupRamp as JSON. None of the
/// three is filtered or aggregated in a query — sessions carry their own snapshot — so columns
/// bought nothing over readable, single-column storage.
/// </summary>
public sealed class RepRangeConverter() : ValueConverter<RepRange, string>(
    range => $"{range.Min}-{range.Max}",
    text => Parse(text))
{
    private static RepRange Parse(string text)
    {
        var parts = text.Split('-', 2);
        return RepRange.Create(int.Parse(parts[0]), int.Parse(parts[1])).Value;
    }
}

public sealed class RestPeriodConverter() : ValueConverter<RestPeriod, string>(
    rest => $"{(int)rest.Min.TotalSeconds}-{(int)rest.Max.TotalSeconds}",
    text => Parse(text))
{
    private static RestPeriod Parse(string text)
    {
        var parts = text.Split('-', 2);
        return RestPeriod.Create(
            TimeSpan.FromSeconds(int.Parse(parts[0])),
            TimeSpan.FromSeconds(int.Parse(parts[1]))).Value;
    }
}

internal sealed record WarmupStepDto(decimal Percent, int Reps, bool Optional);

public sealed class WarmupRampConverter() : ValueConverter<WarmupRamp, string>(
    ramp => Serialize(ramp),
    json => Deserialize(json))
{
    private static string Serialize(WarmupRamp ramp) =>
        JsonSerializer.Serialize(ramp.Steps
            .Select(s => new WarmupStepDto(s.PercentOfWorking, s.Reps, s.IsOptional))
            .ToList());

    private static WarmupRamp Deserialize(string json)
    {
        var dtos = JsonSerializer.Deserialize<List<WarmupStepDto>>(json) ?? [];
        return WarmupRamp.Create(
            dtos.Select(d => WarmupStep.Create(d.Percent, d.Reps, d.Optional).Value)).Value;
    }
}

public sealed class WarmupRampComparer() : ValueComparer<WarmupRamp>(
    (left, right) => left!.Equals(right),
    ramp => ramp.GetHashCode());
