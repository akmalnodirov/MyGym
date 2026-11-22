using System.Reflection;
using MyGym.Modules.ExerciseCatalog.Domain;
using MyGym.Modules.Identity.Domain;
using MyGym.Modules.Training.Domain.Programs;
using NetArchTest.Rules;
using Shouldly;

namespace MyGym.ArchitectureTests;

/// <summary>
/// Checks that every Domain project stays a pure model: no EF Core, no ASP.NET Core, no
/// Microsoft.Extensions.*, no Npgsql, no Telegram.Bot, no System.Text.Json, and no reach into
/// another module's internals. These are the layers the rest of the design assumes are testable
/// and persistence-agnostic in isolation.
/// </summary>
public class DomainPurityTests
{
    public static TheoryData<Assembly> DomainAssemblies => new()
    {
        typeof(TrainingProgram).Assembly,
        typeof(AppUser).Assembly,
        typeof(Exercise).Assembly
    };

    /// <summary>
    /// A violation looks like: someone adds a navigation property typed as an EF Core
    /// <c>DbSet&lt;T&gt;</c> to an aggregate, calls <c>IConfiguration</c> from a domain service, or
    /// reaches for <c>System.Text.Json</c> to serialize a value object inside Domain instead of
    /// leaving that to Infrastructure. NetArchTest inspects the assembly's actual type references
    /// (including attribute and field types), so this catches it even if no domain *.cs file
    /// literally writes a banned `using` — the dependency shows up in IL either way.
    /// </summary>
    [Theory]
    [MemberData(nameof(DomainAssemblies))]
    public void Domain_does_not_depend_on_infrastructure_concerns(Assembly domain)
    {
        var result = Types.InAssembly(domain)
            .Should()
            .NotHaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore",
                "Microsoft.Extensions",
                "Npgsql",
                "Telegram.Bot",
                "System.Text.Json")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(Describe(result));
    }

    /// <summary>
    /// A violation looks like: Training.Domain adding a direct reference to
    /// MyGym.Modules.Identity.Domain (instead of going through Identity.Contracts), or any Domain
    /// project referencing another module's Infrastructure. This inspects the assembly's actual
    /// referenced-assembly table (not source text), so it would catch the dependency even if it
    /// were introduced only to satisfy a generic constraint or an attribute, with nothing resembling
    /// a `using` directive anywhere in the diff.
    /// </summary>
    [Theory]
    [MemberData(nameof(DomainAssemblies))]
    public void Domain_only_references_SharedKernel_and_its_own_contracts(Assembly domain)
    {
        var moduleName = domain.GetName().Name!.Replace(".Domain", string.Empty);

        var forbidden = domain.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => name.StartsWith("MyGym.", StringComparison.Ordinal))
            .Where(name => name != "MyGym.SharedKernel" && name != $"{moduleName}.Contracts")
            .ToList();

        forbidden.ShouldBeEmpty(
            $"{domain.GetName().Name} may only reference MyGym.SharedKernel and " +
            $"{moduleName}.Contracts, but also references: {string.Join(", ", forbidden)}");
    }

    /// <summary>
    /// A violation looks like: <c>TrainingProgram.Days</c> changing from
    /// <c>IReadOnlyList&lt;ProgramDay&gt;</c> back to a public <c>List&lt;ProgramDay&gt;</c>, which
    /// would let a caller outside the aggregate call <c>.Add</c>/<c>.Remove</c> directly and bypass
    /// the aggregate's invariants. This is not vacuous: TrainingProgram.Days, WorkoutSession.Exercises,
    /// SessionExercise.Sets, and Exercise.PrimaryMuscles/SecondaryMuscles/Names/Aliases are all
    /// currently public IReadOnlyList-typed properties on an AggregateRoot&lt;TId&gt; subtype, so the
    /// reflection scan has real candidates to inspect on every run, not an empty set.
    /// </summary>
    [Theory]
    [MemberData(nameof(DomainAssemblies))]
    public void Aggregate_roots_do_not_expose_mutable_collections(Assembly domain)
    {
        var offenders = Types.InAssembly(domain)
            .That().Inherit(typeof(MyGym.SharedKernel.AggregateRoot<>))
            .GetTypes()
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Where(property => property.PropertyType.IsGenericType
                               && property.PropertyType.GetGenericTypeDefinition() == typeof(List<>))
            .Select(property => $"{property.DeclaringType!.Name}.{property.Name}")
            .ToList();

        offenders.ShouldBeEmpty($"Expose IReadOnlyList instead: {string.Join(", ", offenders)}");
    }

    private static string Describe(NetArchTest.Rules.TestResult result) =>
        result.FailingTypeNames is null
            ? "no failing types reported"
            : string.Join(Environment.NewLine, result.FailingTypeNames);
}
