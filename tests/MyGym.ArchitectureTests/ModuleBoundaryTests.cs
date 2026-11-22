using System.Reflection;
using Shouldly;

namespace MyGym.ArchitectureTests;

public class ModuleBoundaryTests
{
    private static readonly string[] Modules = ["Training", "Identity", "ExerciseCatalog"];

    /// <summary>
    /// Every assembly that makes up a module: Domain, Application, Infrastructure, and — this is
    /// the fix for the dataset that used to contain zero Contracts assemblies — each module's
    /// Contracts project too. Without the three Contracts entries,
    /// <see cref="Contracts_assemblies_stay_dependency_free"/> ran once per assembly here, found no
    /// assembly whose name ended in ".Contracts", and returned (test passes) every single time:
    /// a test that cannot fail. Adding them gives it real input.
    /// </summary>
    private static readonly Assembly[] AllModuleAssemblies =
    [
        typeof(Modules.Training.Domain.Programs.TrainingProgram).Assembly,
        typeof(Modules.Training.Application.Programs.CreateProgramCommand).Assembly,
        typeof(Modules.Training.Infrastructure.TrainingDbContext).Assembly,
        typeof(Modules.Training.Contracts.ITrainingDataPurger).Assembly,
        typeof(Modules.Identity.Domain.AppUser).Assembly,
        typeof(Modules.Identity.Application.GetProfileQuery).Assembly,
        typeof(Modules.Identity.Infrastructure.IdentityDbContext).Assembly,
        typeof(Modules.Identity.Contracts.IAthleteProfileQueries).Assembly,
        typeof(Modules.ExerciseCatalog.Domain.Exercise).Assembly,
        typeof(Modules.ExerciseCatalog.Infrastructure.CatalogDbContext).Assembly,
        typeof(Modules.ExerciseCatalog.Contracts.IExerciseCatalogQueries).Assembly
    ];

    public static TheoryData<Assembly> ModuleAssemblies
    {
        get
        {
            var data = new TheoryData<Assembly>();
            foreach (var assembly in AllModuleAssemblies)
            {
                data.Add(assembly);
            }

            return data;
        }
    }

    /// <summary>
    /// The load-bearing rule of the whole design: a module may reach another module only through
    /// its Contracts assembly.
    ///
    /// A violation looks like: MyGym.Modules.Training.Infrastructure adding a direct
    /// ProjectReference to MyGym.Modules.Identity.Infrastructure (or .Domain, or .Application) to
    /// take a shortcut instead of going through MyGym.Modules.Identity.Contracts. This inspects
    /// <see cref="Assembly.GetReferencedAssemblies"/> — the actual compiled dependency list, not
    /// source text — so a violation introduced via a transitive-looking "helper" project, or via a
    /// type used only in a generic constraint, is still caught: it has to show up as a referenced
    /// assembly for the code to compile and run at all.
    /// </summary>
    [Theory]
    [MemberData(nameof(ModuleAssemblies))]
    public void A_module_only_reaches_other_modules_through_their_contracts(Assembly assembly)
    {
        var name = assembly.GetName().Name!;
        var ownModule = Modules.SingleOrDefault(m => name.Contains($".{m}.", StringComparison.Ordinal));
        ownModule.ShouldNotBeNull($"{name} does not look like a module assembly.");

        var violations = assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(referenced => referenced.StartsWith("MyGym.Modules.", StringComparison.Ordinal))
            .Where(referenced => !referenced.Contains($".{ownModule}.", StringComparison.Ordinal))
            .Where(referenced => !referenced.EndsWith(".Contracts", StringComparison.Ordinal))
            .ToList();

        violations.ShouldBeEmpty(
            $"{name} reaches into another module's internals: {string.Join(", ", violations)}");
    }

