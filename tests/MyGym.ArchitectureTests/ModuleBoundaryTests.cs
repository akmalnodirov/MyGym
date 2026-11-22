using System.Reflection;
using System.Xml.Linq;
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
    /// source text — so a violation where the forbidden type is actually *used* (even only in a
    /// generic constraint) is still caught: it has to show up as a referenced assembly for the
    /// code to compile and run at all.
    ///
    /// I1 correction: this does <em>not</em> catch a violation introduced via a "helper" project
    /// that is referenced but never actually used — <c>MyGym.Shared.Infrastructure</c> carried a
    /// ProjectReference to Training.Domain for exactly this reason (hosting three converters
    /// nothing in its own source called), and Roslyn elides an unused reference from
    /// <c>GetReferencedAssemblies()</c>, so that violation was invisible to this test the entire
    /// time. An earlier version of this comment claimed otherwise; it was wrong. Catching that
    /// shape requires reading the <c>.csproj</c> reference graph directly — see
    /// <see cref="Shared_infrastructure_references_no_module_in_its_csproj_file"/>.
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

    /// <summary>
    /// I1: <c>MyGym.Shared.Infrastructure</c> is referenced by every module's Infrastructure and by
    /// the host, so a dependency it takes on one module reaches all the others transitively. It
    /// used to carry a ProjectReference to Training.Domain purely to host
    /// <c>TrainingValueConverters.cs</c> (now moved into Training.Infrastructure, the only project
    /// that used them) — invisible to <see cref="Shared_infrastructure_does_not_reference_any_module_assembly"/>
    /// below because nothing in Shared.Infrastructure's own compiled IL actually used a Training.Domain
    /// type, so Roslyn elided the reference from <see cref="Assembly.GetReferencedAssemblies"/>
    /// even though the .csproj still declared it and every consumer still carried it transitively.
    /// This reads the .csproj's own <c>&lt;ProjectReference&gt;</c> elements, which a metadata-only
    /// check cannot see, closing the exact blind spot the violation exploited.
    /// </summary>
    [Fact]
    public void Shared_infrastructure_references_no_module_in_its_csproj_file()
    {
        var csprojPath = FindRepositoryFile(Path.Combine(
            "src", "Shared", "MyGym.Shared.Infrastructure", "MyGym.Shared.Infrastructure.csproj"));

        var projectReferences = XDocument.Load(csprojPath)
            .Descendants("ProjectReference")
            .Select(e => (e.Attribute("Include")?.Value ?? string.Empty).Replace('\\', '/'))
            .ToList();

        var moduleReferences = projectReferences.Where(include => include.Contains("/Modules/")).ToList();

        moduleReferences.ShouldBeEmpty(
            "MyGym.Shared.Infrastructure.csproj declares a ProjectReference under Modules — every " +
            $"other module's Infrastructure (and the host) would then carry it transitively, whether " +
            $"or not Shared.Infrastructure's own source actually uses it: {string.Join(", ", moduleReferences)}");
    }

    /// <summary>
    /// Metadata-based companion to the .csproj check above. Kept as a separate assertion — not
    /// folded into <see cref="A_module_only_reaches_other_modules_through_their_contracts"/>'s
    /// dataset — because that theory assumes every input belongs to exactly one of
    /// <see cref="Modules"/> ("owned || Contracts" is the allowed shape); Shared.Infrastructure's
    /// correct shape is stricter still — no module reference of any kind, not even a Contracts one.
    /// </summary>
    [Fact]
    public void Shared_infrastructure_does_not_reference_any_module_assembly()
    {
        var assembly = typeof(MyGym.Shared.Infrastructure.Persistence.ModuleDbContext).Assembly;

        var violations = assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => name.StartsWith("MyGym.Modules.", StringComparison.Ordinal))
            .ToList();

        violations.ShouldBeEmpty(
            $"MyGym.Shared.Infrastructure must not reference any module assembly, but references: " +
            $"{string.Join(", ", violations)}");
    }

    private static string FindRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MyGym.sln")))
            directory = directory.Parent;

        if (directory is null)
            throw new InvalidOperationException(
                $"Could not locate the repository root (MyGym.sln) walking up from {AppContext.BaseDirectory}.");

        var fullPath = Path.Combine(directory.FullName, relativePath);
        File.Exists(fullPath).ShouldBeTrue($"Expected to find {fullPath}.");
        return fullPath;
    }
}
