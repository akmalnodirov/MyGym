using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using MyGym.Modules.Training.Domain.ValueObjects;
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Infrastructure;

/// <summary>
/// RepRange and RestPeriod store as compact delimited text, and WarmupRamp as JSON. None of the
/// three is filtered or aggregated in a query — sessions carry their own snapshot — so columns
/// bought nothing over readable, single-column storage.
/// </summary>
/// <remarks>
/// I1: lives in <c>Training.Infrastructure</c>, not <c>Shared.Infrastructure</c, because nothing
/// outside Training.Infrastructure consumes these converters — the previous location forced every
/// other module's Infrastructure (and the host) to carry a ProjectReference to Training.Domain
/// purely to host three converters nobody else used.
/// </remarks>
public sealed class RepRangeConverter() : ValueConverter<RepRange, string>(
    range => $"{range.Min}-{range.Max}",
    text => Parse(text))
{
    private static RepRange Parse(string text)
    {
        var parts = text.Split('-', 2);
        var range = RepRange.Create(int.Parse(parts[0]), int.Parse(parts[1]));

        // I13: a malformed stored value should be unreachable — the same RepRange.Create runs on
        // every write before a row can exist at all — so this is a DomainException (spec §9's
        // "unreachable state" mechanism), not the bare InvalidOperationException Result<T>.Value
        // would otherwise throw with no context about which row or which validation failed.
        return range.IsSuccess
            ? range.Value
            : throw new DomainException(
                $"Stored rep_range '{text}' failed RepRange validation ({range.Error.Code}): " +
                "this should be unreachable since the same validation runs on every write.");
    }
}

public sealed class RestPeriodConverter() : ValueConverter<RestPeriod, string>(
    rest => $"{(int)rest.Min.TotalSeconds}-{(int)rest.Max.TotalSeconds}",
    text => Parse(text))
{
    private static RestPeriod Parse(string text)
    {
        var parts = text.Split('-', 2);
        var rest = RestPeriod.Create(
            TimeSpan.FromSeconds(int.Parse(parts[0])),
            TimeSpan.FromSeconds(int.Parse(parts[1])));

        // I13: see RepRangeConverter.Parse above — same reasoning, same mechanism.
        return rest.IsSuccess
            ? rest.Value
            : throw new DomainException(
                $"Stored rest_period '{text}' failed RestPeriod validation ({rest.Error.Code}): " +
                "this should be unreachable since the same validation runs on every write.");
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
        var steps = dtos.Select(d => WarmupStep.Create(d.Percent, d.Reps, d.Optional));
        var ramp = WarmupRamp.Create(MaterializeOrThrow(steps));

        return ramp.IsSuccess
            ? ramp.Value
            : throw new DomainException(
                $"Stored warmup ramp '{json}' failed WarmupRamp validation ({ramp.Error.Code}): " +
                "this should be unreachable since the same validation runs on every write.");
    }

    // I13: WarmupStep.Create can also fail (e.g. a percentage outside (0,1)); surfacing that as a
    // DomainException too, with the same "should be unreachable" framing, rather than letting
    // Result<T>.Value throw a bare InvalidOperationException for one failure and a DomainException
    // for the other.
    private static IEnumerable<WarmupStep> MaterializeOrThrow(IEnumerable<Result<WarmupStep>> results)
    {
        foreach (var result in results)
        {
            yield return result.IsSuccess
                ? result.Value
                : throw new DomainException(
                    $"A stored warmup step failed WarmupStep validation ({result.Error.Code}): " +
                    "this should be unreachable since the same validation runs on every write.");
        }
    }
}

public sealed class WarmupRampComparer() : ValueComparer<WarmupRamp>(
    (left, right) => left!.Equals(right),
    ramp => ramp.GetHashCode());