    /// <summary>
    /// Contracts projects must stay thin: a dumping ground for shared event records and query
    /// interfaces, never a place to smuggle in a dependency on another module or a concrete
    /// implementation detail.
    ///
    /// A violation looks like: MyGym.Modules.Training.Contracts adding a ProjectReference to
    /// MyGym.Modules.Identity.Domain so an event record can carry an Identity-module type directly,
    /// instead of a primitive or a SharedKernel-owned id type. With the three Contracts assemblies
    /// now in <see cref="ModuleAssemblies"/> (see the comment there), this theory actually runs its
    /// body for Training.Contracts, Identity.Contracts, and ExerciseCatalog.Contracts — previously
    /// the early-return fired for every input and the assertion below was unreachable.
    /// </summary>
    [Theory]
    [MemberData(nameof(ModuleAssemblies))]
    public void Contracts_assemblies_stay_dependency_free(Assembly assembly)
    {
        if (!assembly.GetName().Name!.EndsWith(".Contracts", StringComparison.Ordinal))
            return;

        var violations = assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(referenced => referenced.StartsWith("MyGym.", StringComparison.Ordinal))
            .Where(referenced => referenced != "MyGym.SharedKernel")
            .ToList();

        violations.ShouldBeEmpty($"Contracts must stay thin: {string.Join(", ", violations)}");
    }

    /// <summary>
    /// The host is the composition root: the one place allowed to know about every module's
    /// concrete Infrastructure, because it is the only project that wires DbContexts, repositories,
    /// and endpoints together into one running process.
    ///
    /// This replaces the brief's original version, which asserted
    /// <c>host.GetReferencedAssemblies().Count(name => name.StartsWith("MyGym.Modules.")).ShouldBeGreaterThan(3)</c>
    /// — true today (the host has 5 such references) but true of almost any plausible host, and it
    /// says nothing about the word "only" in the test's own name: it never looks at any assembly
    /// besides the host, so a second project quietly wiring up all three modules' Infrastructure
    /// would sail through unnoticed.
    ///
    /// The version below checks two things instead:
    ///   1. the host directly references all three modules' Infrastructure assemblies (a
    ///      regression here means the host silently stopped wiring up a module — e.g. someone
    ///      deletes the ExerciseCatalog.Infrastructure ProjectReference from MyGym.Api.csproj and
    ///      the app still compiles, just missing a module);
    ///   2. no other assembly in <see cref="ModuleAssemblies"/> references more than one of those
    ///      three Infrastructure assemblies (a violation looks like Identity.Infrastructure or
    ///      Training.Application picking up a second module's Infrastructure ProjectReference —
    ///      which would also make it a second, illegitimate composition root).
    /// Both checks have to pass; today they do, because by construction no module project
    /// references any Infrastructure assembly other than its own, and the host references all three.
    /// </summary>
    [Fact]
    public void The_api_host_is_the_only_project_that_references_every_modules_infrastructure()
    {
        var host = typeof(Program).Assembly;

        var infrastructureAssemblyNames = new[]
        {
            typeof(Modules.Training.Infrastructure.TrainingDbContext).Assembly.GetName().Name!,
            typeof(Modules.Identity.Infrastructure.IdentityDbContext).Assembly.GetName().Name!,
            typeof(Modules.ExerciseCatalog.Infrastructure.CatalogDbContext).Assembly.GetName().Name!
        };

        var hostReferences = host.GetReferencedAssemblies().Select(a => a.Name!).ToHashSet();

        var missing = infrastructureAssemblyNames.Where(name => !hostReferences.Contains(name)).ToList();
        missing.ShouldBeEmpty(
            $"The host must wire up every module's Infrastructure, but is missing: {string.Join(", ", missing)}");

        foreach (var assembly in AllModuleAssemblies)
        {
            var references = assembly.GetReferencedAssemblies().Select(a => a.Name!).ToHashSet();
            var touched = infrastructureAssemblyNames.Where(references.Contains).ToList();

            touched.Count.ShouldBeLessThanOrEqualTo(1,
                $"{assembly.GetName().Name} references more than one module's Infrastructure " +
                $"({string.Join(", ", touched)}), which makes it a second composition root.");
        }
    }
}
