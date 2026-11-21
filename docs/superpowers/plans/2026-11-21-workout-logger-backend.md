# MyGym Workout Logger — Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the MyGym workout logger backend — a .NET 10 modular monolith exposing a REST API that lets a Telegram Mini App author training programs, start sessions from a program day with auto-resolved warm-up weights, and log sets idempotently over an unreliable connection.

**Architecture:** Modular monolith with three bounded contexts (Identity, ExerciseCatalog, Training). Clean architecture layers live *inside* each module — `Domain`, `Application`, `Infrastructure`, `Contracts` — never across the solution. DDD tactical patterns: two aggregates in Training enforce their own invariants; sessions snapshot their prescription so logged history is immutable when programs are later edited. One PostgreSQL database with a schema per module and no cross-schema foreign keys.

**Tech Stack:** .NET 10 (SDK 10.0.303), ASP.NET Core Minimal APIs, EF Core 10 + Npgsql, PostgreSQL 17, xUnit v3, Shouldly, Testcontainers.PostgreSql, NetArchTest.Rules, Telegram.Bot, OpenTelemetry.

**Spec:** `docs/superpowers/specs/2026-11-21-mygym-workout-logger-design.md`

**Out of plan (separate plan):** the React/TypeScript Telegram Mini App client. This plan delivers the API it will consume.

## Global Constraints

- **Target framework:** `net10.0` for every project. No multi-targeting.
- **Nullable reference types:** enabled solution-wide. `<Nullable>enable</Nullable>`.
- **Warnings as errors:** `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` in `Directory.Build.props`.
- **`Domain` project references:** only `MyGym.SharedKernel` and its own module's `Contracts`. Never EF Core, ASP.NET Core, or `Microsoft.Extensions.*`. (This refines spec §4: a module's `Contracts` project carries zero dependencies and holds its event records, so `Domain` may reference it without taking on infrastructure. Referencing *another* module's `Domain`, `Application`, or `Infrastructure` remains forbidden.)
- **No mediator library.** Handlers are plain classes behind `ICommandHandler<TCommand, TResult>` / `IQueryHandler<TQuery, TResult>` defined in `MyGym.SharedKernel`, registered explicitly in DI. Rationale: MediatR v13+ is commercially licensed, and explicit registration keeps call sites navigable.
- **No FluentAssertions.** Use Shouldly — FluentAssertions v8+ requires a paid licence for commercial use.
- **Database schemas:** `identity`, `catalog`, `training`. One `DbContext` per module. No foreign keys across schemas.
- **Queries bypass the domain.** Query handlers project with EF Core directly to response DTOs. Never load an aggregate to serve a read.
- **Repositories are aggregate-granular.** No generic `IRepository<T>`.
- **All timestamps are `DateTimeOffset` in UTC.** Time is injected via `TimeProvider`, never `DateTimeOffset.UtcNow` inline, so tests control the clock.
- **Error codes are stable strings**, e.g. `session.already_in_progress`. Never user-facing English in the domain or application layers.
- **Weight limits:** `Weight.Value` is non-negative and at most 1000 kg equivalent. `LoggedSet.Reps` is 0–100. `WorkingSetCount` is 1–10.
- **Commit after every task.** Conventional Commit prefixes (`feat:`, `test:`, `chore:`, `fix:`).

---

## File Structure

```
Directory.Build.props                              # shared TFM, nullable, warnings-as-errors
Directory.Packages.props                           # central package version management
MyGym.sln

src/Shared/MyGym.SharedKernel/
  Entity.cs                                        # Entity<TId> identity equality
  AggregateRoot.cs                                 # AggregateRoot<TId>, domain event collection
  ValueObject.cs                                   # structural equality base
  Result.cs                                        # Result, Result<T>, Error
  IDomainEvent.cs
  Handlers.cs                                      # ICommandHandler, IQueryHandler
  Weight.cs                                        # Weight, WeightUnit, WeightIncrement
  Ids.cs                                           # strongly-typed id record structs

src/Modules/Training/
  MyGym.Modules.Training.Contracts/
    Events.cs                                      # WorkoutSessionStarted/Completed, ProgramActivated
  MyGym.Modules.Training.Domain/
    ValueObjects/RepRange.cs
    ValueObjects/RestPeriod.cs
    ValueObjects/WarmupRamp.cs                     # WarmupStep, WarmupRamp, ResolvedWarmupStep
    ValueObjects/PrescriptionSnapshot.cs
    ValueObjects/SessionOrigin.cs
    Programs/TrainingProgram.cs                    # aggregate root
    Programs/ProgramDay.cs
    Programs/ExercisePrescription.cs
    Programs/ITrainingProgramRepository.cs
    Sessions/WorkoutSession.cs                     # aggregate root
    Sessions/SessionExercise.cs
    Sessions/LoggedSet.cs
    Sessions/IWorkoutSessionRepository.cs
    TrainingErrors.cs                              # error code constants
  MyGym.Modules.Training.Application/              # command/query handlers + DTOs
  MyGym.Modules.Training.Infrastructure/           # TrainingDbContext, EF configs, repos, migrations

src/Modules/Identity/      MyGym.Modules.Identity.{Contracts,Domain,Application,Infrastructure}
src/Modules/ExerciseCatalog/ MyGym.Modules.ExerciseCatalog.{Contracts,Domain,Application,Infrastructure}

src/Shared/MyGym.Shared.Infrastructure/
  Auth/TelegramInitDataValidator.cs
  Auth/JwtTokenService.cs
  Errors/ProblemDetailsMapper.cs
  Persistence/DomainEventDispatcher.cs

src/Host/MyGym.Api/
  Program.cs                                       # composition root
  Endpoints/*.cs                                   # endpoint groups, one file per resource

tests/
  MyGym.SharedKernel.Tests/
  MyGym.Modules.Training.Domain.Tests/
  MyGym.Modules.Training.Application.Tests/
  MyGym.IntegrationTests/                          # Testcontainers Postgres + WebApplicationFactory
  MyGym.ArchitectureTests/
```

---

### Task 1: Solution scaffolding and SharedKernel primitives

**Files:**
- Create: `Directory.Build.props`, `Directory.Packages.props`, `MyGym.sln`
- Create: `src/Shared/MyGym.SharedKernel/{ValueObject,Entity,AggregateRoot,Result,IDomainEvent,Handlers}.cs`
- Test: `tests/MyGym.SharedKernel.Tests/{ResultTests,ValueObjectTests,EntityTests}.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `ValueObject` (abstract, `GetEqualityComponents()`), `Entity<TId>`, `AggregateRoot<TId>` (`DomainEvents`, `Raise(IDomainEvent)`, `ClearDomainEvents()`), `Error(string Code, string Message)`, `Result` (`IsSuccess`, `Error`, `Success()`, `Failure(Error)`), `Result<T>` (`Value`, `Success(T)`, `Failure(Error)`), `IDomainEvent`, `ICommandHandler<TCommand,TResult>`, `IQueryHandler<TQuery,TResult>`.

- [ ] **Step 1: Create the solution and shared build files**

```bash
mkdir -p src/Shared src/Modules src/Host tests
dotnet new sln -n MyGym
```

`Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
</Project>
```

`Directory.Packages.props`:

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="Microsoft.EntityFrameworkCore" Version="10.0.0" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.0" />
    <PackageVersion Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.0" />
    <PackageVersion Include="xunit.v3" Version="3.0.0" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="3.0.0" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageVersion Include="Shouldly" Version="4.2.1" />
    <PackageVersion Include="Testcontainers.PostgreSql" Version="4.0.0" />
    <PackageVersion Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.0" />
    <PackageVersion Include="NetArchTest.Rules" Version="1.3.2" />
    <PackageVersion Include="Telegram.Bot" Version="22.0.0" />
    <PackageVersion Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.0.0" />
    <PackageVersion Include="OpenTelemetry.Extensions.Hosting" Version="1.10.0" />
    <PackageVersion Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.10.0" />
    <PackageVersion Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.10.0" />
  </ItemGroup>
</Project>
```

> If `dotnet restore` reports a listed version does not exist, run `dotnet package search <id> --take 1` and pin the newest stable version for `net10.0`. Do not downgrade the target framework.

```bash
dotnet new classlib -o src/Shared/MyGym.SharedKernel
dotnet new xunit3 -o tests/MyGym.SharedKernel.Tests
dotnet sln add src/Shared/MyGym.SharedKernel tests/MyGym.SharedKernel.Tests
dotnet add tests/MyGym.SharedKernel.Tests reference src/Shared/MyGym.SharedKernel
dotnet add tests/MyGym.SharedKernel.Tests package Shouldly
rm src/Shared/MyGym.SharedKernel/Class1.cs
```

- [ ] **Step 2: Write the failing tests**

`tests/MyGym.SharedKernel.Tests/ResultTests.cs`:

```csharp
using MyGym.SharedKernel;
using Shouldly;

namespace MyGym.SharedKernel.Tests;

public class ResultTests
{
    [Fact]
    public void Success_result_has_no_error()
    {
        var result = Result.Success();

        result.IsSuccess.ShouldBeTrue();
        result.Error.ShouldBe(Error.None);
    }

    [Fact]
    public void Failure_result_carries_its_error()
    {
        var error = new Error("test.failed", "it failed");

        var result = Result.Failure(error);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBe(error);
    }

    [Fact]
    public void Generic_success_exposes_value()
    {
        var result = Result<int>.Success(42);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
    }

    [Fact]
    public void Accessing_value_of_failure_throws()
    {
        var result = Result<int>.Failure(new Error("test.failed", "it failed"));

        Should.Throw<InvalidOperationException>(() => result.Value);
    }
}
```

`tests/MyGym.SharedKernel.Tests/ValueObjectTests.cs`:

```csharp
using MyGym.SharedKernel;
using Shouldly;

namespace MyGym.SharedKernel.Tests;

public class ValueObjectTests
{
    private sealed class Money(decimal amount, string currency) : ValueObject
    {
        public decimal Amount { get; } = amount;
        public string Currency { get; } = currency;

        protected override IEnumerable<object?> GetEqualityComponents()
        {
            yield return Amount;
            yield return Currency;
        }
    }

    [Fact]
    public void Value_objects_with_equal_components_are_equal()
    {
        new Money(5m, "USD").ShouldBe(new Money(5m, "USD"));
        new Money(5m, "USD").GetHashCode().ShouldBe(new Money(5m, "USD").GetHashCode());
    }

    [Fact]
    public void Value_objects_with_different_components_are_not_equal()
    {
        new Money(5m, "USD").ShouldNotBe(new Money(5m, "EUR"));
    }
}
```

`tests/MyGym.SharedKernel.Tests/EntityTests.cs`:

```csharp
using MyGym.SharedKernel;
using Shouldly;

namespace MyGym.SharedKernel.Tests;

public class EntityTests
{
    private sealed record TestEvent(string What) : IDomainEvent;

    private sealed class Order(Guid id, string label) : AggregateRoot<Guid>(id)
    {
        public string Label { get; private set; } = label;

        public void Rename(string label)
        {
            Label = label;
            Raise(new TestEvent(label));
        }
    }

    [Fact]
    public void Entities_are_equal_by_id_not_by_state()
    {
        var id = Guid.NewGuid();

        new Order(id, "a").ShouldBe(new Order(id, "b"));
        new Order(Guid.NewGuid(), "a").ShouldNotBe(new Order(Guid.NewGuid(), "a"));
    }

    [Fact]
    public void Raised_events_are_collected_and_clearable()
    {
        var order = new Order(Guid.NewGuid(), "a");

        order.Rename("b");
        order.DomainEvents.Count.ShouldBe(1);

        order.ClearDomainEvents();
        order.DomainEvents.ShouldBeEmpty();
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/MyGym.SharedKernel.Tests`
Expected: FAIL — compilation errors, `Result`/`ValueObject`/`AggregateRoot` do not exist.

- [ ] **Step 4: Write the minimal implementation**

`src/Shared/MyGym.SharedKernel/Result.cs`:

```csharp
namespace MyGym.SharedKernel;

public sealed record Error(string Code, string Message)
{
    public static readonly Error None = new(string.Empty, string.Empty);
}

public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        if (isSuccess && error != Error.None)
            throw new ArgumentException("A successful result cannot carry an error.", nameof(error));
        if (!isSuccess && error == Error.None)
            throw new ArgumentException("A failed result requires an error.", nameof(error));

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    public static Result Success() => new(true, Error.None);
    public static Result Failure(Error error) => new(false, error);
}

public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(bool isSuccess, T? value, Error error) : base(isSuccess, error) => _value = value;

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Cannot read the value of a failed result ({Error.Code}).");

    public static Result<T> Success(T value) => new(true, value, Error.None);
    public static new Result<T> Failure(Error error) => new(false, default, error);
}
```

`src/Shared/MyGym.SharedKernel/ValueObject.cs`:

```csharp
namespace MyGym.SharedKernel;

public abstract class ValueObject : IEquatable<ValueObject>
{
    protected abstract IEnumerable<object?> GetEqualityComponents();

    public bool Equals(ValueObject? other) =>
        other is not null
        && other.GetType() == GetType()
        && GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());

    public override bool Equals(object? obj) => obj is ValueObject other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var component in GetEqualityComponents())
            hash.Add(component);
        return hash.ToHashCode();
    }
}
```

`src/Shared/MyGym.SharedKernel/IDomainEvent.cs`:

```csharp
namespace MyGym.SharedKernel;

public interface IDomainEvent;
```

`src/Shared/MyGym.SharedKernel/Entity.cs`:

```csharp
namespace MyGym.SharedKernel;

public abstract class Entity<TId>(TId id) : IEquatable<Entity<TId>>
    where TId : notnull
{
    public TId Id { get; private set; } = id;

    public bool Equals(Entity<TId>? other) =>
        other is not null && other.GetType() == GetType() && other.Id.Equals(Id);

    public override bool Equals(object? obj) => obj is Entity<TId> other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();
}
```

`src/Shared/MyGym.SharedKernel/AggregateRoot.cs`:

```csharp
namespace MyGym.SharedKernel;

public abstract class AggregateRoot<TId>(TId id) : Entity<TId>(id)
    where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
```

`src/Shared/MyGym.SharedKernel/Handlers.cs`:

```csharp
namespace MyGym.SharedKernel;

public interface ICommandHandler<in TCommand, TResult>
{
    Task<Result<TResult>> HandleAsync(TCommand command, CancellationToken ct);
}

public interface ICommandHandler<in TCommand>
{
    Task<Result> HandleAsync(TCommand command, CancellationToken ct);
}

public interface IQueryHandler<in TQuery, TResult>
{
    Task<Result<TResult>> HandleAsync(TQuery query, CancellationToken ct);
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/MyGym.SharedKernel.Tests`
Expected: PASS — 8 tests.

- [ ] **Step 6: Commit**

```bash
git add Directory.Build.props Directory.Packages.props MyGym.sln src/Shared/MyGym.SharedKernel tests/MyGym.SharedKernel.Tests
git commit -m "feat: add solution scaffolding and SharedKernel primitives"
```

---

### Task 2: Weight, units, and plate-aware rounding

**Files:**
- Create: `src/Shared/MyGym.SharedKernel/Weight.cs`, `src/Shared/MyGym.SharedKernel/Ids.cs`
- Test: `tests/MyGym.SharedKernel.Tests/WeightTests.cs`

**Interfaces:**
- Consumes: `ValueObject`, `Result<T>`, `Error` from Task 1.
- Produces: `WeightUnit` (`Kilograms`, `Pounds`), `Weight` (`Value`, `Unit`, `Weight.Create(decimal, WeightUnit) → Result<Weight>`, `Weight.Kg(decimal)`, `ToKilograms()`, `Scale(decimal) → Weight`, `RoundDownTo(WeightIncrement) → Weight`, `CompareTo`), `WeightIncrement` (`Value`, `Unit`, `WeightIncrement.Create(decimal, WeightUnit) → Result<WeightIncrement>`, `WeightIncrement.DefaultKg` = 2.5 kg), and the strongly-typed ids `UserId`, `ProgramId`, `ProgramDayId`, `PrescriptionId`, `SessionId`, `SessionExerciseId`, `LoggedSetId`, `ExerciseId` — each a `readonly record struct` wrapping `Guid` with `New()` and `From(Guid)`.

- [ ] **Step 1: Write the failing tests**

`tests/MyGym.SharedKernel.Tests/WeightTests.cs`:

```csharp
using MyGym.SharedKernel;
using Shouldly;

namespace MyGym.SharedKernel.Tests;

public class WeightTests
{
    [Fact]
    public void Negative_weight_is_rejected()
    {
        var result = Weight.Create(-1m, WeightUnit.Kilograms);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("weight.negative");
    }

    [Fact]
    public void Weight_above_the_sane_maximum_is_rejected()
    {
        Weight.Create(1000.1m, WeightUnit.Kilograms).Error.Code.ShouldBe("weight.exceeds_maximum");
    }

    [Fact]
    public void Pounds_convert_to_kilograms()
    {
        Weight.Create(100m, WeightUnit.Pounds).Value
            .ToKilograms().ShouldBe(45.359m, 0.001m);
    }

    [Theory]
    [InlineData(80, 0.27, 21.6)]
    [InlineData(80, 0.53, 42.4)]
    [InlineData(80, 0.80, 64.0)]
    public void Scaling_multiplies_the_value_and_keeps_the_unit(double working, double factor, double expected)
    {
        var scaled = Weight.Kg((decimal)working).Scale((decimal)factor);

        scaled.Value.ShouldBe((decimal)expected, 0.01m);
        scaled.Unit.ShouldBe(WeightUnit.Kilograms);
    }

    [Theory]
    [InlineData(21.6, 2.5, 20.0)]
    [InlineData(42.4, 2.5, 40.0)]
    [InlineData(64.0, 2.5, 62.5)]
    [InlineData(27.5, 2.5, 27.5)]
    public void Rounding_down_lands_on_a_loadable_increment(double raw, double increment, double expected)
    {
        var rounded = Weight.Kg((decimal)raw)
            .RoundDownTo(WeightIncrement.Create((decimal)increment, WeightUnit.Kilograms).Value);

        rounded.Value.ShouldBe((decimal)expected, 0.001m);
    }

    [Fact]
    public void Rounding_never_produces_a_negative_weight()
    {
        Weight.Kg(1m).RoundDownTo(WeightIncrement.DefaultKg).Value.ShouldBe(0m);
    }

    [Fact]
    public void Rounding_rejects_a_mismatched_unit()
    {
        var poundIncrement = WeightIncrement.Create(5m, WeightUnit.Pounds).Value;

        Should.Throw<InvalidOperationException>(() => Weight.Kg(100m).RoundDownTo(poundIncrement));
    }

    [Fact]
    public void Zero_or_negative_increments_are_rejected()
    {
        WeightIncrement.Create(0m, WeightUnit.Kilograms).Error.Code.ShouldBe("weight_increment.not_positive");
    }
}
```

> Rounding is deliberately **downward**. A warm-up set rounded up can exceed the intended percentage; rounding down is always safe.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/MyGym.SharedKernel.Tests --filter WeightTests`
Expected: FAIL — `Weight` does not exist.

- [ ] **Step 3: Write the minimal implementation**

`src/Shared/MyGym.SharedKernel/Weight.cs`:

```csharp
namespace MyGym.SharedKernel;

public enum WeightUnit
{
    Kilograms = 1,
    Pounds = 2
}

public sealed class WeightIncrement : ValueObject
{
    public const string NotPositiveCode = "weight_increment.not_positive";

    private WeightIncrement(decimal value, WeightUnit unit)
    {
        Value = value;
        Unit = unit;
    }

    public decimal Value { get; }
    public WeightUnit Unit { get; }

    public static WeightIncrement DefaultKg { get; } = new(2.5m, WeightUnit.Kilograms);

    public static Result<WeightIncrement> Create(decimal value, WeightUnit unit) =>
        value <= 0m
            ? Result<WeightIncrement>.Failure(new Error(NotPositiveCode, "Increment must be positive."))
            : Result<WeightIncrement>.Success(new WeightIncrement(value, unit));

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
        yield return Unit;
    }
}

public sealed class Weight : ValueObject, IComparable<Weight>
{
    public const decimal MaximumKilograms = 1000m;
    public const decimal PoundsPerKilogram = 2.20462262m;

    private Weight(decimal value, WeightUnit unit)
    {
        Value = value;
        Unit = unit;
    }

    public decimal Value { get; }
    public WeightUnit Unit { get; }

    public static Weight Zero { get; } = new(0m, WeightUnit.Kilograms);

    public static Result<Weight> Create(decimal value, WeightUnit unit)
    {
        if (value < 0m)
            return Result<Weight>.Failure(new Error("weight.negative", "Weight cannot be negative."));

        var kilograms = unit == WeightUnit.Kilograms ? value : value / PoundsPerKilogram;
        if (kilograms > MaximumKilograms)
            return Result<Weight>.Failure(
                new Error("weight.exceeds_maximum", $"Weight cannot exceed {MaximumKilograms} kg."));

        return Result<Weight>.Success(new Weight(value, unit));
    }

    /// <summary>Trusted factory for values already known to be valid (constants, persisted data).</summary>
    public static Weight Kg(decimal value) => Create(value, WeightUnit.Kilograms).Value;

    public decimal ToKilograms() =>
        Unit == WeightUnit.Kilograms ? Value : Value / PoundsPerKilogram;

    public Weight Scale(decimal factor) => new(Value * factor, Unit);

    public Weight RoundDownTo(WeightIncrement increment)
    {
        if (increment.Unit != Unit)
            throw new InvalidOperationException(
                $"Cannot round a weight in {Unit} to an increment in {increment.Unit}.");

        var steps = Math.Floor(Value / increment.Value);
        return new Weight(Math.Max(0m, steps * increment.Value), Unit);
    }

    public int CompareTo(Weight? other) =>
        other is null ? 1 : ToKilograms().CompareTo(other.ToKilograms());

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
        yield return Unit;
    }
}
```

`src/Shared/MyGym.SharedKernel/Ids.cs`:

```csharp
namespace MyGym.SharedKernel;

public readonly record struct UserId(Guid Value)
{
    public static UserId New() => new(Guid.CreateVersion7());
    public static UserId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}

public readonly record struct ProgramId(Guid Value)
{
    public static ProgramId New() => new(Guid.CreateVersion7());
    public static ProgramId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}

public readonly record struct ProgramDayId(Guid Value)
{
    public static ProgramDayId New() => new(Guid.CreateVersion7());
    public static ProgramDayId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}

public readonly record struct PrescriptionId(Guid Value)
{
    public static PrescriptionId New() => new(Guid.CreateVersion7());
    public static PrescriptionId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}

public readonly record struct SessionId(Guid Value)
{
    public static SessionId New() => new(Guid.CreateVersion7());
    public static SessionId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}

public readonly record struct SessionExerciseId(Guid Value)
{
    public static SessionExerciseId New() => new(Guid.CreateVersion7());
    public static SessionExerciseId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}

public readonly record struct LoggedSetId(Guid Value)
{
    public static LoggedSetId New() => new(Guid.CreateVersion7());
    public static LoggedSetId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}

public readonly record struct ExerciseId(Guid Value)
{
    public static ExerciseId New() => new(Guid.CreateVersion7());
    public static ExerciseId From(Guid value) => new(value);
    public override string ToString() => Value.ToString();
}
```

> `Guid.CreateVersion7()` produces time-ordered GUIDs, which keeps PostgreSQL B-tree index inserts sequential instead of scattering them.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/MyGym.SharedKernel.Tests`
Expected: PASS — 16 tests.

- [ ] **Step 5: Commit**

```bash
git add src/Shared/MyGym.SharedKernel tests/MyGym.SharedKernel.Tests
git commit -m "feat: add Weight value object with plate-aware rounding and typed ids"
```

---

### Task 3: Training value objects and warm-up resolution

This is the task the whole design exists to make testable. `WarmupRamp.Resolve` turns "27% × 10, 53% × 6, 80% × 3" plus a working weight into weights that can actually be loaded on a bar.

**Files:**
- Create: `src/Modules/Training/MyGym.Modules.Training.Domain/ValueObjects/{RepRange,RestPeriod,WarmupRamp}.cs`
- Create: `src/Modules/Training/MyGym.Modules.Training.Domain/TrainingErrors.cs`
- Test: `tests/MyGym.Modules.Training.Domain.Tests/{RepRangeTests,RestPeriodTests,WarmupRampTests}.cs`

**Interfaces:**
- Consumes: `ValueObject`, `Result<T>`, `Error`, `Weight`, `WeightIncrement` from Tasks 1–2.
- Produces: `RepRange` (`Min`, `Max`, `Create(int,int) → Result<RepRange>`), `RestPeriod` (`Min`, `Max` as `TimeSpan`, `Create(TimeSpan,TimeSpan) → Result<RestPeriod>`), `WarmupStep` (`PercentOfWorking`, `Reps`, `IsOptional`, `Create(decimal,int,bool) → Result<WarmupStep>`), `WarmupRamp` (`Steps`, `Empty`, `Create(IEnumerable<WarmupStep>) → Result<WarmupRamp>`, `Resolve(Weight, WeightIncrement) → IReadOnlyList<ResolvedWarmupStep>`), `ResolvedWarmupStep(Weight Weight, int Reps, bool IsOptional)`, `TrainingErrors` constants.

- [ ] **Step 1: Create the projects**

```bash
dotnet new classlib -o src/Modules/Training/MyGym.Modules.Training.Contracts
dotnet new classlib -o src/Modules/Training/MyGym.Modules.Training.Domain
dotnet new xunit3 -o tests/MyGym.Modules.Training.Domain.Tests
dotnet sln add src/Modules/Training/MyGym.Modules.Training.Contracts \
               src/Modules/Training/MyGym.Modules.Training.Domain \
               tests/MyGym.Modules.Training.Domain.Tests
dotnet add src/Modules/Training/MyGym.Modules.Training.Domain reference \
           src/Shared/MyGym.SharedKernel src/Modules/Training/MyGym.Modules.Training.Contracts
dotnet add src/Modules/Training/MyGym.Modules.Training.Contracts reference src/Shared/MyGym.SharedKernel
dotnet add tests/MyGym.Modules.Training.Domain.Tests reference src/Modules/Training/MyGym.Modules.Training.Domain
dotnet add tests/MyGym.Modules.Training.Domain.Tests package Shouldly
rm src/Modules/Training/MyGym.Modules.Training.Domain/Class1.cs \
   src/Modules/Training/MyGym.Modules.Training.Contracts/Class1.cs
```

- [ ] **Step 2: Write the failing tests**

`tests/MyGym.Modules.Training.Domain.Tests/RepRangeTests.cs`:

```csharp
using MyGym.Modules.Training.Domain.ValueObjects;
using Shouldly;

namespace MyGym.Modules.Training.Domain.Tests;

public class RepRangeTests
{
    [Fact]
    public void Valid_range_is_created()
    {
        var range = RepRange.Create(6, 8).Value;

        range.Min.ShouldBe(6);
        range.Max.ShouldBe(8);
    }

    [Fact]
    public void A_single_rep_target_is_a_valid_range()
    {
        RepRange.Create(3, 3).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Inverted_range_is_rejected()
    {
        RepRange.Create(10, 8).Error.Code.ShouldBe("rep_range.inverted");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Minimum_below_one_is_rejected(int min)
    {
        RepRange.Create(min, 8).Error.Code.ShouldBe("rep_range.out_of_bounds");
    }

    [Fact]
    public void Maximum_above_one_hundred_is_rejected()
    {
        RepRange.Create(6, 101).Error.Code.ShouldBe("rep_range.out_of_bounds");
    }
}
```

`tests/MyGym.Modules.Training.Domain.Tests/RestPeriodTests.cs`:

```csharp
using MyGym.Modules.Training.Domain.ValueObjects;
using Shouldly;

namespace MyGym.Modules.Training.Domain.Tests;

public class RestPeriodTests
{
    [Fact]
    public void Valid_rest_window_is_created()
    {
        var rest = RestPeriod.Create(TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(4)).Value;

        rest.Min.ShouldBe(TimeSpan.FromMinutes(3));
        rest.Max.ShouldBe(TimeSpan.FromMinutes(4));
    }

    [Fact]
    public void Inverted_window_is_rejected()
    {
        RestPeriod.Create(TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(3))
            .Error.Code.ShouldBe("rest_period.inverted");
    }

    [Fact]
    public void Non_positive_minimum_is_rejected()
    {
        RestPeriod.Create(TimeSpan.Zero, TimeSpan.FromMinutes(2))
            .Error.Code.ShouldBe("rest_period.out_of_bounds");
    }

    [Fact]
    public void Rest_longer_than_thirty_minutes_is_rejected()
    {
        RestPeriod.Create(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(31))
            .Error.Code.ShouldBe("rest_period.out_of_bounds");
    }
}
```

`tests/MyGym.Modules.Training.Domain.Tests/WarmupRampTests.cs`:

```csharp
using MyGym.Modules.Training.Domain.ValueObjects;
using MyGym.SharedKernel;
using Shouldly;

namespace MyGym.Modules.Training.Domain.Tests;

public class WarmupRampTests
{
    private static WarmupStep Step(decimal percent, int reps, bool optional = false) =>
        WarmupStep.Create(percent, reps, optional).Value;

    [Fact]
    public void An_empty_ramp_is_valid_and_resolves_to_nothing()
    {
        WarmupRamp.Empty.Steps.ShouldBeEmpty();
        WarmupRamp.Empty.Resolve(Weight.Kg(100m), WeightIncrement.DefaultKg).ShouldBeEmpty();
    }

    [Fact]
    public void Percentages_must_be_strictly_ascending()
    {
        var result = WarmupRamp.Create([Step(0.60m, 8), Step(0.50m, 5)]);

        result.Error.Code.ShouldBe("warmup.percent_not_ascending");
    }

    [Fact]
    public void Equal_consecutive_percentages_are_rejected()
    {
        WarmupRamp.Create([Step(0.50m, 8), Step(0.50m, 5)])
            .Error.Code.ShouldBe("warmup.percent_not_ascending");
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.2)]
    public void A_warmup_step_at_or_above_the_working_weight_is_rejected(double percent)
    {
        WarmupStep.Create((decimal)percent, 5, false).Error.Code.ShouldBe("warmup.percent_out_of_bounds");
    }

    [Fact]
    public void A_non_positive_percentage_is_rejected()
    {
        WarmupStep.Create(0m, 5, false).Error.Code.ShouldBe("warmup.percent_out_of_bounds");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public void Warmup_reps_outside_one_to_thirty_are_rejected(int reps)
    {
        WarmupStep.Create(0.5m, reps, false).Error.Code.ShouldBe("warmup.reps_out_of_bounds");
    }

    [Fact]
    public void More_than_five_steps_are_rejected()
    {
        var steps = new[] { Step(0.2m, 10), Step(0.3m, 8), Step(0.4m, 6), Step(0.5m, 5), Step(0.6m, 3), Step(0.7m, 2) };

        WarmupRamp.Create(steps).Error.Code.ShouldBe("warmup.too_many_steps");
    }

    [Fact]
    public void Bench_ramp_resolves_to_loadable_weights()
    {
        // The real ramp from the author's Friday bench press: 27% x 10, 53% x 6, 80% x 3.
        var ramp = WarmupRamp.Create([Step(0.27m, 10), Step(0.53m, 6), Step(0.80m, 3)]).Value;

        var resolved = ramp.Resolve(Weight.Kg(80m), WeightIncrement.DefaultKg);

        resolved.Count.ShouldBe(3);
        resolved[0].Weight.Value.ShouldBe(20.0m);   // 21.6 rounds down to 20.0
        resolved[0].Reps.ShouldBe(10);
        resolved[1].Weight.Value.ShouldBe(40.0m);   // 42.4 rounds down to 40.0, never up
        resolved[2].Weight.Value.ShouldBe(62.5m);   // 64.0 rounds down to 62.5
    }

    [Fact]
    public void Resolution_preserves_step_order_reps_and_optionality()
    {
        var ramp = WarmupRamp.Create([Step(0.40m, 8), Step(0.70m, 3, optional: true)]).Value;

        var resolved = ramp.Resolve(Weight.Kg(100m), WeightIncrement.DefaultKg);

        resolved[0].Reps.ShouldBe(8);
        resolved[0].IsOptional.ShouldBeFalse();
        resolved[1].Reps.ShouldBe(3);
        resolved[1].IsOptional.ShouldBeTrue();
    }

    [Fact]
    public void Resolution_never_returns_a_weight_at_or_above_the_working_weight()
    {
        var ramp = WarmupRamp.Create([Step(0.99m, 1)]).Value;

        var resolved = ramp.Resolve(Weight.Kg(20m), WeightIncrement.DefaultKg);

        resolved[0].Weight.ToKilograms().ShouldBeLessThan(20m);
    }

    [Fact]
    public void A_light_working_weight_can_resolve_to_an_empty_bar()
    {
        var ramp = WarmupRamp.Create([Step(0.30m, 10)]).Value;

        var resolved = ramp.Resolve(Weight.Kg(5m), WeightIncrement.DefaultKg);

        resolved[0].Weight.Value.ShouldBe(0m);
    }
}
```

> `Bench_ramp_resolves_to_loadable_weights` is the acceptance test for the whole feature: a real ramp from a real program, resolved against a real working weight, landing on weights that exist on real plates.

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/MyGym.Modules.Training.Domain.Tests`
Expected: FAIL — `RepRange`, `RestPeriod`, `WarmupRamp` do not exist.

- [ ] **Step 4: Write the minimal implementation**

`src/Modules/Training/MyGym.Modules.Training.Domain/TrainingErrors.cs`:

```csharp
namespace MyGym.Modules.Training.Domain;

public static class TrainingErrors
{
    // Value objects
    public const string RepRangeInverted = "rep_range.inverted";
    public const string RepRangeOutOfBounds = "rep_range.out_of_bounds";
    public const string RestPeriodInverted = "rest_period.inverted";
    public const string RestPeriodOutOfBounds = "rest_period.out_of_bounds";
    public const string WarmupPercentNotAscending = "warmup.percent_not_ascending";
    public const string WarmupPercentOutOfBounds = "warmup.percent_out_of_bounds";
    public const string WarmupRepsOutOfBounds = "warmup.reps_out_of_bounds";
    public const string WarmupTooManySteps = "warmup.too_many_steps";

    // Programs
    public const string ProgramNameRequired = "program.name_required";
    public const string ProgramNotEditable = "program.not_editable";
    public const string ProgramNeedsDay = "program.needs_at_least_one_day";
    public const string ProgramDayNeedsPrescription = "program.day_needs_at_least_one_prescription";
    public const string ProgramAlreadyActive = "program.already_active";
    public const string ProgramArchived = "program.archived";
    public const string ProgramDayNotFound = "program.day_not_found";
    public const string PrescriptionWorkingSetsOutOfBounds = "prescription.working_sets_out_of_bounds";
    public const string PrescriptionAlternativeSameAsPrimary = "prescription.alternative_same_as_primary";

    // Sessions
    public const string SessionAlreadyInProgress = "session.already_in_progress";
    public const string SessionClosed = "session.closed";
    public const string SessionNeedsWorkingSet = "session.needs_at_least_one_working_set";
    public const string SessionExerciseNotFound = "session.exercise_not_found";
    public const string SessionNeedsExercise = "session.needs_at_least_one_exercise";
    public const string SetNotFound = "set.not_found";
    public const string SetRepsOutOfBounds = "set.reps_out_of_bounds";
    public const string SetRirOutOfBounds = "set.rir_out_of_bounds";
}
```

`src/Modules/Training/MyGym.Modules.Training.Domain/ValueObjects/RepRange.cs`:

```csharp
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Domain.ValueObjects;

public sealed class RepRange : ValueObject
{
    public const int AbsoluteMin = 1;
    public const int AbsoluteMax = 100;

    private RepRange(int min, int max)
    {
        Min = min;
        Max = max;
    }

    public int Min { get; }
    public int Max { get; }

    public static Result<RepRange> Create(int min, int max)
    {
        if (min < AbsoluteMin || max > AbsoluteMax)
            return Result<RepRange>.Failure(new Error(
                TrainingErrors.RepRangeOutOfBounds,
                $"Reps must be between {AbsoluteMin} and {AbsoluteMax}."));

        if (min > max)
            return Result<RepRange>.Failure(new Error(
                TrainingErrors.RepRangeInverted, "Minimum reps cannot exceed maximum reps."));

        return Result<RepRange>.Success(new RepRange(min, max));
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Min;
        yield return Max;
    }
}
```

`src/Modules/Training/MyGym.Modules.Training.Domain/ValueObjects/RestPeriod.cs`:

```csharp
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Domain.ValueObjects;

public sealed class RestPeriod : ValueObject
{
    public static readonly TimeSpan AbsoluteMax = TimeSpan.FromMinutes(30);

    private RestPeriod(TimeSpan min, TimeSpan max)
    {
        Min = min;
        Max = max;
    }

    public TimeSpan Min { get; }
    public TimeSpan Max { get; }

    public static Result<RestPeriod> Create(TimeSpan min, TimeSpan max)
    {
        if (min <= TimeSpan.Zero || max > AbsoluteMax)
            return Result<RestPeriod>.Failure(new Error(
                TrainingErrors.RestPeriodOutOfBounds,
                $"Rest must be positive and at most {AbsoluteMax.TotalMinutes} minutes."));

        if (min > max)
            return Result<RestPeriod>.Failure(new Error(
                TrainingErrors.RestPeriodInverted, "Minimum rest cannot exceed maximum rest."));

        return Result<RestPeriod>.Success(new RestPeriod(min, max));
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Min;
        yield return Max;
    }
}
```

`src/Modules/Training/MyGym.Modules.Training.Domain/ValueObjects/WarmupRamp.cs`:

```csharp
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Domain.ValueObjects;

public sealed class WarmupStep : ValueObject
{
    public const int MinReps = 1;
    public const int MaxReps = 30;

    private WarmupStep(decimal percentOfWorking, int reps, bool isOptional)
    {
        PercentOfWorking = percentOfWorking;
        Reps = reps;
        IsOptional = isOptional;
    }

    /// <summary>Fraction of the working weight, exclusive of 0 and 1 — 0.53 means 53%.</summary>
    public decimal PercentOfWorking { get; }
    public int Reps { get; }
    public bool IsOptional { get; }

    public static Result<WarmupStep> Create(decimal percentOfWorking, int reps, bool isOptional)
    {
        if (percentOfWorking <= 0m || percentOfWorking >= 1m)
            return Result<WarmupStep>.Failure(new Error(
                TrainingErrors.WarmupPercentOutOfBounds,
                "Warm-up percentage must be above 0% and below 100% of the working weight."));

        if (reps is < MinReps or > MaxReps)
            return Result<WarmupStep>.Failure(new Error(
                TrainingErrors.WarmupRepsOutOfBounds,
                $"Warm-up reps must be between {MinReps} and {MaxReps}."));

        return Result<WarmupStep>.Success(new WarmupStep(percentOfWorking, reps, isOptional));
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return PercentOfWorking;
        yield return Reps;
        yield return IsOptional;
    }
}

public sealed record ResolvedWarmupStep(Weight Weight, int Reps, bool IsOptional);

public sealed class WarmupRamp : ValueObject
{
    public const int MaxSteps = 5;

    private readonly List<WarmupStep> _steps;

    private WarmupRamp(List<WarmupStep> steps) => _steps = steps;

    public IReadOnlyList<WarmupStep> Steps => _steps;

    public static WarmupRamp Empty { get; } = new([]);

    public static Result<WarmupRamp> Create(IEnumerable<WarmupStep> steps)
    {
        var ordered = steps.ToList();

        if (ordered.Count > MaxSteps)
            return Result<WarmupRamp>.Failure(new Error(
                TrainingErrors.WarmupTooManySteps, $"A ramp may have at most {MaxSteps} steps."));

        for (var i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].PercentOfWorking <= ordered[i - 1].PercentOfWorking)
                return Result<WarmupRamp>.Failure(new Error(
                    TrainingErrors.WarmupPercentNotAscending,
                    "Warm-up percentages must increase strictly from one step to the next."));
        }

        return Result<WarmupRamp>.Success(new WarmupRamp(ordered));
    }

    /// <summary>
    /// Turns percentages into weights that can physically be loaded. Rounds <em>down</em> to the
    /// nearest increment, so a resolved warm-up is never heavier than intended.
    /// </summary>
    public IReadOnlyList<ResolvedWarmupStep> Resolve(Weight working, WeightIncrement rounding) =>
        _steps
            .Select(step => new ResolvedWarmupStep(
                working.Scale(step.PercentOfWorking).RoundDownTo(rounding),
                step.Reps,
                step.IsOptional))
            .ToList();

    protected override IEnumerable<object?> GetEqualityComponents() => _steps;
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/MyGym.Modules.Training.Domain.Tests`
Expected: PASS — 22 tests.

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Training tests/MyGym.Modules.Training.Domain.Tests
git commit -m "feat: add training value objects and warm-up ramp resolution"
```

---

### Task 4: TrainingProgram aggregate

**Files:**
- Create: `src/Modules/Training/MyGym.Modules.Training.Contracts/Events.cs`
- Create: `src/Modules/Training/MyGym.Modules.Training.Domain/Programs/{TrainingProgram,ProgramDay,ExercisePrescription,ITrainingProgramRepository}.cs`
- Test: `tests/MyGym.Modules.Training.Domain.Tests/TrainingProgramTests.cs`

**Interfaces:**
- Consumes: `AggregateRoot<TId>`, `Entity<TId>`, `Result`, typed ids from Tasks 1–2; `RepRange`, `RestPeriod`, `WarmupRamp`, `TrainingErrors` from Task 3.
- Produces:
  - `ProgramStatus` (`Draft`, `Active`, `Archived`), `ProgramVisibility` (`Private`, `Public`)
  - `ProgramActivated(ProgramId ProgramId, UserId OwnerUserId, int RevisionNumber) : IDomainEvent`
  - `TrainingProgram` with `OwnerUserId`, `Name`, `Status`, `Visibility`, `ForkedFromProgramId`, `RevisionNumber`, `Days`, and methods `Create(UserId, string) → Result<TrainingProgram>`, `Rename(string) → Result`, `AddDay(string, DayOfWeek?) → Result<ProgramDay>`, `RemoveDay(ProgramDayId) → Result`, `AddPrescription(ProgramDayId, ExerciseId, ExerciseId?, int, RepRange, RestPeriod, WarmupRamp, string?) → Result<ExercisePrescription>`, `Activate() → Result`, `Archive() → Result`, `SetVisibility(ProgramVisibility) → Result`, `Fork(UserId) → TrainingProgram`
  - `ProgramDay` with `ProgramId`, `Order`, `Label`, `DayOfWeek`, `Prescriptions`
  - `ExercisePrescription` with `ProgramDayId`, `Order`, `ExerciseId`, `AlternativeExerciseId`, `WorkingSetCount`, `TargetReps`, `Rest`, `Warmup`, `Notes`
  - `ITrainingProgramRepository` with `GetByIdAsync(ProgramId, CancellationToken) → Task<TrainingProgram?>`, `AddAsync(TrainingProgram, CancellationToken)`, `Remove(TrainingProgram)`

- [ ] **Step 1: Write the failing tests**

`tests/MyGym.Modules.Training.Domain.Tests/TrainingProgramTests.cs`:

```csharp
using MyGym.Modules.Training.Contracts;
using MyGym.Modules.Training.Domain;
using MyGym.Modules.Training.Domain.Programs;
using MyGym.Modules.Training.Domain.ValueObjects;
using MyGym.SharedKernel;
using Shouldly;

namespace MyGym.Modules.Training.Domain.Tests;

public class TrainingProgramTests
{
    private static readonly UserId Owner = UserId.New();

    private static TrainingProgram NewProgram(string name = "3 Kunlik Muscle Gain") =>
        TrainingProgram.Create(Owner, name).Value;

    private static WarmupRamp Ramp(params (decimal Percent, int Reps)[] steps) =>
        WarmupRamp.Create(steps.Select(s => WarmupStep.Create(s.Percent, s.Reps, false).Value)).Value;

    private static Result<ExercisePrescription> AddBench(TrainingProgram program, ProgramDayId dayId) =>
        program.AddPrescription(
            dayId,
            ExerciseId.New(),
            alternativeExerciseId: null,
            workingSetCount: 3,
            targetReps: RepRange.Create(6, 8).Value,
            rest: RestPeriod.Create(TimeSpan.FromMinutes(2.5), TimeSpan.FromMinutes(3)).Value,
            warmup: Ramp((0.27m, 10), (0.53m, 6), (0.80m, 3)),
            notes: null);

    [Fact]
    public void A_new_program_is_a_private_draft_at_revision_one()
    {
        var program = NewProgram();

        program.OwnerUserId.ShouldBe(Owner);
        program.Status.ShouldBe(ProgramStatus.Draft);
        program.Visibility.ShouldBe(ProgramVisibility.Private);
        program.RevisionNumber.ShouldBe(1);
        program.Days.ShouldBeEmpty();
        program.ForkedFromProgramId.ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_name_is_rejected(string name)
    {
        TrainingProgram.Create(Owner, name).Error.Code.ShouldBe(TrainingErrors.ProgramNameRequired);
    }

    [Fact]
    public void Days_are_numbered_contiguously_from_one()
    {
        var program = NewProgram();

        program.AddDay("Dushanba — Upper Body", DayOfWeek.Monday);
        program.AddDay("Chorshanba — Legs + Core", DayOfWeek.Wednesday);
        program.AddDay("Juma — Full Body", DayOfWeek.Friday);

        program.Days.Select(d => d.Order).ShouldBe([1, 2, 3]);
        program.Days.Select(d => d.Label).ShouldBe(
            ["Dushanba — Upper Body", "Chorshanba — Legs + Core", "Juma — Full Body"]);
    }

    [Fact]
    public void Removing_a_day_renumbers_the_remaining_days_contiguously()
    {
        var program = NewProgram();
        program.AddDay("A", null);
        var middle = program.AddDay("B", null).Value;
        program.AddDay("C", null);

        program.RemoveDay(middle.Id).IsSuccess.ShouldBeTrue();

        program.Days.Select(d => d.Order).ShouldBe([1, 2]);
        program.Days.Select(d => d.Label).ShouldBe(["A", "C"]);
    }

    [Fact]
    public void Removing_an_unknown_day_fails()
    {
        NewProgram().RemoveDay(ProgramDayId.New()).Error.Code.ShouldBe(TrainingErrors.ProgramDayNotFound);
    }

    [Fact]
    public void Prescriptions_are_numbered_contiguously_within_their_day()
    {
        var program = NewProgram();
        var day = program.AddDay("Upper", null).Value;

        AddBench(program, day.Id);
        AddBench(program, day.Id);

        day.Prescriptions.Select(p => p.Order).ShouldBe([1, 2]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void Working_set_counts_outside_one_to_ten_are_rejected(int count)
    {
        var program = NewProgram();
        var day = program.AddDay("Upper", null).Value;

        var result = program.AddPrescription(
            day.Id, ExerciseId.New(), null, count,
            RepRange.Create(6, 8).Value,
            RestPeriod.Create(TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(3)).Value,
            WarmupRamp.Empty, null);

        result.Error.Code.ShouldBe(TrainingErrors.PrescriptionWorkingSetsOutOfBounds);
    }

    [Fact]
    public void An_alternative_identical_to_the_primary_exercise_is_rejected()
    {
        var program = NewProgram();
        var day = program.AddDay("Legs", null).Value;
        var legPress = ExerciseId.New();

        var result = program.AddPrescription(
            day.Id, legPress, legPress, 3,
            RepRange.Create(10, 12).Value,
            RestPeriod.Create(TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(3)).Value,
            WarmupRamp.Empty, null);

        result.Error.Code.ShouldBe(TrainingErrors.PrescriptionAlternativeSameAsPrimary);
    }

    [Fact]
    public void An_alternative_exercise_is_stored_for_either_or_prescriptions()
    {
        var program = NewProgram();
        var day = program.AddDay("Legs", null).Value;
        var legPress = ExerciseId.New();
        var lunges = ExerciseId.New();

        var prescription = program.AddPrescription(
            day.Id, legPress, lunges, 3,
            RepRange.Create(10, 12).Value,
            RestPeriod.Create(TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(3)).Value,
            WarmupRamp.Empty, null).Value;

        prescription.ExerciseId.ShouldBe(legPress);
        prescription.AlternativeExerciseId.ShouldBe(lunges);
    }

    [Fact]
    public void Every_structural_edit_bumps_the_revision_number()
    {
        var program = NewProgram();

        var day = program.AddDay("Upper", null).Value;
        program.RevisionNumber.ShouldBe(2);

        AddBench(program, day.Id);
        program.RevisionNumber.ShouldBe(3);

        program.Rename("New name");
        program.RevisionNumber.ShouldBe(4);
    }

    [Fact]
    public void A_program_without_days_cannot_be_activated()
    {
        NewProgram().Activate().Error.Code.ShouldBe(TrainingErrors.ProgramNeedsDay);
    }

    [Fact]
    public void A_day_without_prescriptions_blocks_activation()
    {
        var program = NewProgram();
        program.AddDay("Empty day", null);

        program.Activate().Error.Code.ShouldBe(TrainingErrors.ProgramDayNeedsPrescription);
    }

    [Fact]
    public void Activating_a_complete_program_succeeds_and_raises_an_event()
    {
        var program = NewProgram();
        var day = program.AddDay("Upper", null).Value;
        AddBench(program, day.Id);

        program.Activate().IsSuccess.ShouldBeTrue();

        program.Status.ShouldBe(ProgramStatus.Active);
        var raised = program.DomainEvents.OfType<ProgramActivated>().ShouldHaveSingleItem();
        raised.ProgramId.ShouldBe(program.Id);
        raised.OwnerUserId.ShouldBe(Owner);
        raised.RevisionNumber.ShouldBe(program.RevisionNumber);
    }

    [Fact]
    public void Activating_an_already_active_program_fails()
    {
        var program = ActiveProgram();

        program.Activate().Error.Code.ShouldBe(TrainingErrors.ProgramAlreadyActive);
    }

    [Fact]
    public void An_active_program_is_still_editable()
    {
        var program = ActiveProgram();

        program.AddDay("Second day", null).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void An_archived_program_cannot_be_edited_or_activated()
    {
        var program = ActiveProgram();
        program.Archive().IsSuccess.ShouldBeTrue();

        program.Status.ShouldBe(ProgramStatus.Archived);
        program.AddDay("Nope", null).Error.Code.ShouldBe(TrainingErrors.ProgramNotEditable);
        program.Rename("Nope").Error.Code.ShouldBe(TrainingErrors.ProgramNotEditable);
        program.Activate().Error.Code.ShouldBe(TrainingErrors.ProgramArchived);
    }

    [Fact]
    public void Forking_copies_the_structure_into_a_fresh_private_draft_owned_by_the_forker()
    {
        var source = ActiveProgram();
        source.SetVisibility(ProgramVisibility.Public);
        var forker = UserId.New();

        var fork = source.Fork(forker);

        fork.Id.ShouldNotBe(source.Id);
        fork.OwnerUserId.ShouldBe(forker);
        fork.Status.ShouldBe(ProgramStatus.Draft);
        fork.Visibility.ShouldBe(ProgramVisibility.Private);
        fork.ForkedFromProgramId.ShouldBe(source.Id);
        fork.RevisionNumber.ShouldBe(1);
        fork.Days.Count.ShouldBe(source.Days.Count);
        fork.Days[0].Prescriptions.Count.ShouldBe(source.Days[0].Prescriptions.Count);
        fork.Days[0].Prescriptions[0].TargetReps.ShouldBe(source.Days[0].Prescriptions[0].TargetReps);
    }

    [Fact]
    public void Forked_children_get_new_identities()
    {
        var source = ActiveProgram();

        var fork = source.Fork(UserId.New());

        fork.Days[0].Id.ShouldNotBe(source.Days[0].Id);
        fork.Days[0].Prescriptions[0].Id.ShouldNotBe(source.Days[0].Prescriptions[0].Id);
        fork.Days[0].ProgramId.ShouldBe(fork.Id);
    }

    private static TrainingProgram ActiveProgram()
    {
        var program = NewProgram();
        var day = program.AddDay("Dushanba — Upper Body", DayOfWeek.Monday).Value;
        AddBench(program, day.Id);
        program.Activate();
        program.ClearDomainEvents();
        return program;
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/MyGym.Modules.Training.Domain.Tests --filter TrainingProgramTests`
Expected: FAIL — `TrainingProgram` does not exist.

- [ ] **Step 3: Write the contracts events**

`src/Modules/Training/MyGym.Modules.Training.Contracts/Events.cs`:

```csharp
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Contracts;

public sealed record ProgramActivated(ProgramId ProgramId, UserId OwnerUserId, int RevisionNumber)
    : IDomainEvent;

public sealed record WorkoutSessionStarted(SessionId SessionId, UserId UserId, DateTimeOffset StartedAt)
    : IDomainEvent;

public sealed record WorkoutSessionCompleted(
    SessionId SessionId,
    UserId UserId,
    DateTimeOffset CompletedAt,
    int WorkingSetCount) : IDomainEvent;
```

- [ ] **Step 4: Write the aggregate**

`src/Modules/Training/MyGym.Modules.Training.Domain/Programs/ExercisePrescription.cs`:

```csharp
using MyGym.Modules.Training.Domain.ValueObjects;
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Domain.Programs;

public sealed class ExercisePrescription : Entity<PrescriptionId>
{
    public const int MinWorkingSets = 1;
    public const int MaxWorkingSets = 10;

    private ExercisePrescription(
        PrescriptionId id,
        ProgramDayId programDayId,
        int order,
        ExerciseId exerciseId,
        ExerciseId? alternativeExerciseId,
        int workingSetCount,
        RepRange targetReps,
        RestPeriod rest,
        WarmupRamp warmup,
        string? notes) : base(id)
    {
        ProgramDayId = programDayId;
        Order = order;
        ExerciseId = exerciseId;
        AlternativeExerciseId = alternativeExerciseId;
        WorkingSetCount = workingSetCount;
        TargetReps = targetReps;
        Rest = rest;
        Warmup = warmup;
        Notes = notes;
    }

    public ProgramDayId ProgramDayId { get; private set; }
    public int Order { get; private set; }
    public ExerciseId ExerciseId { get; private set; }
    public ExerciseId? AlternativeExerciseId { get; private set; }
    public int WorkingSetCount { get; private set; }
    public RepRange TargetReps { get; private set; }
    public RestPeriod Rest { get; private set; }
    public WarmupRamp Warmup { get; private set; }
    public string? Notes { get; private set; }

    internal static Result<ExercisePrescription> Create(
        ProgramDayId programDayId,
        int order,
        ExerciseId exerciseId,
        ExerciseId? alternativeExerciseId,
        int workingSetCount,
        RepRange targetReps,
        RestPeriod rest,
        WarmupRamp warmup,
        string? notes)
    {
        if (workingSetCount is < MinWorkingSets or > MaxWorkingSets)
            return Result<ExercisePrescription>.Failure(new Error(
                TrainingErrors.PrescriptionWorkingSetsOutOfBounds,
                $"Working sets must be between {MinWorkingSets} and {MaxWorkingSets}."));

        if (alternativeExerciseId is not null && alternativeExerciseId == exerciseId)
            return Result<ExercisePrescription>.Failure(new Error(
                TrainingErrors.PrescriptionAlternativeSameAsPrimary,
                "The alternative exercise must differ from the primary exercise."));

        return Result<ExercisePrescription>.Success(new ExercisePrescription(
            PrescriptionId.New(), programDayId, order, exerciseId, alternativeExerciseId,
            workingSetCount, targetReps, rest, warmup, notes));
    }

    internal ExercisePrescription CopyTo(ProgramDayId newDayId) => new(
        PrescriptionId.New(), newDayId, Order, ExerciseId, AlternativeExerciseId,
        WorkingSetCount, TargetReps, Rest, Warmup, Notes);
}
```

`src/Modules/Training/MyGym.Modules.Training.Domain/Programs/ProgramDay.cs`:

```csharp
using MyGym.Modules.Training.Domain.ValueObjects;
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Domain.Programs;

public sealed class ProgramDay : Entity<ProgramDayId>
{
    private readonly List<ExercisePrescription> _prescriptions = [];

    private ProgramDay(ProgramDayId id, ProgramId programId, int order, string label, DayOfWeek? dayOfWeek)
        : base(id)
    {
        ProgramId = programId;
        Order = order;
        Label = label;
        DayOfWeek = dayOfWeek;
    }

    public ProgramId ProgramId { get; private set; }
    public int Order { get; private set; }
    public string Label { get; private set; }
    public DayOfWeek? DayOfWeek { get; private set; }

    public IReadOnlyList<ExercisePrescription> Prescriptions =>
        _prescriptions.OrderBy(p => p.Order).ToList();

    internal static ProgramDay Create(ProgramId programId, int order, string label, DayOfWeek? dayOfWeek) =>
        new(ProgramDayId.New(), programId, order, label.Trim(), dayOfWeek);

    internal void SetOrder(int order) => Order = order;

    internal Result<ExercisePrescription> AddPrescription(
        ExerciseId exerciseId,
        ExerciseId? alternativeExerciseId,
        int workingSetCount,
        RepRange targetReps,
        RestPeriod rest,
        WarmupRamp warmup,
        string? notes)
    {
        var result = ExercisePrescription.Create(
            Id, _prescriptions.Count + 1, exerciseId, alternativeExerciseId,
            workingSetCount, targetReps, rest, warmup, notes);

        if (result.IsFailure)
            return result;

        _prescriptions.Add(result.Value);
        return result;
    }

    internal ProgramDay CopyTo(ProgramId newProgramId)
    {
        var copy = new ProgramDay(ProgramDayId.New(), newProgramId, Order, Label, DayOfWeek);
        copy._prescriptions.AddRange(Prescriptions.Select(p => p.CopyTo(copy.Id)));
        return copy;
    }
}
```

`src/Modules/Training/MyGym.Modules.Training.Domain/Programs/TrainingProgram.cs`:

```csharp
using MyGym.Modules.Training.Contracts;
using MyGym.Modules.Training.Domain.ValueObjects;
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Domain.Programs;

public enum ProgramStatus
{
    Draft = 1,
    Active = 2,
    Archived = 3
}

public enum ProgramVisibility
{
    Private = 1,
    Public = 2
}

public sealed class TrainingProgram : AggregateRoot<ProgramId>
{
    public const int MaxNameLength = 120;

    private readonly List<ProgramDay> _days = [];

    private TrainingProgram(ProgramId id, UserId ownerUserId, string name, ProgramId? forkedFrom)
        : base(id)
    {
        OwnerUserId = ownerUserId;
        Name = name;
        Status = ProgramStatus.Draft;
        Visibility = ProgramVisibility.Private;
        ForkedFromProgramId = forkedFrom;
        RevisionNumber = 1;
    }

    public UserId OwnerUserId { get; private set; }
    public string Name { get; private set; }
    public ProgramStatus Status { get; private set; }
    public ProgramVisibility Visibility { get; private set; }
    public ProgramId? ForkedFromProgramId { get; private set; }
    public int RevisionNumber { get; private set; }

    public IReadOnlyList<ProgramDay> Days => _days.OrderBy(d => d.Order).ToList();

    public static Result<TrainingProgram> Create(UserId ownerUserId, string name)
    {
        var validated = ValidateName(name);
        return validated.IsFailure
            ? Result<TrainingProgram>.Failure(validated.Error)
            : Result<TrainingProgram>.Success(
                new TrainingProgram(ProgramId.New(), ownerUserId, validated.Value, forkedFrom: null));
    }

    public Result Rename(string name)
    {
        var editable = EnsureEditable();
        if (editable.IsFailure)
            return editable;

        var validated = ValidateName(name);
        if (validated.IsFailure)
            return Result.Failure(validated.Error);

        Name = validated.Value;
        RevisionNumber++;
        return Result.Success();
    }

    public Result<ProgramDay> AddDay(string label, DayOfWeek? dayOfWeek)
    {
        var editable = EnsureEditable();
        if (editable.IsFailure)
            return Result<ProgramDay>.Failure(editable.Error);

        if (string.IsNullOrWhiteSpace(label))
            return Result<ProgramDay>.Failure(
                new Error(TrainingErrors.ProgramNameRequired, "A day needs a label."));

        var day = ProgramDay.Create(Id, _days.Count + 1, label, dayOfWeek);
        _days.Add(day);
        RevisionNumber++;
        return Result<ProgramDay>.Success(day);
    }

    public Result RemoveDay(ProgramDayId dayId)
    {
        var editable = EnsureEditable();
        if (editable.IsFailure)
            return editable;

        var day = _days.SingleOrDefault(d => d.Id == dayId);
        if (day is null)
            return Result.Failure(new Error(TrainingErrors.ProgramDayNotFound, "Day not found."));

        _days.Remove(day);
        Renumber();
        RevisionNumber++;
        return Result.Success();
    }

    public Result<ExercisePrescription> AddPrescription(
        ProgramDayId dayId,
        ExerciseId exerciseId,
        ExerciseId? alternativeExerciseId,
        int workingSetCount,
        RepRange targetReps,
        RestPeriod rest,
        WarmupRamp warmup,
        string? notes)
    {
        var editable = EnsureEditable();
        if (editable.IsFailure)
            return Result<ExercisePrescription>.Failure(editable.Error);

        var day = _days.SingleOrDefault(d => d.Id == dayId);
        if (day is null)
            return Result<ExercisePrescription>.Failure(
                new Error(TrainingErrors.ProgramDayNotFound, "Day not found."));

        var result = day.AddPrescription(
            exerciseId, alternativeExerciseId, workingSetCount, targetReps, rest, warmup, notes);

        if (result.IsSuccess)
            RevisionNumber++;

        return result;
    }

    public Result Activate()
    {
        if (Status == ProgramStatus.Archived)
            return Result.Failure(new Error(TrainingErrors.ProgramArchived, "Program is archived."));

        if (Status == ProgramStatus.Active)
            return Result.Failure(new Error(TrainingErrors.ProgramAlreadyActive, "Program is already active."));

        if (_days.Count == 0)
            return Result.Failure(new Error(TrainingErrors.ProgramNeedsDay, "A program needs at least one day."));

        if (_days.Any(d => d.Prescriptions.Count == 0))
            return Result.Failure(new Error(
                TrainingErrors.ProgramDayNeedsPrescription, "Every day needs at least one exercise."));

        Status = ProgramStatus.Active;
        Raise(new ProgramActivated(Id, OwnerUserId, RevisionNumber));
        return Result.Success();
    }

    public Result Archive()
    {
        if (Status == ProgramStatus.Archived)
            return Result.Failure(new Error(TrainingErrors.ProgramArchived, "Program is already archived."));

        Status = ProgramStatus.Archived;
        return Result.Success();
    }

    public Result SetVisibility(ProgramVisibility visibility)
    {
        var editable = EnsureEditable();
        if (editable.IsFailure)
            return editable;

        Visibility = visibility;
        return Result.Success();
    }

    public TrainingProgram Fork(UserId newOwnerUserId)
    {
        var fork = new TrainingProgram(ProgramId.New(), newOwnerUserId, Name, forkedFrom: Id);
        fork._days.AddRange(Days.Select(d => d.CopyTo(fork.Id)));
        return fork;
    }

    private Result EnsureEditable() =>
        Status == ProgramStatus.Archived
            ? Result.Failure(new Error(TrainingErrors.ProgramNotEditable, "An archived program cannot be edited."))
            : Result.Success();

    private void Renumber()
    {
        var order = 1;
        foreach (var day in _days.OrderBy(d => d.Order).ToList())
            day.SetOrder(order++);
    }

    private static Result<string> ValidateName(string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        return trimmed.Length is 0 or > MaxNameLength
            ? Result<string>.Failure(new Error(
                TrainingErrors.ProgramNameRequired,
                $"A program name is required and must be at most {MaxNameLength} characters."))
            : Result<string>.Success(trimmed);
    }
}
```

`src/Modules/Training/MyGym.Modules.Training.Domain/Programs/ITrainingProgramRepository.cs`:

```csharp
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Domain.Programs;

public interface ITrainingProgramRepository
{
    Task<TrainingProgram?> GetByIdAsync(ProgramId id, CancellationToken ct);
    Task AddAsync(TrainingProgram program, CancellationToken ct);
    void Remove(TrainingProgram program);
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/MyGym.Modules.Training.Domain.Tests`
Expected: PASS — all `TrainingProgramTests` plus the 22 value-object tests from Task 3.

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Training tests/MyGym.Modules.Training.Domain.Tests
git commit -m "feat: add TrainingProgram aggregate with day and prescription invariants"
```

---

### Task 5: WorkoutSession aggregate with idempotent set logging

**Files:**
- Create: `src/Modules/Training/MyGym.Modules.Training.Domain/ValueObjects/{SessionOrigin,PrescriptionSnapshot}.cs`
- Create: `src/Modules/Training/MyGym.Modules.Training.Domain/Sessions/{WorkoutSession,SessionExercise,LoggedSet,IWorkoutSessionRepository}.cs`
- Test: `tests/MyGym.Modules.Training.Domain.Tests/WorkoutSessionTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 1–4.
- Produces:
  - `SessionStatus` (`InProgress`, `Completed`, `Abandoned`), `SetKind` (`Warmup`, `Working`)
  - `SessionOrigin` record with `ProgramId`, `ProgramRevision`, `ProgramDayId`, `IsAdHoc`, and factories `SessionOrigin.AdHoc`, `SessionOrigin.FromProgram(ProgramId, int, ProgramDayId)`
  - `PrescriptionSnapshot` value object with `ExerciseId`, `AlternativeExerciseId`, `WorkingSetCount`, `TargetReps`, `Rest`, `Warmup`, `Notes` and `Create(...) → Result<PrescriptionSnapshot>`
  - `WorkoutSession` with `UserId`, `Status`, `Origin`, `StartedAt`, `CompletedAt`, `BodyweightAtSession`, `Exercises`, `WorkingSetCount`, and methods `Start(UserId, SessionOrigin, IReadOnlyList<PrescriptionSnapshot>, DateTimeOffset) → Result<WorkoutSession>`, `AddExercise(PrescriptionSnapshot) → Result<SessionExercise>`, `LogSet(SessionExerciseId, Guid, SetKind, Weight, int, int?, DateTimeOffset) → Result<LoggedSet>`, `EditSet(SessionExerciseId, LoggedSetId, Weight, int, int?) → Result`, `DeleteSet(SessionExerciseId, LoggedSetId) → Result`, `RecordBodyweight(Weight) → Result`, `Complete(DateTimeOffset) → Result`, `Abandon(DateTimeOffset) → Result`
  - `SessionExercise` with `SessionId`, `Order`, `ExerciseId`, `Prescription`, `Sets`
  - `LoggedSet` with `SessionExerciseId`, `Order`, `Kind`, `Weight`, `Reps`, `Rir`, `ClientSetId`, `LoggedAt`
  - `IWorkoutSessionRepository` with `GetByIdAsync(SessionId, CancellationToken)`, `GetInProgressForUserAsync(UserId, CancellationToken)`, `AddAsync(WorkoutSession, CancellationToken)`

> **Not enforced here:** "at most one `InProgress` session per user" spans aggregates, so it cannot live inside `WorkoutSession`. It is enforced in the application handler (Task 11) and by a partial unique index in the database (Task 7). The aggregate's job is everything within one session.

- [ ] **Step 1: Write the failing tests**

`tests/MyGym.Modules.Training.Domain.Tests/WorkoutSessionTests.cs`:

```csharp
using MyGym.Modules.Training.Contracts;
using MyGym.Modules.Training.Domain;
using MyGym.Modules.Training.Domain.Sessions;
using MyGym.Modules.Training.Domain.ValueObjects;
using MyGym.SharedKernel;
using Shouldly;

namespace MyGym.Modules.Training.Domain.Tests;

public class WorkoutSessionTests
{
    private static readonly UserId Athlete = UserId.New();
    private static readonly DateTimeOffset Start = new(2026, 11, 23, 18, 0, 0, TimeSpan.Zero);

    private static PrescriptionSnapshot Snapshot(ExerciseId? exerciseId = null) =>
        PrescriptionSnapshot.Create(
            exerciseId ?? ExerciseId.New(),
            alternativeExerciseId: null,
            workingSetCount: 3,
            targetReps: RepRange.Create(6, 8).Value,
            rest: RestPeriod.Create(TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(3)).Value,
            warmup: WarmupRamp.Empty,
            notes: null).Value;

    private static WorkoutSession ProgramSession(params PrescriptionSnapshot[] snapshots)
    {
        var origin = SessionOrigin.FromProgram(ProgramId.New(), 7, ProgramDayId.New());
        var list = snapshots.Length == 0 ? [Snapshot()] : snapshots.ToList();
        var session = WorkoutSession.Start(Athlete, origin, list, Start).Value;
        session.ClearDomainEvents();
        return session;
    }

    [Fact]
    public void Starting_from_a_program_day_records_the_origin_and_snapshots()
    {
        var programId = ProgramId.New();
        var dayId = ProgramDayId.New();
        var snapshot = Snapshot();

        var session = WorkoutSession.Start(
            Athlete, SessionOrigin.FromProgram(programId, 7, dayId), [snapshot], Start).Value;

        session.UserId.ShouldBe(Athlete);
        session.Status.ShouldBe(SessionStatus.InProgress);
        session.StartedAt.ShouldBe(Start);
        session.CompletedAt.ShouldBeNull();
        session.Origin.ProgramId.ShouldBe(programId);
        session.Origin.ProgramRevision.ShouldBe(7);
        session.Origin.ProgramDayId.ShouldBe(dayId);
        session.Origin.IsAdHoc.ShouldBeFalse();
        session.Exercises.ShouldHaveSingleItem().Prescription.ShouldBe(snapshot);
    }

    [Fact]
    public void Starting_a_session_raises_the_started_event()
    {
        var session = WorkoutSession.Start(
            Athlete, SessionOrigin.FromProgram(ProgramId.New(), 1, ProgramDayId.New()),
            [Snapshot()], Start).Value;

        var raised = session.DomainEvents.OfType<WorkoutSessionStarted>().ShouldHaveSingleItem();
        raised.SessionId.ShouldBe(session.Id);
        raised.UserId.ShouldBe(Athlete);
        raised.StartedAt.ShouldBe(Start);
    }

    [Fact]
    public void A_program_session_must_start_with_at_least_one_exercise()
    {
        var result = WorkoutSession.Start(
            Athlete, SessionOrigin.FromProgram(ProgramId.New(), 1, ProgramDayId.New()), [], Start);

        result.Error.Code.ShouldBe(TrainingErrors.SessionNeedsExercise);
    }

    [Fact]
    public void An_ad_hoc_session_may_start_empty_and_gain_exercises_later()
    {
        var session = WorkoutSession.Start(Athlete, SessionOrigin.AdHoc, [], Start).Value;

        session.Origin.IsAdHoc.ShouldBeTrue();
        session.Exercises.ShouldBeEmpty();

        session.AddExercise(Snapshot()).IsSuccess.ShouldBeTrue();
        session.Exercises.ShouldHaveSingleItem().Order.ShouldBe(1);
    }

    [Fact]
    public void Session_exercises_are_numbered_contiguously_from_one()
    {
        var session = ProgramSession(Snapshot(), Snapshot(), Snapshot());

        session.Exercises.Select(e => e.Order).ShouldBe([1, 2, 3]);
    }

    [Fact]
    public void A_logged_set_records_its_load_and_position()
    {
        var session = ProgramSession();
        var exercise = session.Exercises[0];

        var set = session.LogSet(
            exercise.Id, Guid.NewGuid(), SetKind.Working,
            Weight.Kg(27.5m), reps: 8, rir: 2, loggedAt: Start.AddMinutes(10)).Value;

        set.Kind.ShouldBe(SetKind.Working);
        set.Weight.Value.ShouldBe(27.5m);
        set.Reps.ShouldBe(8);
        set.Rir.ShouldBe(2);
        set.Order.ShouldBe(1);
        session.Exercises[0].Sets.ShouldHaveSingleItem();
    }

    [Fact]
    public void Sets_are_numbered_contiguously_within_their_exercise()
    {
        var session = ProgramSession();
        var exercise = session.Exercises[0];

        session.LogSet(exercise.Id, Guid.NewGuid(), SetKind.Warmup, Weight.Kg(20m), 10, null, Start);
        session.LogSet(exercise.Id, Guid.NewGuid(), SetKind.Working, Weight.Kg(27.5m), 8, null, Start);

        session.Exercises[0].Sets.Select(s => s.Order).ShouldBe([1, 2]);
    }

    [Fact]
    public void Logging_the_same_client_set_id_twice_returns_the_original_set_and_adds_nothing()
    {
        var session = ProgramSession();
        var exercise = session.Exercises[0];
        var clientSetId = Guid.NewGuid();

        var first = session.LogSet(
            exercise.Id, clientSetId, SetKind.Working, Weight.Kg(27.5m), 8, null, Start).Value;

        var retry = session.LogSet(
            exercise.Id, clientSetId, SetKind.Working, Weight.Kg(27.5m), 8, null, Start.AddSeconds(30));

        retry.IsSuccess.ShouldBeTrue();
        retry.Value.Id.ShouldBe(first.Id);
        session.Exercises[0].Sets.Count.ShouldBe(1);
    }

    [Fact]
    public void Logging_to_an_unknown_exercise_fails()
    {
        var session = ProgramSession();

        session.LogSet(SessionExerciseId.New(), Guid.NewGuid(), SetKind.Working, Weight.Kg(50m), 5, null, Start)
            .Error.Code.ShouldBe(TrainingErrors.SessionExerciseNotFound);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Reps_outside_zero_to_one_hundred_are_rejected(int reps)
    {
        var session = ProgramSession();

        session.LogSet(session.Exercises[0].Id, Guid.NewGuid(), SetKind.Working, Weight.Kg(50m), reps, null, Start)
            .Error.Code.ShouldBe(TrainingErrors.SetRepsOutOfBounds);
    }

    [Fact]
    public void A_failed_set_at_zero_reps_is_allowed()
    {
        var session = ProgramSession();

        session.LogSet(session.Exercises[0].Id, Guid.NewGuid(), SetKind.Working, Weight.Kg(50m), 0, null, Start)
            .IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(11)]
    public void Reps_in_reserve_outside_zero_to_ten_are_rejected(int rir)
    {
        var session = ProgramSession();

        session.LogSet(session.Exercises[0].Id, Guid.NewGuid(), SetKind.Working, Weight.Kg(50m), 8, rir, Start)
            .Error.Code.ShouldBe(TrainingErrors.SetRirOutOfBounds);
    }

    [Fact]
    public void Editing_a_set_updates_its_load_and_reps()
    {
        var session = ProgramSession();
        var exercise = session.Exercises[0];
        var set = session.LogSet(exercise.Id, Guid.NewGuid(), SetKind.Working, Weight.Kg(27.5m), 8, null, Start).Value;

        session.EditSet(exercise.Id, set.Id, Weight.Kg(30m), 6, 1).IsSuccess.ShouldBeTrue();

        var updated = session.Exercises[0].Sets[0];
        updated.Weight.Value.ShouldBe(30m);
        updated.Reps.ShouldBe(6);
        updated.Rir.ShouldBe(1);
    }

    [Fact]
    public void Deleting_a_set_renumbers_the_remaining_sets()
    {
        var session = ProgramSession();
        var exercise = session.Exercises[0];
        session.LogSet(exercise.Id, Guid.NewGuid(), SetKind.Warmup, Weight.Kg(20m), 10, null, Start);
        var middle = session.LogSet(exercise.Id, Guid.NewGuid(), SetKind.Working, Weight.Kg(25m), 8, null, Start).Value;
        session.LogSet(exercise.Id, Guid.NewGuid(), SetKind.Working, Weight.Kg(27.5m), 8, null, Start);

        session.DeleteSet(exercise.Id, middle.Id).IsSuccess.ShouldBeTrue();

        session.Exercises[0].Sets.Select(s => s.Order).ShouldBe([1, 2]);
        session.Exercises[0].Sets.Select(s => s.Weight.Value).ShouldBe([20m, 27.5m]);
    }

    [Fact]
    public void Deleting_an_unknown_set_fails()
    {
        var session = ProgramSession();

        session.DeleteSet(session.Exercises[0].Id, LoggedSetId.New())
            .Error.Code.ShouldBe(TrainingErrors.SetNotFound);
    }

    [Fact]
    public void A_session_without_a_working_set_cannot_be_completed()
    {
        var session = ProgramSession();
        session.LogSet(session.Exercises[0].Id, Guid.NewGuid(), SetKind.Warmup, Weight.Kg(20m), 10, null, Start);

        session.Complete(Start.AddHours(1)).Error.Code.ShouldBe(TrainingErrors.SessionNeedsWorkingSet);
    }

    [Fact]
    public void Completing_a_session_closes_it_and_reports_its_working_set_count()
    {
        var session = ProgramSession();
        var exercise = session.Exercises[0];
        session.LogSet(exercise.Id, Guid.NewGuid(), SetKind.Warmup, Weight.Kg(20m), 10, null, Start);
        session.LogSet(exercise.Id, Guid.NewGuid(), SetKind.Working, Weight.Kg(27.5m), 8, null, Start);
        session.LogSet(exercise.Id, Guid.NewGuid(), SetKind.Working, Weight.Kg(27.5m), 7, null, Start);
        var finishedAt = Start.AddMinutes(75);

        session.Complete(finishedAt).IsSuccess.ShouldBeTrue();

        session.Status.ShouldBe(SessionStatus.Completed);
        session.CompletedAt.ShouldBe(finishedAt);
        session.WorkingSetCount.ShouldBe(2);

        var raised = session.DomainEvents.OfType<WorkoutSessionCompleted>().ShouldHaveSingleItem();
        raised.SessionId.ShouldBe(session.Id);
        raised.CompletedAt.ShouldBe(finishedAt);
        raised.WorkingSetCount.ShouldBe(2);
    }

    [Fact]
    public void A_completed_session_rejects_every_further_mutation()
    {
        var session = CompletedSession();
        var exerciseId = session.Exercises[0].Id;
        var setId = session.Exercises[0].Sets[0].Id;

        session.LogSet(exerciseId, Guid.NewGuid(), SetKind.Working, Weight.Kg(30m), 5, null, Start)
            .Error.Code.ShouldBe(TrainingErrors.SessionClosed);
        session.EditSet(exerciseId, setId, Weight.Kg(30m), 5, null)
            .Error.Code.ShouldBe(TrainingErrors.SessionClosed);
        session.DeleteSet(exerciseId, setId).Error.Code.ShouldBe(TrainingErrors.SessionClosed);
        session.AddExercise(Snapshot()).Error.Code.ShouldBe(TrainingErrors.SessionClosed);
        session.Complete(Start.AddHours(2)).Error.Code.ShouldBe(TrainingErrors.SessionClosed);
        session.Abandon(Start.AddHours(2)).Error.Code.ShouldBe(TrainingErrors.SessionClosed);
    }

    [Fact]
    public void An_abandoned_session_is_closed_without_requiring_a_working_set()
    {
        var session = ProgramSession();

        session.Abandon(Start.AddMinutes(5)).IsSuccess.ShouldBeTrue();

        session.Status.ShouldBe(SessionStatus.Abandoned);
        session.DomainEvents.OfType<WorkoutSessionCompleted>().ShouldBeEmpty();
    }

    [Fact]
    public void Bodyweight_can_be_recorded_while_the_session_is_open()
    {
        var session = ProgramSession();

        session.RecordBodyweight(Weight.Kg(74.5m)).IsSuccess.ShouldBeTrue();

        session.BodyweightAtSession!.Value.ShouldBe(74.5m);
    }

    private static WorkoutSession CompletedSession()
    {
        var session = ProgramSession();
        session.LogSet(session.Exercises[0].Id, Guid.NewGuid(), SetKind.Working, Weight.Kg(27.5m), 8, null, Start);
        session.Complete(Start.AddHours(1));
        session.ClearDomainEvents();
        return session;
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/MyGym.Modules.Training.Domain.Tests --filter WorkoutSessionTests`
Expected: FAIL — `WorkoutSession` does not exist.

- [ ] **Step 3: Write the value objects**

`src/Modules/Training/MyGym.Modules.Training.Domain/ValueObjects/SessionOrigin.cs`:

```csharp
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Domain.ValueObjects;

public sealed record SessionOrigin(ProgramId? ProgramId, int? ProgramRevision, ProgramDayId? ProgramDayId)
{
    public static SessionOrigin AdHoc { get; } = new(null, null, null);

    public static SessionOrigin FromProgram(ProgramId programId, int revision, ProgramDayId dayId) =>
        new(programId, revision, dayId);

    public bool IsAdHoc => ProgramId is null;
}
```

`src/Modules/Training/MyGym.Modules.Training.Domain/ValueObjects/PrescriptionSnapshot.cs`:

```csharp
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Domain.ValueObjects;

/// <summary>
/// What was prescribed, frozen at the moment a session started. Programs may be edited afterwards;
/// a logged session must never change as a result.
/// </summary>
public sealed class PrescriptionSnapshot : ValueObject
{
    private PrescriptionSnapshot(
        ExerciseId exerciseId,
        ExerciseId? alternativeExerciseId,
        int workingSetCount,
        RepRange targetReps,
        RestPeriod rest,
        WarmupRamp warmup,
        string? notes)
    {
        ExerciseId = exerciseId;
        AlternativeExerciseId = alternativeExerciseId;
        WorkingSetCount = workingSetCount;
        TargetReps = targetReps;
        Rest = rest;
        Warmup = warmup;
        Notes = notes;
    }

    public ExerciseId ExerciseId { get; }
    public ExerciseId? AlternativeExerciseId { get; }
    public int WorkingSetCount { get; }
    public RepRange TargetReps { get; }
    public RestPeriod Rest { get; }
    public WarmupRamp Warmup { get; }
    public string? Notes { get; }

    public static Result<PrescriptionSnapshot> Create(
        ExerciseId exerciseId,
        ExerciseId? alternativeExerciseId,
        int workingSetCount,
        RepRange targetReps,
        RestPeriod rest,
        WarmupRamp warmup,
        string? notes)
    {
        if (workingSetCount is < 1 or > 10)
            return Result<PrescriptionSnapshot>.Failure(new Error(
                TrainingErrors.PrescriptionWorkingSetsOutOfBounds,
                "Working sets must be between 1 and 10."));

        return Result<PrescriptionSnapshot>.Success(new PrescriptionSnapshot(
            exerciseId, alternativeExerciseId, workingSetCount, targetReps, rest, warmup, notes));
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return ExerciseId;
        yield return AlternativeExerciseId;
        yield return WorkingSetCount;
        yield return TargetReps;
        yield return Rest;
        yield return Warmup;
        yield return Notes;
    }
}
```

- [ ] **Step 4: Write the aggregate**

`src/Modules/Training/MyGym.Modules.Training.Domain/Sessions/LoggedSet.cs`:

```csharp
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Domain.Sessions;

public enum SetKind
{
    Warmup = 1,
    Working = 2
}

public sealed class LoggedSet : Entity<LoggedSetId>
{
    public const int MinReps = 0;
    public const int MaxReps = 100;
    public const int MinRir = 0;
    public const int MaxRir = 10;

    private LoggedSet(
        LoggedSetId id,
        SessionExerciseId sessionExerciseId,
        int order,
        SetKind kind,
        Weight weight,
        int reps,
        int? rir,
        Guid clientSetId,
        DateTimeOffset loggedAt) : base(id)
    {
        SessionExerciseId = sessionExerciseId;
        Order = order;
        Kind = kind;
        Weight = weight;
        Reps = reps;
        Rir = rir;
        ClientSetId = clientSetId;
        LoggedAt = loggedAt;
    }

    public SessionExerciseId SessionExerciseId { get; private set; }
    public int Order { get; private set; }
    public SetKind Kind { get; private set; }
    public Weight Weight { get; private set; }
    public int Reps { get; private set; }
    public int? Rir { get; private set; }

    /// <summary>Client-generated identity that makes logging idempotent over a flaky connection.</summary>
    public Guid ClientSetId { get; private set; }
    public DateTimeOffset LoggedAt { get; private set; }

    internal static Result<LoggedSet> Create(
        SessionExerciseId sessionExerciseId,
        int order,
        SetKind kind,
        Weight weight,
        int reps,
        int? rir,
        Guid clientSetId,
        DateTimeOffset loggedAt)
    {
        var validated = Validate(reps, rir);
        return validated.IsFailure
            ? Result<LoggedSet>.Failure(validated.Error)
            : Result<LoggedSet>.Success(new LoggedSet(
                LoggedSetId.New(), sessionExerciseId, order, kind, weight, reps, rir, clientSetId, loggedAt));
    }

    internal Result Update(Weight weight, int reps, int? rir)
    {
        var validated = Validate(reps, rir);
        if (validated.IsFailure)
            return validated;

        Weight = weight;
        Reps = reps;
        Rir = rir;
        return Result.Success();
    }

    internal void SetOrder(int order) => Order = order;

    private static Result Validate(int reps, int? rir)
    {
        if (reps is < MinReps or > MaxReps)
            return Result.Failure(new Error(
                TrainingErrors.SetRepsOutOfBounds, $"Reps must be between {MinReps} and {MaxReps}."));

        if (rir is < MinRir or > MaxRir)
            return Result.Failure(new Error(
                TrainingErrors.SetRirOutOfBounds, $"Reps in reserve must be between {MinRir} and {MaxRir}."));

        return Result.Success();
    }
}
```

`src/Modules/Training/MyGym.Modules.Training.Domain/Sessions/SessionExercise.cs`:

```csharp
using MyGym.Modules.Training.Domain.ValueObjects;
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Domain.Sessions;

public sealed class SessionExercise : Entity<SessionExerciseId>
{
    private readonly List<LoggedSet> _sets = [];

    private SessionExercise(
        SessionExerciseId id,
        SessionId sessionId,
        int order,
        PrescriptionSnapshot prescription) : base(id)
    {
        SessionId = sessionId;
        Order = order;
        Prescription = prescription;
    }

    public SessionId SessionId { get; private set; }
    public int Order { get; private set; }
    public PrescriptionSnapshot Prescription { get; private set; }
    public ExerciseId ExerciseId => Prescription.ExerciseId;

    public IReadOnlyList<LoggedSet> Sets => _sets.OrderBy(s => s.Order).ToList();

    internal static SessionExercise Create(SessionId sessionId, int order, PrescriptionSnapshot prescription) =>
        new(SessionExerciseId.New(), sessionId, order, prescription);

    internal void SetOrder(int order) => Order = order;

    internal LoggedSet? FindByClientSetId(Guid clientSetId) =>
        _sets.SingleOrDefault(s => s.ClientSetId == clientSetId);

    internal Result<LoggedSet> AddSet(
        SetKind kind, Weight weight, int reps, int? rir, Guid clientSetId, DateTimeOffset loggedAt)
    {
        var result = LoggedSet.Create(Id, _sets.Count + 1, kind, weight, reps, rir, clientSetId, loggedAt);
        if (result.IsFailure)
            return result;

        _sets.Add(result.Value);
        return result;
    }

    internal Result UpdateSet(LoggedSetId setId, Weight weight, int reps, int? rir)
    {
        var set = _sets.SingleOrDefault(s => s.Id == setId);
        return set is null
            ? Result.Failure(new Error(TrainingErrors.SetNotFound, "Set not found."))
            : set.Update(weight, reps, rir);
    }

    internal Result RemoveSet(LoggedSetId setId)
    {
        var set = _sets.SingleOrDefault(s => s.Id == setId);
        if (set is null)
            return Result.Failure(new Error(TrainingErrors.SetNotFound, "Set not found."));

        _sets.Remove(set);

        var order = 1;
        foreach (var remaining in _sets.OrderBy(s => s.Order).ToList())
            remaining.SetOrder(order++);

        return Result.Success();
    }

    internal int CountWorkingSets() => _sets.Count(s => s.Kind == SetKind.Working);
}
```

`src/Modules/Training/MyGym.Modules.Training.Domain/Sessions/WorkoutSession.cs`:

```csharp
using MyGym.Modules.Training.Contracts;
using MyGym.Modules.Training.Domain.ValueObjects;
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Domain.Sessions;

public enum SessionStatus
{
    InProgress = 1,
    Completed = 2,
    Abandoned = 3
}

public sealed class WorkoutSession : AggregateRoot<SessionId>
{
    private readonly List<SessionExercise> _exercises = [];

    private WorkoutSession(SessionId id, UserId userId, SessionOrigin origin, DateTimeOffset startedAt)
        : base(id)
    {
        UserId = userId;
        Origin = origin;
        StartedAt = startedAt;
        Status = SessionStatus.InProgress;
    }

    public UserId UserId { get; private set; }
    public SessionOrigin Origin { get; private set; }
    public SessionStatus Status { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public Weight? BodyweightAtSession { get; private set; }

    public IReadOnlyList<SessionExercise> Exercises => _exercises.OrderBy(e => e.Order).ToList();

    public int WorkingSetCount => _exercises.Sum(e => e.CountWorkingSets());

    public static Result<WorkoutSession> Start(
        UserId userId,
        SessionOrigin origin,
        IReadOnlyList<PrescriptionSnapshot> prescriptions,
        DateTimeOffset startedAt)
    {
        if (!origin.IsAdHoc && prescriptions.Count == 0)
            return Result<WorkoutSession>.Failure(new Error(
                TrainingErrors.SessionNeedsExercise,
                "A session started from a program day needs at least one exercise."));

        var session = new WorkoutSession(SessionId.New(), userId, origin, startedAt);

        var order = 1;
        foreach (var prescription in prescriptions)
            session._exercises.Add(SessionExercise.Create(session.Id, order++, prescription));

        session.Raise(new WorkoutSessionStarted(session.Id, userId, startedAt));
        return Result<WorkoutSession>.Success(session);
    }

    public Result<SessionExercise> AddExercise(PrescriptionSnapshot prescription)
    {
        var open = EnsureOpen();
        if (open.IsFailure)
            return Result<SessionExercise>.Failure(open.Error);

        var exercise = SessionExercise.Create(Id, _exercises.Count + 1, prescription);
        _exercises.Add(exercise);
        return Result<SessionExercise>.Success(exercise);
    }

    /// <summary>
    /// Idempotent on <paramref name="clientSetId"/>: a retry after a dropped response returns the
    /// set that was already stored instead of creating a duplicate.
    /// </summary>
    public Result<LoggedSet> LogSet(
        SessionExerciseId sessionExerciseId,
        Guid clientSetId,
        SetKind kind,
        Weight weight,
        int reps,
        int? rir,
        DateTimeOffset loggedAt)
    {
        var open = EnsureOpen();
        if (open.IsFailure)
            return Result<LoggedSet>.Failure(open.Error);

        var exercise = FindExercise(sessionExerciseId);
        if (exercise is null)
            return Result<LoggedSet>.Failure(new Error(
                TrainingErrors.SessionExerciseNotFound, "Session exercise not found."));

        var existing = exercise.FindByClientSetId(clientSetId);
        if (existing is not null)
            return Result<LoggedSet>.Success(existing);

        return exercise.AddSet(kind, weight, reps, rir, clientSetId, loggedAt);
    }

    public Result EditSet(SessionExerciseId sessionExerciseId, LoggedSetId setId, Weight weight, int reps, int? rir)
    {
        var open = EnsureOpen();
        if (open.IsFailure)
            return open;

        var exercise = FindExercise(sessionExerciseId);
        return exercise is null
            ? Result.Failure(new Error(TrainingErrors.SessionExerciseNotFound, "Session exercise not found."))
            : exercise.UpdateSet(setId, weight, reps, rir);
    }

    public Result DeleteSet(SessionExerciseId sessionExerciseId, LoggedSetId setId)
    {
        var open = EnsureOpen();
        if (open.IsFailure)
            return open;

        var exercise = FindExercise(sessionExerciseId);
        return exercise is null
            ? Result.Failure(new Error(TrainingErrors.SessionExerciseNotFound, "Session exercise not found."))
            : exercise.RemoveSet(setId);
    }

    public Result RecordBodyweight(Weight bodyweight)
    {
        var open = EnsureOpen();
        if (open.IsFailure)
            return open;

        BodyweightAtSession = bodyweight;
        return Result.Success();
    }

    public Result Complete(DateTimeOffset completedAt)
    {
        var open = EnsureOpen();
        if (open.IsFailure)
            return open;

        var workingSets = WorkingSetCount;
        if (workingSets == 0)
            return Result.Failure(new Error(
                TrainingErrors.SessionNeedsWorkingSet, "Log at least one working set before finishing."));

        Status = SessionStatus.Completed;
        CompletedAt = completedAt;
        Raise(new WorkoutSessionCompleted(Id, UserId, completedAt, workingSets));
        return Result.Success();
    }

    public Result Abandon(DateTimeOffset abandonedAt)
    {
        var open = EnsureOpen();
        if (open.IsFailure)
            return open;

        Status = SessionStatus.Abandoned;
        CompletedAt = abandonedAt;
        return Result.Success();
    }

    private SessionExercise? FindExercise(SessionExerciseId id) =>
        _exercises.SingleOrDefault(e => e.Id == id);

    private Result EnsureOpen() =>
        Status == SessionStatus.InProgress
            ? Result.Success()
            : Result.Failure(new Error(TrainingErrors.SessionClosed, "This session is already closed."));
}
```

`src/Modules/Training/MyGym.Modules.Training.Domain/Sessions/IWorkoutSessionRepository.cs`:

```csharp
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Domain.Sessions;

public interface IWorkoutSessionRepository
{
    Task<WorkoutSession?> GetByIdAsync(SessionId id, CancellationToken ct);
    Task<WorkoutSession?> GetInProgressForUserAsync(UserId userId, CancellationToken ct);
    Task AddAsync(WorkoutSession session, CancellationToken ct);
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/MyGym.Modules.Training.Domain.Tests`
Expected: PASS — the whole Training domain suite.

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Training tests/MyGym.Modules.Training.Domain.Tests
git commit -m "feat: add WorkoutSession aggregate with idempotent set logging"
```

---

### Task 6: Identity module — AppUser and athlete profile

**Files:**
- Create: `src/Modules/Identity/MyGym.Modules.Identity.Contracts/Events.cs`
- Create: `src/Modules/Identity/MyGym.Modules.Identity.Domain/{AppUser,AthleteProfile,IAppUserRepository,IdentityErrors}.cs`
- Test: `tests/MyGym.Modules.Identity.Domain.Tests/AppUserTests.cs`

**Interfaces:**
- Consumes: `AggregateRoot<TId>`, `ValueObject`, `Result`, `UserId`, `Weight`, `WeightUnit`, `WeightIncrement` from Tasks 1–2.
- Produces:
  - `ExperienceLevel` (`Beginner`, `Intermediate`, `Advanced`)
  - `AthleteProfile` with `BirthYear`, `Bodyweight`, `PreferredUnit`, `LoadingIncrement`, `ExperienceLevel`, `AvailableDaysPerWeek`, `SessionMinutes`, `TimeZoneId`, and `Create(int?, Weight?, WeightUnit, WeightIncrement, ExperienceLevel, int?, int?, string) → Result<AthleteProfile>`, `AthleteProfile.Default`
  - `AppUser` with `TelegramUserId`, `FirstName`, `Username`, `LanguageCode`, `Profile`, `CreatedAt`, `LastSeenAt`, and `Register(long, string, string?, string?, DateTimeOffset) → Result<AppUser>`, `UpdateProfile(AthleteProfile) → Result`, `TouchLastSeen(DateTimeOffset)`
  - `AppUserRegistered(UserId UserId, long TelegramUserId, DateTimeOffset RegisteredAt) : IDomainEvent`
  - `IAppUserRepository` with `GetByIdAsync(UserId, CancellationToken)`, `GetByTelegramUserIdAsync(long, CancellationToken)`, `AddAsync(AppUser, CancellationToken)`, `Remove(AppUser)`
  - `IdentityErrors` constants

- [ ] **Step 1: Create the projects**

```bash
dotnet new classlib -o src/Modules/Identity/MyGym.Modules.Identity.Contracts
dotnet new classlib -o src/Modules/Identity/MyGym.Modules.Identity.Domain
dotnet new xunit3 -o tests/MyGym.Modules.Identity.Domain.Tests
dotnet sln add src/Modules/Identity/MyGym.Modules.Identity.Contracts \
               src/Modules/Identity/MyGym.Modules.Identity.Domain \
               tests/MyGym.Modules.Identity.Domain.Tests
dotnet add src/Modules/Identity/MyGym.Modules.Identity.Contracts reference src/Shared/MyGym.SharedKernel
dotnet add src/Modules/Identity/MyGym.Modules.Identity.Domain reference \
           src/Shared/MyGym.SharedKernel src/Modules/Identity/MyGym.Modules.Identity.Contracts
dotnet add tests/MyGym.Modules.Identity.Domain.Tests reference src/Modules/Identity/MyGym.Modules.Identity.Domain
dotnet add tests/MyGym.Modules.Identity.Domain.Tests package Shouldly
rm src/Modules/Identity/MyGym.Modules.Identity.Domain/Class1.cs \
   src/Modules/Identity/MyGym.Modules.Identity.Contracts/Class1.cs
```

- [ ] **Step 2: Write the failing tests**

`tests/MyGym.Modules.Identity.Domain.Tests/AppUserTests.cs`:

```csharp
using MyGym.Modules.Identity.Contracts;
using MyGym.Modules.Identity.Domain;
using MyGym.SharedKernel;
using Shouldly;

namespace MyGym.Modules.Identity.Domain.Tests;

public class AppUserTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Registering_links_the_telegram_account_and_applies_default_profile()
    {
        var user = AppUser.Register(123456789L, "Akmal", "akmal", "uz", Now).Value;

        user.TelegramUserId.ShouldBe(123456789L);
        user.FirstName.ShouldBe("Akmal");
        user.Username.ShouldBe("akmal");
        user.LanguageCode.ShouldBe("uz");
        user.CreatedAt.ShouldBe(Now);
        user.LastSeenAt.ShouldBe(Now);
        user.Profile.PreferredUnit.ShouldBe(WeightUnit.Kilograms);
        user.Profile.ExperienceLevel.ShouldBe(ExperienceLevel.Beginner);
        user.Profile.TimeZoneId.ShouldBe("UTC");
    }

    [Fact]
    public void Registering_raises_the_registration_event()
    {
        var user = AppUser.Register(42L, "A", null, null, Now).Value;

        var raised = user.DomainEvents.OfType<AppUserRegistered>().ShouldHaveSingleItem();
        raised.UserId.ShouldBe(user.Id);
        raised.TelegramUserId.ShouldBe(42L);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void A_non_positive_telegram_id_is_rejected(long telegramUserId)
    {
        AppUser.Register(telegramUserId, "A", null, null, Now)
            .Error.Code.ShouldBe(IdentityErrors.TelegramIdInvalid);
    }

    [Fact]
    public void A_blank_first_name_is_rejected()
    {
        AppUser.Register(42L, "   ", null, null, Now).Error.Code.ShouldBe(IdentityErrors.FirstNameRequired);
    }

    [Fact]
    public void A_complete_profile_is_accepted()
    {
        var profile = AthleteProfile.Create(
            birthYear: 1996,
            bodyweight: Weight.Kg(74.5m),
            preferredUnit: WeightUnit.Kilograms,
            loadingIncrement: WeightIncrement.DefaultKg,
            experienceLevel: ExperienceLevel.Intermediate,
            availableDaysPerWeek: 3,
            sessionMinutes: 90,
            timeZoneId: "Asia/Tashkent").Value;

        var user = AppUser.Register(42L, "A", null, "uz", Now).Value;
        user.UpdateProfile(profile).IsSuccess.ShouldBeTrue();

        user.Profile.BirthYear.ShouldBe(1996);
        user.Profile.Bodyweight!.Value.ShouldBe(74.5m);
        user.Profile.AvailableDaysPerWeek.ShouldBe(3);
        user.Profile.SessionMinutes.ShouldBe(90);
        user.Profile.TimeZoneId.ShouldBe("Asia/Tashkent");
    }

    [Theory]
    [InlineData(1919)]
    [InlineData(2021)]
    public void An_implausible_birth_year_is_rejected(int birthYear)
    {
        Profile(birthYear: birthYear).Error.Code.ShouldBe(IdentityErrors.BirthYearOutOfBounds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    public void Available_days_outside_one_to_seven_are_rejected(int days)
    {
        Profile(days: days).Error.Code.ShouldBe(IdentityErrors.AvailableDaysOutOfBounds);
    }

    [Theory]
    [InlineData(19)]
    [InlineData(241)]
    public void Session_length_outside_twenty_to_two_hundred_forty_minutes_is_rejected(int minutes)
    {
        Profile(minutes: minutes).Error.Code.ShouldBe(IdentityErrors.SessionMinutesOutOfBounds);
    }

    [Fact]
    public void An_unknown_time_zone_is_rejected()
    {
        Profile(timeZoneId: "Mars/Olympus").Error.Code.ShouldBe(IdentityErrors.TimeZoneUnknown);
    }

    [Fact]
    public void Touching_last_seen_moves_it_forward_only()
    {
        var user = AppUser.Register(42L, "A", null, null, Now).Value;

        user.TouchLastSeen(Now.AddHours(5));
        user.LastSeenAt.ShouldBe(Now.AddHours(5));

        user.TouchLastSeen(Now.AddHours(1));
        user.LastSeenAt.ShouldBe(Now.AddHours(5));
    }

    private static Result<AthleteProfile> Profile(
        int? birthYear = 1996, int? days = 3, int? minutes = 90, string timeZoneId = "UTC") =>
        AthleteProfile.Create(
            birthYear, Weight.Kg(74.5m), WeightUnit.Kilograms, WeightIncrement.DefaultKg,
            ExperienceLevel.Intermediate, days, minutes, timeZoneId);
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/MyGym.Modules.Identity.Domain.Tests`
Expected: FAIL — `AppUser` does not exist.

- [ ] **Step 4: Write the implementation**

`src/Modules/Identity/MyGym.Modules.Identity.Contracts/Events.cs`:

```csharp
using MyGym.SharedKernel;

namespace MyGym.Modules.Identity.Contracts;

public sealed record AppUserRegistered(UserId UserId, long TelegramUserId, DateTimeOffset RegisteredAt)
    : IDomainEvent;
```

`src/Modules/Identity/MyGym.Modules.Identity.Domain/IdentityErrors.cs`:

```csharp
namespace MyGym.Modules.Identity.Domain;

public static class IdentityErrors
{
    public const string TelegramIdInvalid = "identity.telegram_id_invalid";
    public const string FirstNameRequired = "identity.first_name_required";
    public const string BirthYearOutOfBounds = "profile.birth_year_out_of_bounds";
    public const string AvailableDaysOutOfBounds = "profile.available_days_out_of_bounds";
    public const string SessionMinutesOutOfBounds = "profile.session_minutes_out_of_bounds";
    public const string TimeZoneUnknown = "profile.time_zone_unknown";
    public const string UserNotFound = "identity.user_not_found";
}
```

`src/Modules/Identity/MyGym.Modules.Identity.Domain/AthleteProfile.cs`:

```csharp
using MyGym.SharedKernel;

namespace MyGym.Modules.Identity.Domain;

public enum ExperienceLevel
{
    Beginner = 1,
    Intermediate = 2,
    Advanced = 3
}

public sealed class AthleteProfile : ValueObject
{
    public const int MinBirthYear = 1920;
    public const int MaxBirthYear = 2020;
    public const int MinSessionMinutes = 20;
    public const int MaxSessionMinutes = 240;

    private AthleteProfile(
        int? birthYear,
        Weight? bodyweight,
        WeightUnit preferredUnit,
        WeightIncrement loadingIncrement,
        ExperienceLevel experienceLevel,
        int? availableDaysPerWeek,
        int? sessionMinutes,
        string timeZoneId)
    {
        BirthYear = birthYear;
        Bodyweight = bodyweight;
        PreferredUnit = preferredUnit;
        LoadingIncrement = loadingIncrement;
        ExperienceLevel = experienceLevel;
        AvailableDaysPerWeek = availableDaysPerWeek;
        SessionMinutes = sessionMinutes;
        TimeZoneId = timeZoneId;
    }

    public int? BirthYear { get; }
    public Weight? Bodyweight { get; }
    public WeightUnit PreferredUnit { get; }
    public WeightIncrement LoadingIncrement { get; }
    public ExperienceLevel ExperienceLevel { get; }
    public int? AvailableDaysPerWeek { get; }
    public int? SessionMinutes { get; }
    public string TimeZoneId { get; }

    public static AthleteProfile Default { get; } = new(
        null, null, WeightUnit.Kilograms, WeightIncrement.DefaultKg,
        ExperienceLevel.Beginner, null, null, "UTC");

    public static Result<AthleteProfile> Create(
        int? birthYear,
        Weight? bodyweight,
        WeightUnit preferredUnit,
        WeightIncrement loadingIncrement,
        ExperienceLevel experienceLevel,
        int? availableDaysPerWeek,
        int? sessionMinutes,
        string timeZoneId)
    {
        if (birthYear is < MinBirthYear or > MaxBirthYear)
            return Result<AthleteProfile>.Failure(new Error(
                IdentityErrors.BirthYearOutOfBounds,
                $"Birth year must be between {MinBirthYear} and {MaxBirthYear}."));

        if (availableDaysPerWeek is < 1 or > 7)
            return Result<AthleteProfile>.Failure(new Error(
                IdentityErrors.AvailableDaysOutOfBounds, "Available days must be between 1 and 7."));

        if (sessionMinutes is < MinSessionMinutes or > MaxSessionMinutes)
            return Result<AthleteProfile>.Failure(new Error(
                IdentityErrors.SessionMinutesOutOfBounds,
                $"Session length must be between {MinSessionMinutes} and {MaxSessionMinutes} minutes."));

        if (!TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out _))
            return Result<AthleteProfile>.Failure(new Error(
                IdentityErrors.TimeZoneUnknown, "Unknown time zone."));

        return Result<AthleteProfile>.Success(new AthleteProfile(
            birthYear, bodyweight, preferredUnit, loadingIncrement,
            experienceLevel, availableDaysPerWeek, sessionMinutes, timeZoneId));
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return BirthYear;
        yield return Bodyweight;
        yield return PreferredUnit;
        yield return LoadingIncrement;
        yield return ExperienceLevel;
        yield return AvailableDaysPerWeek;
        yield return SessionMinutes;
        yield return TimeZoneId;
    }
}
```

`src/Modules/Identity/MyGym.Modules.Identity.Domain/AppUser.cs`:

```csharp
using MyGym.Modules.Identity.Contracts;
using MyGym.SharedKernel;

namespace MyGym.Modules.Identity.Domain;

public sealed class AppUser : AggregateRoot<UserId>
{
    private AppUser(
        UserId id,
        long telegramUserId,
        string firstName,
        string? username,
        string? languageCode,
        DateTimeOffset createdAt) : base(id)
    {
        TelegramUserId = telegramUserId;
        FirstName = firstName;
        Username = username;
        LanguageCode = languageCode;
        Profile = AthleteProfile.Default;
        CreatedAt = createdAt;
        LastSeenAt = createdAt;
    }

    public long TelegramUserId { get; private set; }
    public string FirstName { get; private set; }
    public string? Username { get; private set; }
    public string? LanguageCode { get; private set; }
    public AthleteProfile Profile { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset LastSeenAt { get; private set; }

    public static Result<AppUser> Register(
        long telegramUserId,
        string firstName,
        string? username,
        string? languageCode,
        DateTimeOffset now)
    {
        if (telegramUserId <= 0)
            return Result<AppUser>.Failure(new Error(
                IdentityErrors.TelegramIdInvalid, "Telegram user id must be positive."));

        var trimmed = firstName?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return Result<AppUser>.Failure(new Error(
                IdentityErrors.FirstNameRequired, "A first name is required."));

        var user = new AppUser(UserId.New(), telegramUserId, trimmed, username, languageCode, now);
        user.Raise(new AppUserRegistered(user.Id, telegramUserId, now));
        return Result<AppUser>.Success(user);
    }

    public Result UpdateProfile(AthleteProfile profile)
    {
        Profile = profile;
        return Result.Success();
    }

    public void TouchLastSeen(DateTimeOffset now)
    {
        if (now > LastSeenAt)
            LastSeenAt = now;
    }
}
```

`src/Modules/Identity/MyGym.Modules.Identity.Domain/IAppUserRepository.cs`:

```csharp
using MyGym.SharedKernel;

namespace MyGym.Modules.Identity.Domain;

public interface IAppUserRepository
{
    Task<AppUser?> GetByIdAsync(UserId id, CancellationToken ct);
    Task<AppUser?> GetByTelegramUserIdAsync(long telegramUserId, CancellationToken ct);
    Task AddAsync(AppUser user, CancellationToken ct);
    void Remove(AppUser user);
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/MyGym.Modules.Identity.Domain.Tests`
Expected: PASS — 14 tests.

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Identity tests/MyGym.Modules.Identity.Domain.Tests
git commit -m "feat: add Identity module with AppUser and athlete profile"
```

---

### Task 7: ExerciseCatalog module with multilingual names

**Files:**
- Create: `src/Modules/ExerciseCatalog/MyGym.Modules.ExerciseCatalog.Domain/{Exercise,ExerciseName,Enums,IExerciseRepository,CatalogErrors}.cs`
- Create: `src/Modules/ExerciseCatalog/MyGym.Modules.ExerciseCatalog.Domain/CatalogSeed.cs`
- Test: `tests/MyGym.Modules.ExerciseCatalog.Domain.Tests/{ExerciseTests,CatalogSeedTests}.cs`

**Interfaces:**
- Consumes: `AggregateRoot<TId>`, `ValueObject`, `Result`, `ExerciseId`, `UserId` from Tasks 1–2.
- Produces:
  - `MovementPattern` (`HorizontalPush`, `VerticalPush`, `HorizontalPull`, `VerticalPull`, `Squat`, `Hinge`, `Lunge`, `Carry`, `Isolation`, `Core`)
  - `MuscleGroup` (`Chest`, `Back`, `Lats`, `Shoulders`, `Biceps`, `Triceps`, `Quads`, `Hamstrings`, `Glutes`, `Calves`, `Abs`, `Forearms`, `Traps`)
  - `Equipment` (`Barbell`, `Dumbbell`, `Machine`, `Cable`, `Bodyweight`, `EzBar`, `Other`)
  - `ExerciseName(string LanguageCode, string Value)` value object with `Create(string, string) → Result<ExerciseName>`
  - `Exercise` with `Slug`, `MovementPattern`, `PrimaryMuscles`, `SecondaryMuscles`, `Equipment`, `Names`, `Aliases`, `OwnerUserId`, `IsCustom`, `NameFor(string languageCode) → string`, and `Create(...) → Result<Exercise>`, `CreateCustom(...) → Result<Exercise>`
  - `IExerciseRepository` with `GetByIdAsync`, `GetBySlugAsync`, `ExistsAsync(ExerciseId, CancellationToken)`, `AddAsync`
  - `CatalogSeed.Exercises` — the 26 seeded exercises
- Depended on by: Task 12's `StartSessionCommandHandler`, which validates prescription exercise ids via `IExerciseRepository.ExistsAsync`.

- [ ] **Step 1: Create the projects**

```bash
dotnet new classlib -o src/Modules/ExerciseCatalog/MyGym.Modules.ExerciseCatalog.Contracts
dotnet new classlib -o src/Modules/ExerciseCatalog/MyGym.Modules.ExerciseCatalog.Domain
dotnet new xunit3 -o tests/MyGym.Modules.ExerciseCatalog.Domain.Tests
dotnet sln add src/Modules/ExerciseCatalog/MyGym.Modules.ExerciseCatalog.Contracts \
               src/Modules/ExerciseCatalog/MyGym.Modules.ExerciseCatalog.Domain \
               tests/MyGym.Modules.ExerciseCatalog.Domain.Tests
dotnet add src/Modules/ExerciseCatalog/MyGym.Modules.ExerciseCatalog.Contracts reference src/Shared/MyGym.SharedKernel
dotnet add src/Modules/ExerciseCatalog/MyGym.Modules.ExerciseCatalog.Domain reference \
           src/Shared/MyGym.SharedKernel src/Modules/ExerciseCatalog/MyGym.Modules.ExerciseCatalog.Contracts
dotnet add tests/MyGym.Modules.ExerciseCatalog.Domain.Tests reference \
           src/Modules/ExerciseCatalog/MyGym.Modules.ExerciseCatalog.Domain
dotnet add tests/MyGym.Modules.ExerciseCatalog.Domain.Tests package Shouldly
rm src/Modules/ExerciseCatalog/MyGym.Modules.ExerciseCatalog.Domain/Class1.cs \
   src/Modules/ExerciseCatalog/MyGym.Modules.ExerciseCatalog.Contracts/Class1.cs
```

- [ ] **Step 2: Write the failing tests**

`tests/MyGym.Modules.ExerciseCatalog.Domain.Tests/ExerciseTests.cs`:

```csharp
using MyGym.Modules.ExerciseCatalog.Domain;
using MyGym.SharedKernel;
using Shouldly;

namespace MyGym.Modules.ExerciseCatalog.Domain.Tests;

public class ExerciseTests
{
    private static Result<Exercise> Create(string slug = "incline-dumbbell-press") =>
        Exercise.Create(
            slug,
            MovementPattern.HorizontalPush,
            [MuscleGroup.Chest],
            [MuscleGroup.Shoulders, MuscleGroup.Triceps],
            Equipment.Dumbbell,
            [ExerciseName.Create("en", "Incline Dumbbell Press").Value,
             ExerciseName.Create("uz", "Qiya gantel press").Value],
            ["incline db press", "incline bench dumbbell"]);

    [Fact]
    public void A_catalog_exercise_is_created_with_its_names_and_muscles()
    {
        var exercise = Create().Value;

        exercise.Slug.ShouldBe("incline-dumbbell-press");
        exercise.IsCustom.ShouldBeFalse();
        exercise.OwnerUserId.ShouldBeNull();
        exercise.PrimaryMuscles.ShouldBe([MuscleGroup.Chest]);
        exercise.Names.Count.ShouldBe(2);
        exercise.Aliases.Count.ShouldBe(2);
    }

    [Theory]
    [InlineData("Incline Dumbbell Press")]
    [InlineData("incline_dumbbell_press")]
    [InlineData("incline dumbbell press")]
    [InlineData("")]
    public void A_slug_must_be_lowercase_kebab_case(string slug)
    {
        Create(slug).Error.Code.ShouldBe(CatalogErrors.SlugInvalid);
    }

    [Fact]
    public void An_exercise_needs_at_least_one_name()
    {
        var result = Exercise.Create(
            "x", MovementPattern.Isolation, [MuscleGroup.Biceps], [], Equipment.Dumbbell, [], []);

        result.Error.Code.ShouldBe(CatalogErrors.NameRequired);
    }

    [Fact]
    public void An_exercise_needs_at_least_one_primary_muscle()
    {
        var result = Exercise.Create(
            "x", MovementPattern.Isolation, [], [], Equipment.Dumbbell,
            [ExerciseName.Create("en", "X").Value], []);

        result.Error.Code.ShouldBe(CatalogErrors.PrimaryMuscleRequired);
    }

    [Fact]
    public void Duplicate_language_codes_are_rejected()
    {
        var result = Exercise.Create(
            "x", MovementPattern.Isolation, [MuscleGroup.Biceps], [], Equipment.Dumbbell,
            [ExerciseName.Create("en", "X").Value, ExerciseName.Create("en", "Y").Value], []);

        result.Error.Code.ShouldBe(CatalogErrors.DuplicateLanguage);
    }

    [Fact]
    public void Language_codes_are_normalised_to_lowercase()
    {
        ExerciseName.Create("EN", "Squat").Value.LanguageCode.ShouldBe("en");
    }

    [Fact]
    public void A_two_letter_language_code_is_required()
    {
        ExerciseName.Create("english", "Squat").Error.Code.ShouldBe(CatalogErrors.LanguageCodeInvalid);
    }

    [Fact]
    public void Name_lookup_falls_back_to_english_then_to_the_first_name()
    {
        var exercise = Create().Value;

        exercise.NameFor("uz").ShouldBe("Qiya gantel press");
        exercise.NameFor("ru").ShouldBe("Incline Dumbbell Press");
    }

    [Fact]
    public void A_custom_exercise_belongs_to_its_creator()
    {
        var owner = UserId.New();

        var exercise = Exercise.CreateCustom(
            owner, "my-weird-machine", MovementPattern.Isolation, [MuscleGroup.Calves], [],
            Equipment.Machine, [ExerciseName.Create("en", "My Weird Machine").Value]).Value;

        exercise.IsCustom.ShouldBeTrue();
        exercise.OwnerUserId.ShouldBe(owner);
    }
}
```

`tests/MyGym.Modules.ExerciseCatalog.Domain.Tests/CatalogSeedTests.cs`:

```csharp
using MyGym.Modules.ExerciseCatalog.Domain;
using Shouldly;

namespace MyGym.Modules.ExerciseCatalog.Domain.Tests;

public class CatalogSeedTests
{
    [Fact]
    public void Every_seeded_exercise_is_valid()
    {
        CatalogSeed.Exercises.ShouldNotBeEmpty();
        CatalogSeed.Exercises.Count.ShouldBe(26);
    }

    [Fact]
    public void Seeded_slugs_are_unique()
    {
        CatalogSeed.Exercises.Select(e => e.Slug).Distinct().Count()
            .ShouldBe(CatalogSeed.Exercises.Count);
    }

    [Fact]
    public void Every_seeded_exercise_has_an_english_and_an_uzbek_name()
    {
        foreach (var exercise in CatalogSeed.Exercises)
        {
            exercise.Names.Any(n => n.LanguageCode == "en").ShouldBeTrue(exercise.Slug);
            exercise.Names.Any(n => n.LanguageCode == "uz").ShouldBeTrue(exercise.Slug);
        }
    }

    [Fact]
    public void The_authors_program_exercises_are_all_present()
    {
        string[] required =
        [
            "incline-dumbbell-press", "chest-supported-row", "lat-pulldown", "pull-up",
            "seated-dumbbell-shoulder-press", "machine-chest-press", "dumbbell-lateral-raise",
            "ez-bar-curl", "rope-triceps-pushdown", "back-squat", "romanian-deadlift",
            "leg-press", "walking-lunge", "leg-curl", "standing-calf-raise", "hanging-leg-raise",
            "cable-crunch", "flat-barbell-bench-press", "seated-cable-row", "bulgarian-split-squat",
            "pec-deck", "incline-cable-fly", "rear-delt-fly", "incline-dumbbell-curl",
            "overhead-ez-bar-triceps-extension", "overhead-rope-triceps-extension"
        ];

        var slugs = CatalogSeed.Exercises.Select(e => e.Slug).ToHashSet();
        foreach (var slug in required)
            slugs.ShouldContain(slug);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/MyGym.Modules.ExerciseCatalog.Domain.Tests`
Expected: FAIL — `Exercise` does not exist.

- [ ] **Step 4: Write the implementation**

`src/Modules/ExerciseCatalog/MyGym.Modules.ExerciseCatalog.Domain/Enums.cs`:

```csharp
namespace MyGym.Modules.ExerciseCatalog.Domain;

public enum MovementPattern
{
    HorizontalPush = 1,
    VerticalPush = 2,
    HorizontalPull = 3,
    VerticalPull = 4,
    Squat = 5,
    Hinge = 6,
    Lunge = 7,
    Carry = 8,
    Isolation = 9,
    Core = 10
}

public enum MuscleGroup
{
    Chest = 1,
    Back = 2,
    Lats = 3,
    Shoulders = 4,
    Biceps = 5,
    Triceps = 6,
    Quads = 7,
    Hamstrings = 8,
    Glutes = 9,
    Calves = 10,
    Abs = 11,
    Forearms = 12,
    Traps = 13
}

public enum Equipment
{
    Barbell = 1,
    Dumbbell = 2,
    Machine = 3,
    Cable = 4,
    Bodyweight = 5,
    EzBar = 6,
    Other = 7
}
```

`src/Modules/ExerciseCatalog/MyGym.Modules.ExerciseCatalog.Domain/CatalogErrors.cs`:

```csharp
namespace MyGym.Modules.ExerciseCatalog.Domain;

public static class CatalogErrors
{
    public const string SlugInvalid = "exercise.slug_invalid";
    public const string NameRequired = "exercise.name_required";
    public const string PrimaryMuscleRequired = "exercise.primary_muscle_required";
    public const string DuplicateLanguage = "exercise.duplicate_language";
    public const string LanguageCodeInvalid = "exercise.language_code_invalid";
    public const string NameValueRequired = "exercise.name_value_required";
    public const string ExerciseNotFound = "exercise.not_found";
}
```

`src/Modules/ExerciseCatalog/MyGym.Modules.ExerciseCatalog.Domain/ExerciseName.cs`:

```csharp
using MyGym.SharedKernel;

namespace MyGym.Modules.ExerciseCatalog.Domain;

public sealed class ExerciseName : ValueObject
{
    private ExerciseName(string languageCode, string value)
    {
        LanguageCode = languageCode;
        Value = value;
    }

    /// <summary>ISO 639-1, lowercase — "en", "uz", "ru".</summary>
    public string LanguageCode { get; }
    public string Value { get; }

    public static Result<ExerciseName> Create(string languageCode, string value)
    {
        var code = languageCode?.Trim().ToLowerInvariant() ?? string.Empty;
        if (code.Length != 2 || !code.All(char.IsAsciiLetterLower))
            return Result<ExerciseName>.Failure(new Error(
                CatalogErrors.LanguageCodeInvalid, "Language code must be two ASCII letters."));

        var name = value?.Trim() ?? string.Empty;
        if (name.Length is 0 or > 120)
            return Result<ExerciseName>.Failure(new Error(
                CatalogErrors.NameValueRequired, "A name of 1 to 120 characters is required."));

        return Result<ExerciseName>.Success(new ExerciseName(code, name));
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return LanguageCode;
        yield return Value;
    }
}
```

`src/Modules/ExerciseCatalog/MyGym.Modules.ExerciseCatalog.Domain/Exercise.cs`:

```csharp
using System.Text.RegularExpressions;
using MyGym.SharedKernel;

namespace MyGym.Modules.ExerciseCatalog.Domain;

public sealed partial class Exercise : AggregateRoot<ExerciseId>
{
    public const string FallbackLanguage = "en";

    private readonly List<ExerciseName> _names = [];
    private readonly List<string> _aliases = [];

    private Exercise(
        ExerciseId id,
        string slug,
        MovementPattern movementPattern,
        IEnumerable<MuscleGroup> primaryMuscles,
        IEnumerable<MuscleGroup> secondaryMuscles,
        Equipment equipment,
        IEnumerable<ExerciseName> names,
        IEnumerable<string> aliases,
        UserId? ownerUserId) : base(id)
    {
        Slug = slug;
        MovementPattern = movementPattern;
        PrimaryMuscles = primaryMuscles.Distinct().ToList();
        SecondaryMuscles = secondaryMuscles.Distinct().ToList();
        Equipment = equipment;
        OwnerUserId = ownerUserId;
        _names.AddRange(names);
        _aliases.AddRange(aliases);
    }

    public string Slug { get; private set; }
    public MovementPattern MovementPattern { get; private set; }
    public IReadOnlyList<MuscleGroup> PrimaryMuscles { get; private set; }
    public IReadOnlyList<MuscleGroup> SecondaryMuscles { get; private set; }
    public Equipment Equipment { get; private set; }
    public UserId? OwnerUserId { get; private set; }
    public bool IsCustom => OwnerUserId is not null;

    public IReadOnlyList<ExerciseName> Names => _names;
    public IReadOnlyList<string> Aliases => _aliases;

    public static Result<Exercise> Create(
        string slug,
        MovementPattern movementPattern,
        IReadOnlyList<MuscleGroup> primaryMuscles,
        IReadOnlyList<MuscleGroup> secondaryMuscles,
        Equipment equipment,
        IReadOnlyList<ExerciseName> names,
        IReadOnlyList<string> aliases) =>
        CreateInternal(slug, movementPattern, primaryMuscles, secondaryMuscles, equipment,
            names, aliases, ownerUserId: null);

    public static Result<Exercise> CreateCustom(
        UserId ownerUserId,
        string slug,
        MovementPattern movementPattern,
        IReadOnlyList<MuscleGroup> primaryMuscles,
        IReadOnlyList<MuscleGroup> secondaryMuscles,
        Equipment equipment,
        IReadOnlyList<ExerciseName> names) =>
        CreateInternal(slug, movementPattern, primaryMuscles, secondaryMuscles, equipment,
            names, [], ownerUserId);

    public string NameFor(string languageCode)
    {
        var code = languageCode?.Trim().ToLowerInvariant() ?? FallbackLanguage;

        return _names.FirstOrDefault(n => n.LanguageCode == code)?.Value
            ?? _names.FirstOrDefault(n => n.LanguageCode == FallbackLanguage)?.Value
            ?? _names[0].Value;
    }

    private static Result<Exercise> CreateInternal(
        string slug,
        MovementPattern movementPattern,
        IReadOnlyList<MuscleGroup> primaryMuscles,
        IReadOnlyList<MuscleGroup> secondaryMuscles,
        Equipment equipment,
        IReadOnlyList<ExerciseName> names,
        IReadOnlyList<string> aliases,
        UserId? ownerUserId)
    {
        if (string.IsNullOrWhiteSpace(slug) || !SlugPattern().IsMatch(slug))
            return Result<Exercise>.Failure(new Error(
                CatalogErrors.SlugInvalid, "Slug must be lowercase kebab-case, e.g. back-squat."));

        if (names.Count == 0)
            return Result<Exercise>.Failure(new Error(
                CatalogErrors.NameRequired, "At least one name is required."));

        if (names.Select(n => n.LanguageCode).Distinct().Count() != names.Count)
            return Result<Exercise>.Failure(new Error(
                CatalogErrors.DuplicateLanguage, "Each language may appear at most once."));

        if (primaryMuscles.Count == 0)
            return Result<Exercise>.Failure(new Error(
                CatalogErrors.PrimaryMuscleRequired, "At least one primary muscle is required."));

        return Result<Exercise>.Success(new Exercise(
            ExerciseId.New(), slug, movementPattern, primaryMuscles, secondaryMuscles,
            equipment, names, aliases.Select(a => a.Trim().ToLowerInvariant()), ownerUserId));
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();
}
```

`src/Modules/ExerciseCatalog/MyGym.Modules.ExerciseCatalog.Domain/IExerciseRepository.cs`:

```csharp
using MyGym.SharedKernel;

namespace MyGym.Modules.ExerciseCatalog.Domain;

public interface IExerciseRepository
{
    Task<Exercise?> GetByIdAsync(ExerciseId id, CancellationToken ct);
    Task<Exercise?> GetBySlugAsync(string slug, CancellationToken ct);
    Task<bool> ExistsAsync(ExerciseId id, CancellationToken ct);
    Task AddAsync(Exercise exercise, CancellationToken ct);
}
```

- [ ] **Step 5: Write the seed catalog**

`src/Modules/ExerciseCatalog/MyGym.Modules.ExerciseCatalog.Domain/CatalogSeed.cs`:

```csharp
namespace MyGym.Modules.ExerciseCatalog.Domain;

/// <summary>
/// The starting catalog: every exercise in the author's own 3-day program, so the app is usable
/// on day one. Names are English and Uzbek; Russian can be added without code changes.
/// </summary>
public static class CatalogSeed
{
    private static ExerciseName En(string value) => ExerciseName.Create("en", value).Value;
    private static ExerciseName Uz(string value) => ExerciseName.Create("uz", value).Value;

    private static Exercise Make(
        string slug, MovementPattern pattern, MuscleGroup[] primary, MuscleGroup[] secondary,
        Equipment equipment, string en, string uz, params string[] aliases) =>
        Exercise.Create(slug, pattern, primary, secondary, equipment, [En(en), Uz(uz)], aliases).Value;

    public static IReadOnlyList<Exercise> Exercises { get; } =
    [
        Make("incline-dumbbell-press", MovementPattern.HorizontalPush,
            [MuscleGroup.Chest], [MuscleGroup.Shoulders, MuscleGroup.Triceps], Equipment.Dumbbell,
            "Incline Dumbbell Press", "Qiya gantel press", "incline db press"),
        Make("flat-barbell-bench-press", MovementPattern.HorizontalPush,
            [MuscleGroup.Chest], [MuscleGroup.Shoulders, MuscleGroup.Triceps], Equipment.Barbell,
            "Flat Barbell Bench Press", "Gorizontal shtanga press", "bench press", "bench"),
        Make("machine-chest-press", MovementPattern.HorizontalPush,
            [MuscleGroup.Chest], [MuscleGroup.Triceps], Equipment.Machine,
            "Machine Chest Press", "Trenajyorda ko'krak press"),
        Make("pec-deck", MovementPattern.Isolation,
            [MuscleGroup.Chest], [], Equipment.Machine,
            "Pec Deck", "Pek-dek", "butterfly"),
        Make("incline-cable-fly", MovementPattern.Isolation,
            [MuscleGroup.Chest], [], Equipment.Cable,
            "Incline Cable Fly", "Qiya bloklarda yoyish"),
        Make("chest-supported-row", MovementPattern.HorizontalPull,
            [MuscleGroup.Back], [MuscleGroup.Lats, MuscleGroup.Biceps], Equipment.Machine,
            "Chest Supported Row", "Ko'krak tayanchli tortish"),
        Make("seated-cable-row", MovementPattern.HorizontalPull,
            [MuscleGroup.Back], [MuscleGroup.Lats, MuscleGroup.Biceps], Equipment.Cable,
            "Seated Cable Row", "O'tirgan holda blok tortish"),
        Make("lat-pulldown", MovementPattern.VerticalPull,
            [MuscleGroup.Lats], [MuscleGroup.Biceps], Equipment.Cable,
            "Lat Pulldown", "Yuqori blok tortish"),
        Make("pull-up", MovementPattern.VerticalPull,
            [MuscleGroup.Lats], [MuscleGroup.Biceps], Equipment.Bodyweight,
            "Pull-up", "Turnikda tortilish", "chin up"),
        Make("seated-dumbbell-shoulder-press", MovementPattern.VerticalPush,
            [MuscleGroup.Shoulders], [MuscleGroup.Triceps], Equipment.Dumbbell,
            "Seated Dumbbell Shoulder Press", "O'tirgan holda gantel press"),
        Make("dumbbell-lateral-raise", MovementPattern.Isolation,
            [MuscleGroup.Shoulders], [], Equipment.Dumbbell,
            "Dumbbell Lateral Raise", "Gantelni yonga ko'tarish", "lateral raise", "side raise"),
        Make("rear-delt-fly", MovementPattern.Isolation,
            [MuscleGroup.Shoulders], [MuscleGroup.Traps], Equipment.Dumbbell,
            "Rear Delt Fly", "Orqa delta yoyish"),
        Make("ez-bar-curl", MovementPattern.Isolation,
            [MuscleGroup.Biceps], [MuscleGroup.Forearms], Equipment.EzBar,
            "EZ Bar Curl", "EZ shtanga bitseps"),
        Make("incline-dumbbell-curl", MovementPattern.Isolation,
            [MuscleGroup.Biceps], [], Equipment.Dumbbell,
            "Incline Dumbbell Curl", "Qiya gantel bitseps"),
        Make("rope-triceps-pushdown", MovementPattern.Isolation,
            [MuscleGroup.Triceps], [], Equipment.Cable,
            "Rope Triceps Pushdown", "Arqon bilan tritseps"),
        Make("overhead-ez-bar-triceps-extension", MovementPattern.Isolation,
            [MuscleGroup.Triceps], [], Equipment.EzBar,
            "Overhead EZ-Bar Triceps Extension", "Bosh ustidan EZ tritseps"),
        Make("overhead-rope-triceps-extension", MovementPattern.Isolation,
            [MuscleGroup.Triceps], [], Equipment.Cable,
            "Overhead Rope Triceps Extension", "Bosh ustidan arqon tritseps"),
        Make("back-squat", MovementPattern.Squat,
            [MuscleGroup.Quads], [MuscleGroup.Glutes, MuscleGroup.Hamstrings], Equipment.Barbell,
            "Back Squat", "Shtanga bilan o'tirib turish", "squat"),
        Make("leg-press", MovementPattern.Squat,
            [MuscleGroup.Quads], [MuscleGroup.Glutes], Equipment.Machine,
            "Leg Press", "Oyoq press"),
        Make("bulgarian-split-squat", MovementPattern.Lunge,
            [MuscleGroup.Quads], [MuscleGroup.Glutes], Equipment.Dumbbell,
            "Bulgarian Split Squat", "Bolgar split prisedaniya"),
        Make("walking-lunge", MovementPattern.Lunge,
            [MuscleGroup.Quads], [MuscleGroup.Glutes], Equipment.Dumbbell,
            "Walking Lunge", "Yurib qadamlash"),
        Make("romanian-deadlift", MovementPattern.Hinge,
            [MuscleGroup.Hamstrings], [MuscleGroup.Glutes, MuscleGroup.Back], Equipment.Barbell,
            "Romanian Deadlift", "Rumin tortish", "rdl"),
        Make("leg-curl", MovementPattern.Isolation,
            [MuscleGroup.Hamstrings], [], Equipment.Machine,
            "Leg Curl", "Oyoq bukish"),
        Make("standing-calf-raise", MovementPattern.Isolation,
            [MuscleGroup.Calves], [], Equipment.Machine,
            "Standing Calf Raise", "Tik turib boldir ko'tarish"),
        Make("hanging-leg-raise", MovementPattern.Core,
            [MuscleGroup.Abs], [], Equipment.Bodyweight,
            "Hanging Leg Raise", "Osilib oyoq ko'tarish"),
        Make("cable-crunch", MovementPattern.Core,
            [MuscleGroup.Abs], [], Equipment.Cable,
            "Cable Crunch", "Blokda qorin bukish")
    ];
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/MyGym.Modules.ExerciseCatalog.Domain.Tests`
Expected: PASS — 14 tests. If `CatalogSeedTests.Every_seeded_exercise_is_valid` reports a count other than 26, a `Make(...)` call threw while building the static list; the exception message names the failing slug.

- [ ] **Step 7: Commit**

```bash
git add src/Modules/ExerciseCatalog tests/MyGym.Modules.ExerciseCatalog.Domain.Tests
git commit -m "feat: add ExerciseCatalog module with multilingual names and seed catalog"
```

---

### Task 8: Training persistence and the database-enforced invariants

The two invariants the domain cannot enforce alone — one in-progress session per athlete, and
idempotent set logging — become real here, as PostgreSQL indexes. The tests in this task run
against a real database for exactly that reason.

**Files:**
- Create: `src/Shared/MyGym.Shared.Infrastructure/Persistence/{ModuleDbContext,IUnitOfWork,DomainEventDispatcher,TypedIdConverters,TrainingValueConverters}.cs`
- Create: `src/Modules/Training/MyGym.Modules.Training.Infrastructure/{TrainingDbContext,TrainingProgramConfiguration,WorkoutSessionConfiguration,TrainingProgramRepository,WorkoutSessionRepository,TrainingInfrastructureExtensions}.cs`
- Create: `src/Modules/Training/MyGym.Modules.Training.Domain/ITrainingUnitOfWork.cs`
- Test: `tests/MyGym.IntegrationTests/{PostgresFixture,TrainingPersistenceTests}.cs`

**Interfaces:**
- Consumes: both Training aggregates and their repository interfaces (Tasks 4–5).
- Produces:
  - `IUnitOfWork` with `SaveChangesAsync(CancellationToken) → Task<int>`; `ITrainingUnitOfWork : IUnitOfWork`
  - `IDomainEventHandler<TEvent>` with `HandleAsync(TEvent, CancellationToken)`; `IDomainEventDispatcher` with `DispatchAsync(IReadOnlyList<IDomainEvent>, CancellationToken)`
  - `ModuleDbContext` — base class that collects and dispatches domain events inside the save transaction
  - `TrainingDbContext` with `DbSet<TrainingProgram> Programs`, `DbSet<WorkoutSession> Sessions`
  - `TrainingProgramRepository`, `WorkoutSessionRepository`
  - `TrainingInfrastructureExtensions.AddTrainingInfrastructure(IServiceCollection, string connectionString)`
  - `PostgresFixture` with `ConnectionString`, `CreateTrainingContext()`, `ResetAsync()`

- [ ] **Step 1: Add packages and create the projects**

Add to `Directory.Packages.props`:

```xml
<PackageVersion Include="EFCore.NamingConventions" Version="10.0.0" />
```

```bash
dotnet new classlib -o src/Shared/MyGym.Shared.Infrastructure
dotnet new classlib -o src/Modules/Training/MyGym.Modules.Training.Infrastructure
dotnet new xunit3 -o tests/MyGym.IntegrationTests
dotnet sln add src/Shared/MyGym.Shared.Infrastructure \
               src/Modules/Training/MyGym.Modules.Training.Infrastructure \
               tests/MyGym.IntegrationTests

dotnet add src/Shared/MyGym.Shared.Infrastructure reference src/Shared/MyGym.SharedKernel
dotnet add src/Shared/MyGym.Shared.Infrastructure package Microsoft.EntityFrameworkCore
dotnet add src/Shared/MyGym.Shared.Infrastructure package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add src/Shared/MyGym.Shared.Infrastructure package EFCore.NamingConventions
dotnet add src/Shared/MyGym.Shared.Infrastructure reference src/Modules/Training/MyGym.Modules.Training.Domain

dotnet add src/Modules/Training/MyGym.Modules.Training.Infrastructure reference \
           src/Modules/Training/MyGym.Modules.Training.Domain src/Shared/MyGym.Shared.Infrastructure
dotnet add src/Modules/Training/MyGym.Modules.Training.Infrastructure package Microsoft.EntityFrameworkCore.Design

dotnet add tests/MyGym.IntegrationTests reference src/Modules/Training/MyGym.Modules.Training.Infrastructure
dotnet add tests/MyGym.IntegrationTests package Testcontainers.PostgreSql
dotnet add tests/MyGym.IntegrationTests package Shouldly
rm src/Shared/MyGym.Shared.Infrastructure/Class1.cs \
   src/Modules/Training/MyGym.Modules.Training.Infrastructure/Class1.cs
```

> `MyGym.Shared.Infrastructure` referencing `Training.Domain` is a deliberate, temporary shortcut so
> the value converters for `WarmupRamp`, `RepRange`, and `RestPeriod` live in one place. Task 15's
> architecture test pins the rule that no *module* references another module's internals; shared
> infrastructure sits below the modules and is exempt. If a second module ever needs these
> converters, move them into `SharedKernel` instead of widening this reference.

- [ ] **Step 2: Write the failing integration tests**

`tests/MyGym.IntegrationTests/PostgresFixture.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using MyGym.Modules.Training.Infrastructure;
using Testcontainers.PostgreSql;

namespace MyGym.IntegrationTests;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("mygym_test")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await using var context = CreateTrainingContext();
        await context.Database.MigrateAsync();
    }

    public TrainingDbContext CreateTrainingContext()
    {
        var options = new DbContextOptionsBuilder<TrainingDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new TrainingDbContext(options, dispatcher: null);
    }

    public async Task ResetAsync()
    {
        await using var context = CreateTrainingContext();
        await context.Database.ExecuteSqlRawAsync(
            "TRUNCATE training.sessions, training.programs CASCADE;");
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}

[CollectionDefinition(nameof(PostgresCollection))]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
```

`tests/MyGym.IntegrationTests/TrainingPersistenceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using MyGym.Modules.Training.Domain.Programs;
using MyGym.Modules.Training.Domain.Sessions;
using MyGym.Modules.Training.Domain.ValueObjects;
using MyGym.SharedKernel;
using Shouldly;

namespace MyGym.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public class TrainingPersistenceTests(PostgresFixture fixture) : IAsyncLifetime
{
    private static readonly DateTimeOffset Start = new(2026, 11, 23, 18, 0, 0, TimeSpan.Zero);

    public async ValueTask InitializeAsync() => await fixture.ResetAsync();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static WarmupRamp BenchRamp() => WarmupRamp.Create(
    [
        WarmupStep.Create(0.27m, 10, false).Value,
        WarmupStep.Create(0.53m, 6, false).Value,
        WarmupStep.Create(0.80m, 3, true).Value
    ]).Value;

    private static TrainingProgram BuildProgram(UserId owner)
    {
        var program = TrainingProgram.Create(owner, "3 Kunlik Muscle Gain").Value;
        var day = program.AddDay("Dushanba — Upper Body", DayOfWeek.Monday).Value;
        program.AddPrescription(
            day.Id, ExerciseId.New(), ExerciseId.New(), 3,
            RepRange.Create(6, 8).Value,
            RestPeriod.Create(TimeSpan.FromMinutes(2.5), TimeSpan.FromMinutes(3)).Value,
            BenchRamp(), "Keep elbows tucked");
        return program;
    }

    [Fact]
    public async Task A_program_round_trips_with_its_days_prescriptions_and_warmup_ramp()
    {
        var program = BuildProgram(UserId.New());

        await using (var write = fixture.CreateTrainingContext())
        {
            write.Programs.Add(program);
            await write.SaveChangesAsync();
        }

        await using var read = fixture.CreateTrainingContext();
        var loaded = await read.Programs.SingleAsync(p => p.Id == program.Id);

        loaded.Name.ShouldBe("3 Kunlik Muscle Gain");
        loaded.Days.ShouldHaveSingleItem();
        var prescription = loaded.Days[0].Prescriptions.ShouldHaveSingleItem();
        prescription.TargetReps.ShouldBe(RepRange.Create(6, 8).Value);
        prescription.Rest.Max.ShouldBe(TimeSpan.FromMinutes(3));
        prescription.Notes.ShouldBe("Keep elbows tucked");
        prescription.Warmup.Steps.Count.ShouldBe(3);
        prescription.Warmup.Steps[2].PercentOfWorking.ShouldBe(0.80m);
        prescription.Warmup.Steps[2].IsOptional.ShouldBeTrue();
    }

    [Fact]
    public async Task Aggregate_children_load_without_an_explicit_include()
    {
        var program = BuildProgram(UserId.New());
        await using (var write = fixture.CreateTrainingContext())
        {
            write.Programs.Add(program);
            await write.SaveChangesAsync();
        }

        await using var read = fixture.CreateTrainingContext();
        var loaded = await read.Programs.AsNoTracking().SingleAsync(p => p.Id == program.Id);

        loaded.Days[0].Prescriptions.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Editing_a_program_does_not_change_an_already_logged_session()
    {
        var owner = UserId.New();
        var program = BuildProgram(owner);
        var prescription = program.Days[0].Prescriptions[0];

        var snapshot = PrescriptionSnapshot.Create(
            prescription.ExerciseId, prescription.AlternativeExerciseId, prescription.WorkingSetCount,
            prescription.TargetReps, prescription.Rest, prescription.Warmup, prescription.Notes).Value;

        var session = WorkoutSession.Start(
            owner,
            SessionOrigin.FromProgram(program.Id, program.RevisionNumber, program.Days[0].Id),
            [snapshot],
            Start).Value;
        session.LogSet(session.Exercises[0].Id, Guid.NewGuid(), SetKind.Working, Weight.Kg(27.5m), 8, 2, Start);
        session.Complete(Start.AddHours(1));

        await using (var write = fixture.CreateTrainingContext())
        {
            write.Programs.Add(program);
            write.Sessions.Add(session);
            await write.SaveChangesAsync();
        }

        // Now change the program the way an athlete would in a later block.
        await using (var edit = fixture.CreateTrainingContext())
        {
            var loaded = await edit.Programs.SingleAsync(p => p.Id == program.Id);
            var day = loaded.Days[0];
            loaded.AddPrescription(
                day.Id, ExerciseId.New(), null, 5,
                RepRange.Create(12, 15).Value,
                RestPeriod.Create(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2)).Value,
                WarmupRamp.Empty, null);
            await edit.SaveChangesAsync();
        }

        await using var read = fixture.CreateTrainingContext();
        var storedSession = await read.Sessions.AsNoTracking().SingleAsync(s => s.Id == session.Id);

        storedSession.Exercises.ShouldHaveSingleItem();
        storedSession.Exercises[0].Prescription.TargetReps.ShouldBe(RepRange.Create(6, 8).Value);
        storedSession.Exercises[0].Prescription.WorkingSetCount.ShouldBe(3);
        storedSession.Origin.ProgramRevision.ShouldBe(program.RevisionNumber);
        storedSession.WorkingSetCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_logged_set_round_trips_its_weight_reps_and_client_set_id()
    {
        var session = AdHocSessionWithOneSet(out var clientSetId);

        await using (var write = fixture.CreateTrainingContext())
        {
            write.Sessions.Add(session);
            await write.SaveChangesAsync();
        }

        await using var read = fixture.CreateTrainingContext();
        var loaded = await read.Sessions.AsNoTracking().SingleAsync(s => s.Id == session.Id);
        var set = loaded.Exercises[0].Sets.ShouldHaveSingleItem();

        set.Weight.Value.ShouldBe(27.5m);
        set.Weight.Unit.ShouldBe(WeightUnit.Kilograms);
        set.Reps.ShouldBe(8);
        set.Rir.ShouldBe(2);
        set.ClientSetId.ShouldBe(clientSetId);
    }

    [Fact]
    public async Task The_database_refuses_a_second_in_progress_session_for_the_same_athlete()
    {
        var athlete = UserId.New();
        var first = WorkoutSession.Start(athlete, SessionOrigin.AdHoc, [], Start).Value;
        var second = WorkoutSession.Start(athlete, SessionOrigin.AdHoc, [], Start.AddMinutes(1)).Value;

        await using var context = fixture.CreateTrainingContext();
        context.Sessions.Add(first);
        await context.SaveChangesAsync();

        context.Sessions.Add(second);
        var exception = await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync());
        exception.InnerException!.Message.ShouldContain("ix_sessions_user_id_in_progress");
    }

    [Fact]
    public async Task A_completed_session_no_longer_blocks_a_new_one()
    {
        var athlete = UserId.New();
        var first = WorkoutSession.Start(athlete, SessionOrigin.AdHoc, [], Start).Value;
        first.AddExercise(AnySnapshot());
        first.LogSet(first.Exercises[0].Id, Guid.NewGuid(), SetKind.Working, Weight.Kg(50m), 5, null, Start);
        first.Complete(Start.AddHours(1));

        await using var context = fixture.CreateTrainingContext();
        context.Sessions.Add(first);
        await context.SaveChangesAsync();

        context.Sessions.Add(WorkoutSession.Start(athlete, SessionOrigin.AdHoc, [], Start.AddDays(2)).Value);
        await Should.NotThrowAsync(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task The_database_refuses_a_duplicate_client_set_id_within_one_exercise()
    {
        var session = AdHocSessionWithOneSet(out var clientSetId);

        await using var context = fixture.CreateTrainingContext();
        context.Sessions.Add(session);
        await context.SaveChangesAsync();

        // Simulate a buggy write path bypassing the aggregate's idempotency check.
        var exerciseId = session.Exercises[0].Id.Value;
        var exception = await Should.ThrowAsync<Npgsql.PostgresException>(() =>
            context.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO training.logged_sets
                    (id, session_exercise_id, "order", kind, weight_value, weight_unit, reps, rir,
                     client_set_id, logged_at)
                VALUES
                    ({0}, {1}, 2, 2, 30.0, 1, 8, NULL, {2}, now());
                """,
                Guid.CreateVersion7(), exerciseId, clientSetId));

        exception.ConstraintName.ShouldBe("ix_logged_sets_session_exercise_id_client_set_id");
    }

    private static PrescriptionSnapshot AnySnapshot() =>
        PrescriptionSnapshot.Create(
            ExerciseId.New(), null, 3,
            RepRange.Create(6, 8).Value,
            RestPeriod.Create(TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(3)).Value,
            WarmupRamp.Empty, null).Value;

    private static WorkoutSession AdHocSessionWithOneSet(out Guid clientSetId)
    {
        clientSetId = Guid.NewGuid();
        var session = WorkoutSession.Start(UserId.New(), SessionOrigin.AdHoc, [], Start).Value;
        session.AddExercise(AnySnapshot());
        session.LogSet(
            session.Exercises[0].Id, clientSetId, SetKind.Working, Weight.Kg(27.5m), 8, 2, Start);
        return session;
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/MyGym.IntegrationTests`
Expected: FAIL — `TrainingDbContext` does not exist. (Docker must be running; Testcontainers starts `postgres:17-alpine` on first use.)

- [ ] **Step 4: Write the shared persistence plumbing**

`src/Shared/MyGym.Shared.Infrastructure/Persistence/IUnitOfWork.cs`:

```csharp
namespace MyGym.Shared.Infrastructure.Persistence;

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct);
}
```

`src/Shared/MyGym.Shared.Infrastructure/Persistence/DomainEventDispatcher.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using MyGym.SharedKernel;

namespace MyGym.Shared.Infrastructure.Persistence;

public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken ct);
}

public interface IDomainEventDispatcher
{
    Task DispatchAsync(IReadOnlyList<IDomainEvent> events, CancellationToken ct);
}

/// <summary>
/// In-process dispatch. Sub-project 1 has no subscribers; the dispatcher exists so events are
/// delivered rather than dropped, and so sub-project 2 can subscribe without touching this code.
/// A transactional outbox replaces this when the first cross-module subscriber appears.
/// </summary>
public sealed class DomainEventDispatcher(IServiceProvider services) : IDomainEventDispatcher
{
    public async Task DispatchAsync(IReadOnlyList<IDomainEvent> events, CancellationToken ct)
    {
        foreach (var domainEvent in events)
        {
            var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(domainEvent.GetType());
            foreach (var handler in services.GetServices(handlerType))
            {
                if (handler is null)
                    continue;

                var method = handlerType.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.HandleAsync))!;
                await (Task)method.Invoke(handler, [domainEvent, ct])!;
            }
        }
    }
}
```

`src/Shared/MyGym.Shared.Infrastructure/Persistence/ModuleDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using MyGym.SharedKernel;

namespace MyGym.Shared.Infrastructure.Persistence;

/// <summary>
/// Base for every module's context. Saves and then dispatches the domain events raised during the
/// change, both inside one transaction, so a failing subscriber rolls the write back.
/// </summary>
public abstract class ModuleDbContext(DbContextOptions options, IDomainEventDispatcher? dispatcher)
    : DbContext(options), IUnitOfWork
{
    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        var roots = ChangeTracker.Entries()
            .Select(e => e.Entity)
            .OfType<IHasDomainEvents>()
            .Where(e => e.DomainEvents.Count > 0)
            .ToList();

        var events = roots.SelectMany(r => r.DomainEvents).ToList();

        if (dispatcher is null || events.Count == 0)
        {
            foreach (var root in roots)
                root.ClearDomainEvents();

            return await base.SaveChangesAsync(ct);
        }

        await using var transaction = await Database.BeginTransactionAsync(ct);

        var written = await base.SaveChangesAsync(ct);
        foreach (var root in roots)
            root.ClearDomainEvents();

        await dispatcher.DispatchAsync(events, ct);
        await transaction.CommitAsync(ct);

        return written;
    }
}
```

Add the marker interface to `src/Shared/MyGym.SharedKernel/AggregateRoot.cs` so the context can find
roots without knowing their id type:

```csharp
namespace MyGym.SharedKernel;

public interface IHasDomainEvents
{
    IReadOnlyList<IDomainEvent> DomainEvents { get; }
    void ClearDomainEvents();
}

public abstract class AggregateRoot<TId>(TId id) : Entity<TId>(id), IHasDomainEvents
    where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
```

`src/Shared/MyGym.Shared.Infrastructure/Persistence/TypedIdConverters.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using MyGym.SharedKernel;

namespace MyGym.Shared.Infrastructure.Persistence;

public sealed class UserIdConverter() : ValueConverter<UserId, Guid>(id => id.Value, v => UserId.From(v));
public sealed class ProgramIdConverter() : ValueConverter<ProgramId, Guid>(id => id.Value, v => ProgramId.From(v));
public sealed class ProgramDayIdConverter() : ValueConverter<ProgramDayId, Guid>(id => id.Value, v => ProgramDayId.From(v));
public sealed class PrescriptionIdConverter() : ValueConverter<PrescriptionId, Guid>(id => id.Value, v => PrescriptionId.From(v));
public sealed class SessionIdConverter() : ValueConverter<SessionId, Guid>(id => id.Value, v => SessionId.From(v));
public sealed class SessionExerciseIdConverter() : ValueConverter<SessionExerciseId, Guid>(id => id.Value, v => SessionExerciseId.From(v));
public sealed class LoggedSetIdConverter() : ValueConverter<LoggedSetId, Guid>(id => id.Value, v => LoggedSetId.From(v));
public sealed class ExerciseIdConverter() : ValueConverter<ExerciseId, Guid>(id => id.Value, v => ExerciseId.From(v));

public static class TypedIdConventions
{
    /// <summary>Call from every module context's ConfigureConventions.</summary>
    public static void AddTypedIds(this ModelConfigurationBuilder builder)
    {
        builder.Properties<UserId>().HaveConversion<UserIdConverter>();
        builder.Properties<ProgramId>().HaveConversion<ProgramIdConverter>();
        builder.Properties<ProgramDayId>().HaveConversion<ProgramDayIdConverter>();
        builder.Properties<PrescriptionId>().HaveConversion<PrescriptionIdConverter>();
        builder.Properties<SessionId>().HaveConversion<SessionIdConverter>();
        builder.Properties<SessionExerciseId>().HaveConversion<SessionExerciseIdConverter>();
        builder.Properties<LoggedSetId>().HaveConversion<LoggedSetIdConverter>();
        builder.Properties<ExerciseId>().HaveConversion<ExerciseIdConverter>();
    }
}
```

`src/Shared/MyGym.Shared.Infrastructure/Persistence/TrainingValueConverters.cs`:

```csharp
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
```

- [ ] **Step 5: Write the Training context and configurations**

`src/Modules/Training/MyGym.Modules.Training.Domain/ITrainingUnitOfWork.cs`:

```csharp
namespace MyGym.Modules.Training.Domain;

/// <summary>
/// Declared in Domain so application handlers depend on an abstraction, not on EF Core.
/// Implemented by TrainingDbContext.
/// </summary>
public interface ITrainingUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct);
}
```

`src/Modules/Training/MyGym.Modules.Training.Infrastructure/TrainingDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using MyGym.Modules.Training.Domain;
using MyGym.Modules.Training.Domain.Programs;
using MyGym.Modules.Training.Domain.Sessions;
using MyGym.Modules.Training.Domain.ValueObjects;
using MyGym.Shared.Infrastructure.Persistence;

namespace MyGym.Modules.Training.Infrastructure;

public sealed class TrainingDbContext(DbContextOptions<TrainingDbContext> options, IDomainEventDispatcher? dispatcher)
    : ModuleDbContext(options, dispatcher), ITrainingUnitOfWork
{
    public const string Schema = "training";

    public DbSet<TrainingProgram> Programs => Set<TrainingProgram>();
    public DbSet<WorkoutSession> Sessions => Set<WorkoutSession>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfiguration(new TrainingProgramConfiguration());
        modelBuilder.ApplyConfiguration(new WorkoutSessionConfiguration());
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddTypedIds();
        configurationBuilder.Properties<RepRange>().HaveConversion<RepRangeConverter>();
        configurationBuilder.Properties<RestPeriod>().HaveConversion<RestPeriodConverter>();
        configurationBuilder.Properties<WarmupRamp>()
            .HaveConversion<WarmupRampConverter, WarmupRampComparer>()
            .HaveColumnType("jsonb");
    }
}
```

`src/Modules/Training/MyGym.Modules.Training.Infrastructure/TrainingProgramConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyGym.Modules.Training.Domain.Programs;

namespace MyGym.Modules.Training.Infrastructure;

internal sealed class TrainingProgramConfiguration : IEntityTypeConfiguration<TrainingProgram>
{
    public void Configure(EntityTypeBuilder<TrainingProgram> builder)
    {
        builder.ToTable("programs");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).HasMaxLength(TrainingProgram.MaxNameLength).IsRequired();
        builder.Property(p => p.Status).HasConversion<int>();
        builder.Property(p => p.Visibility).HasConversion<int>();
        builder.Property(p => p.RevisionNumber).IsConcurrencyToken();
        builder.HasIndex(p => p.OwnerUserId);

        // Days and prescriptions are owned: they load with the root automatically, which is exactly
        // aggregate semantics and removes any chance of evaluating invariants on a half-loaded graph.
        builder.OwnsMany<ProgramDay>("_days", day =>
        {
            day.ToTable("program_days");
            day.WithOwner().HasForeignKey(d => d.ProgramId);
            day.HasKey(d => d.Id);
            day.Property(d => d.Label).HasMaxLength(120).IsRequired();
            day.Property(d => d.Order).HasColumnName("order");
            day.HasIndex(d => new { d.ProgramId, d.Order }).IsUnique();

            day.OwnsMany<ExercisePrescription>("_prescriptions", prescription =>
            {
                prescription.ToTable("exercise_prescriptions");
                prescription.WithOwner().HasForeignKey(p => p.ProgramDayId);
                prescription.HasKey(p => p.Id);
                prescription.Property(p => p.Order).HasColumnName("order");
                prescription.Property(p => p.Notes).HasMaxLength(500);
                prescription.HasIndex(p => new { p.ProgramDayId, p.Order }).IsUnique();
            });
        });

        builder.Navigation("_days").AutoInclude();
    }
}
```

`src/Modules/Training/MyGym.Modules.Training.Infrastructure/WorkoutSessionConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyGym.Modules.Training.Domain.Sessions;
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Infrastructure;

internal sealed class WorkoutSessionConfiguration : IEntityTypeConfiguration<WorkoutSession>
{
    public const string InProgressIndexName = "ix_sessions_user_id_in_progress";
    public const string ClientSetIdIndexName = "ix_logged_sets_session_exercise_id_client_set_id";

    public void Configure(EntityTypeBuilder<WorkoutSession> builder)
    {
        builder.ToTable("sessions");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Status).HasConversion<int>();
        builder.HasIndex(s => new { s.UserId, s.StartedAt });

        // Invariant: at most one in-progress session per athlete. The aggregate cannot see its
        // siblings, so the database is the only place this can be guaranteed under concurrency.
        builder.HasIndex(s => s.UserId)
            .HasDatabaseName(InProgressIndexName)
            .HasFilter($"status = {(int)SessionStatus.InProgress}")
            .IsUnique();

        builder.OwnsOne(s => s.Origin, origin =>
        {
            origin.Property(o => o.ProgramId).HasColumnName("program_id");
            origin.Property(o => o.ProgramRevision).HasColumnName("program_revision");
            origin.Property(o => o.ProgramDayId).HasColumnName("program_day_id");
        });

        builder.OwnsOne(s => s.BodyweightAtSession, weight =>
        {
            weight.Property(w => w.Value).HasColumnName("bodyweight_value").HasPrecision(6, 2);
            weight.Property(w => w.Unit).HasColumnName("bodyweight_unit").HasConversion<int>();
        });

        builder.OwnsMany<SessionExercise>("_exercises", exercise =>
        {
            exercise.ToTable("session_exercises");
            exercise.WithOwner().HasForeignKey(e => e.SessionId);
            exercise.HasKey(e => e.Id);
            exercise.Property(e => e.Order).HasColumnName("order");
            exercise.Ignore(e => e.ExerciseId);  // derived from the snapshot
            exercise.HasIndex(e => new { e.SessionId, e.Order }).IsUnique();

            exercise.OwnsOne(e => e.Prescription, snapshot =>
            {
                snapshot.Property(p => p.ExerciseId).HasColumnName("exercise_id");
                snapshot.Property(p => p.AlternativeExerciseId).HasColumnName("alternative_exercise_id");
                snapshot.Property(p => p.WorkingSetCount).HasColumnName("working_set_count");
                snapshot.Property(p => p.TargetReps).HasColumnName("target_reps");
                snapshot.Property(p => p.Rest).HasColumnName("rest");
                snapshot.Property(p => p.Warmup).HasColumnName("warmup").HasColumnType("jsonb");
                snapshot.Property(p => p.Notes).HasColumnName("notes").HasMaxLength(500);
            });

            exercise.OwnsMany<LoggedSet>("_sets", set =>
            {
                set.ToTable("logged_sets");
                set.WithOwner().HasForeignKey(s => s.SessionExerciseId);
                set.HasKey(s => s.Id);
                set.Property(s => s.Order).HasColumnName("order");
                set.Property(s => s.Kind).HasConversion<int>();

                set.OwnsOne(s => s.Weight, weight =>
                {
                    weight.Property(w => w.Value).HasColumnName("weight_value").HasPrecision(7, 2);
                    weight.Property(w => w.Unit).HasColumnName("weight_unit").HasConversion<int>();
                });

                // Invariant: logging a set is idempotent on the client-generated id.
                set.HasIndex(s => new { s.SessionExerciseId, s.ClientSetId })
                    .HasDatabaseName(ClientSetIdIndexName)
                    .IsUnique();
            });
        });

        builder.Navigation("_exercises").AutoInclude();
    }
}
```

> `OwnsOne(s => s.Weight, ...)` requires EF Core to see a non-nullable owned reference. `Weight` is
> a class with a private constructor whose parameter names match its properties (`value`, `unit`),
> which EF binds automatically. If EF reports it cannot find a constructor, add
> `weight.HasNoKey()`-free explicit mapping is not the fix — verify the parameter names match the
> property names exactly, case-insensitively.

- [ ] **Step 6: Write the repositories and DI extension**

`src/Modules/Training/MyGym.Modules.Training.Infrastructure/TrainingProgramRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using MyGym.Modules.Training.Domain.Programs;
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Infrastructure;

internal sealed class TrainingProgramRepository(TrainingDbContext context) : ITrainingProgramRepository
{
    public Task<TrainingProgram?> GetByIdAsync(ProgramId id, CancellationToken ct) =>
        context.Programs.SingleOrDefaultAsync(p => p.Id == id, ct);

    public async Task AddAsync(TrainingProgram program, CancellationToken ct) =>
        await context.Programs.AddAsync(program, ct);

    public void Remove(TrainingProgram program) => context.Programs.Remove(program);
}
```

`src/Modules/Training/MyGym.Modules.Training.Infrastructure/WorkoutSessionRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using MyGym.Modules.Training.Domain.Sessions;
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Infrastructure;

internal sealed class WorkoutSessionRepository(TrainingDbContext context) : IWorkoutSessionRepository
{
    public Task<WorkoutSession?> GetByIdAsync(SessionId id, CancellationToken ct) =>
        context.Sessions.SingleOrDefaultAsync(s => s.Id == id, ct);

    public Task<WorkoutSession?> GetInProgressForUserAsync(UserId userId, CancellationToken ct) =>
        context.Sessions.SingleOrDefaultAsync(
            s => s.UserId == userId && s.Status == SessionStatus.InProgress, ct);

    public async Task AddAsync(WorkoutSession session, CancellationToken ct) =>
        await context.Sessions.AddAsync(session, ct);
}
```

`src/Modules/Training/MyGym.Modules.Training.Infrastructure/TrainingInfrastructureExtensions.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyGym.Modules.Training.Domain;
using MyGym.Modules.Training.Domain.Programs;
using MyGym.Modules.Training.Domain.Sessions;

namespace MyGym.Modules.Training.Infrastructure;

public static class TrainingInfrastructureExtensions
{
    public static IServiceCollection AddTrainingInfrastructure(
        this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<TrainingDbContext>(options => options
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", TrainingDbContext.Schema))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<ITrainingProgramRepository, TrainingProgramRepository>();
        services.AddScoped<IWorkoutSessionRepository, WorkoutSessionRepository>();
        services.AddScoped<ITrainingUnitOfWork>(sp => sp.GetRequiredService<TrainingDbContext>());

        return services;
    }
}
```

- [ ] **Step 7: Create the migration**

```bash
dotnet tool install --global dotnet-ef --version 10.0.0
dotnet ef migrations add InitialTraining \
  --project src/Modules/Training/MyGym.Modules.Training.Infrastructure \
  --startup-project tests/MyGym.IntegrationTests \
  --context TrainingDbContext \
  --output-dir Migrations
```

> If `dotnet ef` cannot find a startup project with a service provider, add a design-time factory at
> `src/Modules/Training/MyGym.Modules.Training.Infrastructure/TrainingDbContextFactory.cs`:
>
> ```csharp
> using Microsoft.EntityFrameworkCore;
> using Microsoft.EntityFrameworkCore.Design;
>
> namespace MyGym.Modules.Training.Infrastructure;
>
> public sealed class TrainingDbContextFactory : IDesignTimeDbContextFactory<TrainingDbContext>
> {
>     public TrainingDbContext CreateDbContext(string[] args)
>     {
>         var options = new DbContextOptionsBuilder<TrainingDbContext>()
>             .UseNpgsql("Host=localhost;Database=mygym_design;Username=postgres;Password=postgres")
>             .UseSnakeCaseNamingConvention()
>             .Options;
>
>         return new TrainingDbContext(options, dispatcher: null);
>     }
> }
> ```
>
> Then re-run the migration command with `--startup-project` pointing at the Infrastructure project
> itself. The connection string is never used to generate a migration — only to pick the provider.

Verify the generated migration creates both named indexes:

```bash
grep -n "ix_sessions_user_id_in_progress\|ix_logged_sets_session_exercise_id_client_set_id" \
  src/Modules/Training/MyGym.Modules.Training.Infrastructure/Migrations/*_InitialTraining.cs
```

Expected: both names appear, and the sessions index carries `filter: "status = 1"`.

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test tests/MyGym.IntegrationTests`
Expected: PASS — 7 tests. Docker must be running.

- [ ] **Step 9: Commit**

```bash
git add src/Shared/MyGym.Shared.Infrastructure src/Modules/Training tests/MyGym.IntegrationTests Directory.Packages.props
git commit -m "feat: add Training persistence with database-enforced session invariants"
```

---

### Task 9: Identity and ExerciseCatalog persistence with catalog seeding

**Files:**
- Create: `src/Modules/Identity/MyGym.Modules.Identity.Infrastructure/{IdentityDbContext,AppUserConfiguration,AppUserRepository,IdentityInfrastructureExtensions}.cs`
- Create: `src/Modules/Identity/MyGym.Modules.Identity.Domain/IIdentityUnitOfWork.cs`
- Create: `src/Modules/ExerciseCatalog/MyGym.Modules.ExerciseCatalog.Infrastructure/{CatalogDbContext,ExerciseConfiguration,ExerciseRepository,CatalogSeeder,CatalogInfrastructureExtensions}.cs`
- Test: `tests/MyGym.IntegrationTests/{IdentityPersistenceTests,CatalogPersistenceTests}.cs`

**Interfaces:**
- Consumes: `AppUser`, `AthleteProfile`, `IAppUserRepository` (Task 6); `Exercise`, `ExerciseName`, `IExerciseRepository`, `CatalogSeed` (Task 7); persistence plumbing (Task 8).
- Produces:
  - `IIdentityUnitOfWork`, `ICatalogUnitOfWork` — each with `SaveChangesAsync(CancellationToken)`
  - `IdentityDbContext` with `DbSet<AppUser> Users`; `CatalogDbContext` with `DbSet<Exercise> Exercises`
  - `AppUserRepository`, `ExerciseRepository`
  - `CatalogSeeder.SeedAsync(CatalogDbContext, CancellationToken)` — idempotent, matches on slug
  - `AddIdentityInfrastructure(IServiceCollection, string)`, `AddCatalogInfrastructure(IServiceCollection, string)`
  - `PostgresFixture.CreateIdentityContext()`, `PostgresFixture.CreateCatalogContext()`

- [ ] **Step 1: Create the projects**

```bash
dotnet new classlib -o src/Modules/Identity/MyGym.Modules.Identity.Infrastructure
dotnet new classlib -o src/Modules/ExerciseCatalog/MyGym.Modules.ExerciseCatalog.Infrastructure
dotnet sln add src/Modules/Identity/MyGym.Modules.Identity.Infrastructure \
               src/Modules/ExerciseCatalog/MyGym.Modules.ExerciseCatalog.Infrastructure

dotnet add src/Modules/Identity/MyGym.Modules.Identity.Infrastructure reference \
           src/Modules/Identity/MyGym.Modules.Identity.Domain src/Shared/MyGym.Shared.Infrastructure
dotnet add src/Modules/Identity/MyGym.Modules.Identity.Infrastructure package Microsoft.EntityFrameworkCore.Design

dotnet add src/Modules/ExerciseCatalog/MyGym.Modules.ExerciseCatalog.Infrastructure reference \
           src/Modules/ExerciseCatalog/MyGym.Modules.ExerciseCatalog.Domain src/Shared/MyGym.Shared.Infrastructure
dotnet add src/Modules/ExerciseCatalog/MyGym.Modules.ExerciseCatalog.Infrastructure package Microsoft.EntityFrameworkCore.Design

dotnet add tests/MyGym.IntegrationTests reference \
           src/Modules/Identity/MyGym.Modules.Identity.Infrastructure \
           src/Modules/ExerciseCatalog/MyGym.Modules.ExerciseCatalog.Infrastructure
rm src/Modules/Identity/MyGym.Modules.Identity.Infrastructure/Class1.cs \
   src/Modules/ExerciseCatalog/MyGym.Modules.ExerciseCatalog.Infrastructure/Class1.cs
```

- [ ] **Step 2: Write the failing tests**

`tests/MyGym.IntegrationTests/IdentityPersistenceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using MyGym.Modules.Identity.Domain;
using MyGym.SharedKernel;
using Shouldly;

namespace MyGym.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public class IdentityPersistenceTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_user_round_trips_with_a_fully_populated_profile()
    {
        var user = AppUser.Register(Random.Shared.NextInt64(1, long.MaxValue), "Akmal", "akmal", "uz", Now).Value;
        user.UpdateProfile(AthleteProfile.Create(
            1996, Weight.Kg(74.5m), WeightUnit.Kilograms, WeightIncrement.DefaultKg,
            ExperienceLevel.Intermediate, 3, 90, "Asia/Tashkent").Value);

        await using (var write = fixture.CreateIdentityContext())
        {
            write.Users.Add(user);
            await write.SaveChangesAsync();
        }

        await using var read = fixture.CreateIdentityContext();
        var loaded = await read.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);

        loaded.FirstName.ShouldBe("Akmal");
        loaded.LanguageCode.ShouldBe("uz");
        loaded.Profile.BirthYear.ShouldBe(1996);
        loaded.Profile.Bodyweight!.Value.ShouldBe(74.5m);
        loaded.Profile.LoadingIncrement.Value.ShouldBe(2.5m);
        loaded.Profile.ExperienceLevel.ShouldBe(ExperienceLevel.Intermediate);
        loaded.Profile.AvailableDaysPerWeek.ShouldBe(3);
        loaded.Profile.TimeZoneId.ShouldBe("Asia/Tashkent");
    }

    [Fact]
    public async Task A_telegram_account_can_only_be_linked_once()
    {
        var telegramId = Random.Shared.NextInt64(1, long.MaxValue);

        await using var context = fixture.CreateIdentityContext();
        context.Users.Add(AppUser.Register(telegramId, "First", null, null, Now).Value);
        await context.SaveChangesAsync();

        context.Users.Add(AppUser.Register(telegramId, "Second", null, null, Now).Value);

        var exception = await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync());
        exception.InnerException!.Message.ShouldContain("telegram_user_id");
    }

    [Fact]
    public async Task A_user_can_be_looked_up_by_telegram_id()
    {
        var telegramId = Random.Shared.NextInt64(1, long.MaxValue);
        var user = AppUser.Register(telegramId, "Akmal", null, "uz", Now).Value;

        await using (var write = fixture.CreateIdentityContext())
        {
            write.Users.Add(user);
            await write.SaveChangesAsync();
        }

        await using var read = fixture.CreateIdentityContext();
        var found = await read.Users.SingleOrDefaultAsync(u => u.TelegramUserId == telegramId);

        found!.Id.ShouldBe(user.Id);
    }
}
```

`tests/MyGym.IntegrationTests/CatalogPersistenceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using MyGym.Modules.ExerciseCatalog.Domain;
using MyGym.Modules.ExerciseCatalog.Infrastructure;
using Shouldly;

namespace MyGym.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public class CatalogPersistenceTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Seeding_inserts_the_whole_catalog()
    {
        await using var context = fixture.CreateCatalogContext();
        await CatalogSeeder.SeedAsync(context, CancellationToken.None);

        (await context.Exercises.CountAsync()).ShouldBeGreaterThanOrEqualTo(26);
    }

    [Fact]
    public async Task Seeding_twice_does_not_duplicate_exercises()
    {
        await using var context = fixture.CreateCatalogContext();
        await CatalogSeeder.SeedAsync(context, CancellationToken.None);
        var afterFirst = await context.Exercises.CountAsync();

        await CatalogSeeder.SeedAsync(context, CancellationToken.None);

        (await context.Exercises.CountAsync()).ShouldBe(afterFirst);
    }

    [Fact]
    public async Task A_seeded_exercise_round_trips_its_names_muscles_and_aliases()
    {
        await using var context = fixture.CreateCatalogContext();
        await CatalogSeeder.SeedAsync(context, CancellationToken.None);

        var squat = await context.Exercises.AsNoTracking().SingleAsync(e => e.Slug == "back-squat");

        squat.MovementPattern.ShouldBe(MovementPattern.Squat);
        squat.Equipment.ShouldBe(Equipment.Barbell);
        squat.PrimaryMuscles.ShouldContain(MuscleGroup.Quads);
        squat.SecondaryMuscles.ShouldContain(MuscleGroup.Glutes);
        squat.NameFor("uz").ShouldBe("Shtanga bilan o'tirib turish");
        squat.NameFor("en").ShouldBe("Back Squat");
        squat.Aliases.ShouldContain("squat");
    }

    [Fact]
    public async Task Slugs_are_unique_in_the_database()
    {
        await using var context = fixture.CreateCatalogContext();
        await CatalogSeeder.SeedAsync(context, CancellationToken.None);

        context.Exercises.Add(Exercise.Create(
            "back-squat", MovementPattern.Squat, [MuscleGroup.Quads], [], Equipment.Barbell,
            [ExerciseName.Create("en", "Duplicate").Value], []).Value);

        var exception = await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync());
        exception.InnerException!.Message.ShouldContain("slug");
    }
}
```

- [ ] **Step 3: Extend the fixture**

Add to `tests/MyGym.IntegrationTests/PostgresFixture.cs`, and change `InitializeAsync` to migrate all
three contexts:

```csharp
    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        await using (var training = CreateTrainingContext())
            await training.Database.MigrateAsync();
        await using (var identity = CreateIdentityContext())
            await identity.Database.MigrateAsync();
        await using (var catalog = CreateCatalogContext())
            await catalog.Database.MigrateAsync();
    }

    public IdentityDbContext CreateIdentityContext()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new IdentityDbContext(options, dispatcher: null);
    }

    public CatalogDbContext CreateCatalogContext()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new CatalogDbContext(options, dispatcher: null);
    }
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test tests/MyGym.IntegrationTests`
Expected: FAIL — `IdentityDbContext` and `CatalogDbContext` do not exist.

- [ ] **Step 5: Write the Identity infrastructure**

`src/Modules/Identity/MyGym.Modules.Identity.Domain/IIdentityUnitOfWork.cs`:

```csharp
namespace MyGym.Modules.Identity.Domain;

public interface IIdentityUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct);
}
```

`src/Modules/Identity/MyGym.Modules.Identity.Infrastructure/IdentityDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using MyGym.Modules.Identity.Domain;
using MyGym.Shared.Infrastructure.Persistence;

namespace MyGym.Modules.Identity.Infrastructure;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options, IDomainEventDispatcher? dispatcher)
    : ModuleDbContext(options, dispatcher), IIdentityUnitOfWork
{
    public const string Schema = "identity";

    public DbSet<AppUser> Users => Set<AppUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfiguration(new AppUserConfiguration());
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.AddTypedIds();
}
```

`src/Modules/Identity/MyGym.Modules.Identity.Infrastructure/AppUserConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyGym.Modules.Identity.Domain;

namespace MyGym.Modules.Identity.Infrastructure;

internal sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.ToTable("users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.FirstName).HasMaxLength(120).IsRequired();
        builder.Property(u => u.Username).HasMaxLength(64);
        builder.Property(u => u.LanguageCode).HasMaxLength(8);
        builder.HasIndex(u => u.TelegramUserId).IsUnique();

        builder.OwnsOne(u => u.Profile, profile =>
        {
            profile.Property(p => p.BirthYear).HasColumnName("birth_year");
            profile.Property(p => p.PreferredUnit).HasColumnName("preferred_unit").HasConversion<int>();
            profile.Property(p => p.ExperienceLevel).HasColumnName("experience_level").HasConversion<int>();
            profile.Property(p => p.AvailableDaysPerWeek).HasColumnName("available_days_per_week");
            profile.Property(p => p.SessionMinutes).HasColumnName("session_minutes");
            profile.Property(p => p.TimeZoneId).HasColumnName("time_zone_id").HasMaxLength(64).IsRequired();

            profile.OwnsOne(p => p.Bodyweight, weight =>
            {
                weight.Property(w => w.Value).HasColumnName("bodyweight_value").HasPrecision(6, 2);
                weight.Property(w => w.Unit).HasColumnName("bodyweight_unit").HasConversion<int>();
            });

            profile.OwnsOne(p => p.LoadingIncrement, increment =>
            {
                increment.Property(i => i.Value).HasColumnName("loading_increment_value").HasPrecision(5, 2);
                increment.Property(i => i.Unit).HasColumnName("loading_increment_unit").HasConversion<int>();
            });
        });

        builder.Navigation(u => u.Profile).IsRequired();
    }
}
```

`src/Modules/Identity/MyGym.Modules.Identity.Infrastructure/AppUserRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using MyGym.Modules.Identity.Domain;
using MyGym.SharedKernel;

namespace MyGym.Modules.Identity.Infrastructure;

internal sealed class AppUserRepository(IdentityDbContext context) : IAppUserRepository
{
    public Task<AppUser?> GetByIdAsync(UserId id, CancellationToken ct) =>
        context.Users.SingleOrDefaultAsync(u => u.Id == id, ct);

    public Task<AppUser?> GetByTelegramUserIdAsync(long telegramUserId, CancellationToken ct) =>
        context.Users.SingleOrDefaultAsync(u => u.TelegramUserId == telegramUserId, ct);

    public async Task AddAsync(AppUser user, CancellationToken ct) => await context.Users.AddAsync(user, ct);

    public void Remove(AppUser user) => context.Users.Remove(user);
}
```

`src/Modules/Identity/MyGym.Modules.Identity.Infrastructure/IdentityInfrastructureExtensions.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyGym.Modules.Identity.Domain;

namespace MyGym.Modules.Identity.Infrastructure;

public static class IdentityInfrastructureExtensions
{
    public static IServiceCollection AddIdentityInfrastructure(
        this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<IdentityDbContext>(options => options
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", IdentityDbContext.Schema))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IAppUserRepository, AppUserRepository>();
        services.AddScoped<IIdentityUnitOfWork>(sp => sp.GetRequiredService<IdentityDbContext>());

        return services;
    }
}
```

- [ ] **Step 6: Write the ExerciseCatalog infrastructure**

`src/Modules/ExerciseCatalog/MyGym.Modules.ExerciseCatalog.Domain/ICatalogUnitOfWork.cs`:

```csharp
namespace MyGym.Modules.ExerciseCatalog.Domain;

public interface ICatalogUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct);
}
```

`src/Modules/ExerciseCatalog/MyGym.Modules.ExerciseCatalog.Infrastructure/CatalogDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using MyGym.Modules.ExerciseCatalog.Domain;
using MyGym.Shared.Infrastructure.Persistence;

namespace MyGym.Modules.ExerciseCatalog.Infrastructure;

public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options, IDomainEventDispatcher? dispatcher)
    : ModuleDbContext(options, dispatcher), ICatalogUnitOfWork
{
    public const string Schema = "catalog";

    public DbSet<Exercise> Exercises => Set<Exercise>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfiguration(new ExerciseConfiguration());
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.AddTypedIds();
}
```

`src/Modules/ExerciseCatalog/MyGym.Modules.ExerciseCatalog.Infrastructure/ExerciseConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyGym.Modules.ExerciseCatalog.Domain;

namespace MyGym.Modules.ExerciseCatalog.Infrastructure;

internal sealed class ExerciseConfiguration : IEntityTypeConfiguration<Exercise>
{
    public void Configure(EntityTypeBuilder<Exercise> builder)
    {
        builder.ToTable("exercises");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Slug).HasMaxLength(80).IsRequired();
        builder.HasIndex(e => e.Slug).IsUnique();
        builder.Property(e => e.MovementPattern).HasConversion<int>();
        builder.Property(e => e.Equipment).HasConversion<int>();
        builder.Ignore(e => e.IsCustom);
        builder.HasIndex(e => e.OwnerUserId);

        // Enum lists and aliases are stored as PostgreSQL arrays: queryable with ANY(), no join.
        builder.Property(e => e.PrimaryMuscles)
            .HasConversion(
                muscles => muscles.Select(m => (int)m).ToArray(),
                values => values.Select(v => (MuscleGroup)v).ToList())
            .HasColumnType("integer[]")
            .Metadata.SetValueComparer(
                new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<IReadOnlyList<MuscleGroup>>(
                    (a, b) => a!.SequenceEqual(b!),
                    list => list.Aggregate(0, (hash, m) => HashCode.Combine(hash, m))));

        builder.Property(e => e.SecondaryMuscles)
            .HasConversion(
                muscles => muscles.Select(m => (int)m).ToArray(),
                values => values.Select(v => (MuscleGroup)v).ToList())
            .HasColumnType("integer[]")
            .Metadata.SetValueComparer(
                new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<IReadOnlyList<MuscleGroup>>(
                    (a, b) => a!.SequenceEqual(b!),
                    list => list.Aggregate(0, (hash, m) => HashCode.Combine(hash, m))));

        builder.PrimitiveCollection<List<string>>("_aliases")
            .HasColumnName("aliases")
            .HasColumnType("text[]");

        builder.OwnsMany<ExerciseName>("_names", name =>
        {
            name.ToTable("exercise_names");
            name.WithOwner().HasForeignKey("exercise_id");
            name.Property(n => n.LanguageCode).HasColumnName("language_code").HasMaxLength(2).IsRequired();
            name.Property(n => n.Value).HasColumnName("value").HasMaxLength(120).IsRequired();
            name.HasKey("exercise_id", nameof(ExerciseName.LanguageCode));
        });

        builder.Navigation("_names").AutoInclude();
    }
}
```

`src/Modules/ExerciseCatalog/MyGym.Modules.ExerciseCatalog.Infrastructure/ExerciseRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using MyGym.Modules.ExerciseCatalog.Domain;
using MyGym.SharedKernel;

namespace MyGym.Modules.ExerciseCatalog.Infrastructure;

internal sealed class ExerciseRepository(CatalogDbContext context) : IExerciseRepository
{
    public Task<Exercise?> GetByIdAsync(ExerciseId id, CancellationToken ct) =>
        context.Exercises.SingleOrDefaultAsync(e => e.Id == id, ct);

    public Task<Exercise?> GetBySlugAsync(string slug, CancellationToken ct) =>
        context.Exercises.SingleOrDefaultAsync(e => e.Slug == slug, ct);

    public Task<bool> ExistsAsync(ExerciseId id, CancellationToken ct) =>
        context.Exercises.AnyAsync(e => e.Id == id, ct);

    public async Task AddAsync(Exercise exercise, CancellationToken ct) =>
        await context.Exercises.AddAsync(exercise, ct);
}
```

`src/Modules/ExerciseCatalog/MyGym.Modules.ExerciseCatalog.Infrastructure/CatalogSeeder.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using MyGym.Modules.ExerciseCatalog.Domain;

namespace MyGym.Modules.ExerciseCatalog.Infrastructure;

public static class CatalogSeeder
{
    /// <summary>Idempotent: matches on slug, so re-running on every startup is safe.</summary>
    public static async Task SeedAsync(CatalogDbContext context, CancellationToken ct)
    {
        var existing = await context.Exercises.Select(e => e.Slug).ToListAsync(ct);
        var known = existing.ToHashSet();

        var missing = CatalogSeed.Exercises.Where(e => !known.Contains(e.Slug)).ToList();
        if (missing.Count == 0)
            return;

        await context.Exercises.AddRangeAsync(missing, ct);
        await context.SaveChangesAsync(ct);
    }
}
```

`src/Modules/ExerciseCatalog/MyGym.Modules.ExerciseCatalog.Infrastructure/CatalogInfrastructureExtensions.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyGym.Modules.ExerciseCatalog.Domain;

namespace MyGym.Modules.ExerciseCatalog.Infrastructure;

public static class CatalogInfrastructureExtensions
{
    public static IServiceCollection AddCatalogInfrastructure(
        this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<CatalogDbContext>(options => options
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", CatalogDbContext.Schema))
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IExerciseRepository, ExerciseRepository>();
        services.AddScoped<ICatalogUnitOfWork>(sp => sp.GetRequiredService<CatalogDbContext>());

        return services;
    }
}
```

- [ ] **Step 7: Create the migrations**

```bash
dotnet ef migrations add InitialIdentity \
  --project src/Modules/Identity/MyGym.Modules.Identity.Infrastructure \
  --startup-project src/Modules/Identity/MyGym.Modules.Identity.Infrastructure \
  --context IdentityDbContext --output-dir Migrations

dotnet ef migrations add InitialCatalog \
  --project src/Modules/ExerciseCatalog/MyGym.Modules.ExerciseCatalog.Infrastructure \
  --startup-project src/Modules/ExerciseCatalog/MyGym.Modules.ExerciseCatalog.Infrastructure \
  --context CatalogDbContext --output-dir Migrations
```

Each needs an `IDesignTimeDbContextFactory` alongside it, following the same shape as
`TrainingDbContextFactory` in Task 8 but returning its own context type and passing
`dispatcher: null`.

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test tests/MyGym.IntegrationTests`
Expected: PASS — 14 tests across the three persistence suites.

- [ ] **Step 9: Commit**

```bash
git add src/Modules/Identity src/Modules/ExerciseCatalog tests/MyGym.IntegrationTests
git commit -m "feat: add Identity and ExerciseCatalog persistence with idempotent catalog seeding"
```

---

### Task 10: Telegram initData validation and token issuance

**Files:**
- Create: `src/Shared/MyGym.Shared.Infrastructure/Auth/{TelegramInitDataValidator,TelegramAuthOptions,JwtTokenService,JwtOptions}.cs`
- Create: `src/Modules/Identity/MyGym.Modules.Identity.Domain/{RefreshToken,IRefreshTokenRepository}.cs`
- Create: `src/Modules/Identity/MyGym.Modules.Identity.Infrastructure/{RefreshTokenConfiguration,RefreshTokenRepository}.cs`
- Test: `tests/MyGym.Shared.Infrastructure.Tests/TelegramInitDataValidatorTests.cs`
- Test: `tests/MyGym.IntegrationTests/RefreshTokenPersistenceTests.cs`

**Interfaces:**
- Consumes: `Result<T>`, `Error`, `UserId` (Tasks 1–2); `IdentityDbContext` (Task 9).
- Produces:
  - `TelegramUserInfo(long TelegramUserId, string FirstName, string? Username, string? LanguageCode, DateTimeOffset AuthDate)`
  - `TelegramAuthOptions` with `BotToken`, `WebhookSecretToken`, `MaxInitDataAge` (default 1 hour)
  - `ITelegramInitDataValidator` with `Validate(string initData) → Result<TelegramUserInfo>`; implementation `TelegramInitDataValidator`
  - `JwtOptions` with `Issuer`, `Audience`, `SigningKey`, `AccessTokenLifetime` (default 15 minutes), `RefreshTokenLifetime` (default 30 days)
  - `IJwtTokenService` with `CreateAccessToken(UserId) → string`, `CreateRefreshToken() → (string Token, string Hash)`, `HashRefreshToken(string) → string`
  - `RefreshToken` entity with `UserId`, `TokenHash`, `ExpiresAt`, `CreatedAt`, `RevokedAt`, `IsActive(DateTimeOffset)`, `Revoke(DateTimeOffset)`, and `RefreshToken.Issue(UserId, string, DateTimeOffset, DateTimeOffset)`
  - `IRefreshTokenRepository` with `FindActiveAsync(string hash, DateTimeOffset now, CancellationToken)`, `AddAsync`, `RevokeAllForUserAsync(UserId, DateTimeOffset, CancellationToken)`

- [ ] **Step 1: Create the test project**

```bash
dotnet new xunit3 -o tests/MyGym.Shared.Infrastructure.Tests
dotnet sln add tests/MyGym.Shared.Infrastructure.Tests
dotnet add tests/MyGym.Shared.Infrastructure.Tests reference src/Shared/MyGym.Shared.Infrastructure
dotnet add tests/MyGym.Shared.Infrastructure.Tests package Shouldly
dotnet add src/Shared/MyGym.Shared.Infrastructure package Microsoft.AspNetCore.Authentication.JwtBearer
```

- [ ] **Step 2: Write the failing tests**

`tests/MyGym.Shared.Infrastructure.Tests/TelegramInitDataValidatorTests.cs`:

```csharp
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MyGym.Shared.Infrastructure.Auth;
using Shouldly;

namespace MyGym.Shared.Infrastructure.Tests;

public class TelegramInitDataValidatorTests
{
    private const string BotToken = "123456:TEST-BOT-TOKEN-FOR-UNIT-TESTS";
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    private static TelegramInitDataValidator CreateValidator() =>
        new(Options.Create(new TelegramAuthOptions
        {
            BotToken = BotToken,
            WebhookSecretToken = "irrelevant-here",
            MaxInitDataAge = TimeSpan.FromHours(1)
        }), new FakeTimeProvider(Now));

    /// <summary>
    /// Builds initData the way Telegram does. This mirrors the algorithm under test, so it cannot
    /// prove interoperability with Telegram — only the tamper, staleness, and parsing tests below
    /// have that power, and the true end-to-end check is opening the real Mini App (Task 14).
    /// </summary>
    private static string BuildInitData(
        long userId = 123456789L,
        string firstName = "Akmal",
        string? username = "akmal",
        string? languageCode = "uz",
        DateTimeOffset? authDate = null,
        string? overrideHash = null)
    {
        var user = $"{{\"id\":{userId},\"first_name\":\"{firstName}\"" +
                   (username is null ? "" : $",\"username\":\"{username}\"") +
                   (languageCode is null ? "" : $",\"language_code\":\"{languageCode}\"") + "}";

        var fields = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["auth_date"] = ((authDate ?? Now).ToUnixTimeSeconds()).ToString(),
            ["query_id"] = "AAEtest",
            ["user"] = user
        };

        var dataCheckString = string.Join('\n', fields.Select(f => $"{f.Key}={f.Value}"));
        using var secretKeyHmac = new HMACSHA256(Encoding.UTF8.GetBytes("WebAppData"));
        var secretKey = secretKeyHmac.ComputeHash(Encoding.UTF8.GetBytes(BotToken));
        using var signatureHmac = new HMACSHA256(secretKey);
        var hash = Convert.ToHexStringLower(
            signatureHmac.ComputeHash(Encoding.UTF8.GetBytes(dataCheckString)));

        var parts = fields.Select(f => $"{f.Key}={WebUtility.UrlEncode(f.Value)}").ToList();
        parts.Add($"hash={overrideHash ?? hash}");
        return string.Join('&', parts);
    }

    [Fact]
    public void Valid_init_data_yields_the_telegram_user()
    {
        var result = CreateValidator().Validate(BuildInitData());

        result.IsSuccess.ShouldBeTrue(result.Error.Code);
        result.Value.TelegramUserId.ShouldBe(123456789L);
        result.Value.FirstName.ShouldBe("Akmal");
        result.Value.Username.ShouldBe("akmal");
        result.Value.LanguageCode.ShouldBe("uz");
        result.Value.AuthDate.ShouldBe(Now);
    }

    [Fact]
    public void An_optional_username_and_language_may_be_absent()
    {
        var result = CreateValidator().Validate(BuildInitData(username: null, languageCode: null));

        result.IsSuccess.ShouldBeTrue(result.Error.Code);
        result.Value.Username.ShouldBeNull();
        result.Value.LanguageCode.ShouldBeNull();
    }

    [Fact]
    public void A_tampered_hash_is_rejected()
    {
        var result = CreateValidator().Validate(
            BuildInitData(overrideHash: new string('a', 64)));

        result.Error.Code.ShouldBe(AuthErrors.InitDataSignatureInvalid);
    }

    [Fact]
    public void A_tampered_payload_is_rejected()
    {
        // Keep the signature from user 123456789 but claim to be someone else.
        var honest = BuildInitData();
        var hash = honest.Split("hash=")[1];
        var forged = BuildInitData(userId: 999999999L, overrideHash: hash);

        CreateValidator().Validate(forged).Error.Code.ShouldBe(AuthErrors.InitDataSignatureInvalid);
    }

    [Fact]
    public void Init_data_older_than_the_configured_window_is_rejected()
    {
        var result = CreateValidator().Validate(BuildInitData(authDate: Now.AddHours(-2)));

        result.Error.Code.ShouldBe(AuthErrors.InitDataExpired);
    }

    [Fact]
    public void Init_data_from_the_future_is_rejected()
    {
        CreateValidator().Validate(BuildInitData(authDate: Now.AddMinutes(10)))
            .Error.Code.ShouldBe(AuthErrors.InitDataExpired);
    }

    [Theory]
    [InlineData("")]
    [InlineData("auth_date=1&user=%7B%7D")]
    [InlineData("hash=abc")]
    [InlineData("not-a-query-string")]
    public void Malformed_init_data_is_rejected(string initData)
    {
        CreateValidator().Validate(initData).IsFailure.ShouldBeTrue();
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/MyGym.Shared.Infrastructure.Tests`
Expected: FAIL — `TelegramInitDataValidator` does not exist.

- [ ] **Step 4: Write the validator**

`src/Shared/MyGym.Shared.Infrastructure/Auth/TelegramAuthOptions.cs`:

```csharp
namespace MyGym.Shared.Infrastructure.Auth;

public sealed class TelegramAuthOptions
{
    public const string SectionName = "Telegram";

    public required string BotToken { get; init; }
    public required string WebhookSecretToken { get; init; }
    public TimeSpan MaxInitDataAge { get; init; } = TimeSpan.FromHours(1);
}

public static class AuthErrors
{
    public const string InitDataMalformed = "auth.init_data_malformed";
    public const string InitDataSignatureInvalid = "auth.init_data_signature_invalid";
    public const string InitDataExpired = "auth.init_data_expired";
    public const string RefreshTokenInvalid = "auth.refresh_token_invalid";
}
```

`src/Shared/MyGym.Shared.Infrastructure/Auth/TelegramInitDataValidator.cs`:

```csharp
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MyGym.SharedKernel;

namespace MyGym.Shared.Infrastructure.Auth;

public sealed record TelegramUserInfo(
    long TelegramUserId,
    string FirstName,
    string? Username,
    string? LanguageCode,
    DateTimeOffset AuthDate);

public interface ITelegramInitDataValidator
{
    Result<TelegramUserInfo> Validate(string initData);
}

/// <summary>
/// Verifies the signature Telegram attaches to Mini App init data.
/// See https://core.telegram.org/bots/webapps#validating-data-received-via-the-mini-app
/// </summary>
public sealed class TelegramInitDataValidator(
    IOptions<TelegramAuthOptions> options,
    TimeProvider timeProvider) : ITelegramInitDataValidator
{
    private const string SecretKeySalt = "WebAppData";

    public Result<TelegramUserInfo> Validate(string initData)
    {
        if (string.IsNullOrWhiteSpace(initData))
            return Fail(AuthErrors.InitDataMalformed, "Init data is empty.");

        var fields = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in initData.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            if (separator <= 0)
                return Fail(AuthErrors.InitDataMalformed, "Init data is not a query string.");

            fields[pair[..separator]] = WebUtility.UrlDecode(pair[(separator + 1)..]);
        }

        if (!fields.Remove("hash", out var providedHash) || providedHash.Length != 64)
            return Fail(AuthErrors.InitDataMalformed, "Init data has no usable hash.");

        if (!fields.TryGetValue("auth_date", out var authDateRaw)
            || !long.TryParse(authDateRaw, out var authDateUnix))
            return Fail(AuthErrors.InitDataMalformed, "Init data has no auth_date.");

        if (!fields.TryGetValue("user", out var userJson))
            return Fail(AuthErrors.InitDataMalformed, "Init data has no user.");

        var dataCheckString = string.Join('\n', fields.Select(f => $"{f.Key}={f.Value}"));
        if (!SignatureMatches(dataCheckString, providedHash))
            return Fail(AuthErrors.InitDataSignatureInvalid, "Init data signature does not match.");

        var authDate = DateTimeOffset.FromUnixTimeSeconds(authDateUnix);
        var now = timeProvider.GetUtcNow();
        if (authDate > now.AddMinutes(1) || now - authDate > options.Value.MaxInitDataAge)
            return Fail(AuthErrors.InitDataExpired, "Init data is outside the accepted time window.");

        return ParseUser(userJson, authDate);
    }

    private bool SignatureMatches(string dataCheckString, string providedHash)
    {
        using var secretKeyHmac = new HMACSHA256(Encoding.UTF8.GetBytes(SecretKeySalt));
        var secretKey = secretKeyHmac.ComputeHash(Encoding.UTF8.GetBytes(options.Value.BotToken));

        using var signatureHmac = new HMACSHA256(secretKey);
        var computed = signatureHmac.ComputeHash(Encoding.UTF8.GetBytes(dataCheckString));

        Span<byte> provided = stackalloc byte[32];
        return Convert.FromHexString(providedHash).AsSpan().TryCopyTo(provided)
            && CryptographicOperations.FixedTimeEquals(computed, provided);
    }

    private static Result<TelegramUserInfo> ParseUser(string userJson, DateTimeOffset authDate)
    {
        try
        {
            using var document = JsonDocument.Parse(userJson);
            var root = document.RootElement;

            if (!root.TryGetProperty("id", out var idElement) || !idElement.TryGetInt64(out var id))
                return Fail(AuthErrors.InitDataMalformed, "User payload has no id.");

            var firstName = root.TryGetProperty("first_name", out var firstNameElement)
                ? firstNameElement.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(firstName))
                return Fail(AuthErrors.InitDataMalformed, "User payload has no first_name.");

            return Result<TelegramUserInfo>.Success(new TelegramUserInfo(
                id,
                firstName,
                root.TryGetProperty("username", out var u) ? u.GetString() : null,
                root.TryGetProperty("language_code", out var l) ? l.GetString() : null,
                authDate));
        }
        catch (JsonException)
        {
            return Fail(AuthErrors.InitDataMalformed, "User payload is not valid JSON.");
        }
    }

    private static Result<TelegramUserInfo> Fail(string code, string message) =>
        Result<TelegramUserInfo>.Failure(new Error(code, message));
}
```

- [ ] **Step 5: Write the token service and refresh token entity**

`src/Shared/MyGym.Shared.Infrastructure/Auth/JwtTokenService.cs`:

```csharp
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using MyGym.SharedKernel;

namespace MyGym.Shared.Infrastructure.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public required string Issuer { get; init; }
    public required string Audience { get; init; }
    public required string SigningKey { get; init; }
    public TimeSpan AccessTokenLifetime { get; init; } = TimeSpan.FromMinutes(15);
    public TimeSpan RefreshTokenLifetime { get; init; } = TimeSpan.FromDays(30);
}

public interface IJwtTokenService
{
    string CreateAccessToken(UserId userId);
    (string Token, string Hash) CreateRefreshToken();
    string HashRefreshToken(string token);
}

public sealed class JwtTokenService(IOptions<JwtOptions> options, TimeProvider timeProvider) : IJwtTokenService
{
    public string CreateAccessToken(UserId userId)
    {
        var now = timeProvider.GetUtcNow();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Value.SigningKey));

        var token = new JwtSecurityToken(
            issuer: options.Value.Issuer,
            audience: options.Value.Audience,
            claims: [new Claim(JwtRegisteredClaimNames.Sub, userId.Value.ToString())],
            notBefore: now.UtcDateTime,
            expires: now.Add(options.Value.AccessTokenLifetime).UtcDateTime,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public (string Token, string Hash) CreateRefreshToken()
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        return (token, HashRefreshToken(token));
    }

    /// <summary>Refresh tokens are stored hashed, so a database leak does not yield usable tokens.</summary>
    public string HashRefreshToken(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

internal static class JwtRegisteredClaimNames
{
    public const string Sub = "sub";
}
```

`src/Modules/Identity/MyGym.Modules.Identity.Domain/RefreshToken.cs`:

```csharp
using MyGym.SharedKernel;

namespace MyGym.Modules.Identity.Domain;

public sealed class RefreshToken : Entity<Guid>
{
    private RefreshToken(
        Guid id, UserId userId, string tokenHash, DateTimeOffset createdAt, DateTimeOffset expiresAt)
        : base(id)
    {
        UserId = userId;
        TokenHash = tokenHash;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public UserId UserId { get; private set; }
    public string TokenHash { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    public static RefreshToken Issue(
        UserId userId, string tokenHash, DateTimeOffset now, DateTimeOffset expiresAt) =>
        new(Guid.CreateVersion7(), userId, tokenHash, now, expiresAt);

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt;

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> FindActiveAsync(string tokenHash, DateTimeOffset now, CancellationToken ct);
    Task AddAsync(RefreshToken token, CancellationToken ct);
    Task RevokeAllForUserAsync(UserId userId, DateTimeOffset now, CancellationToken ct);
}
```

`src/Modules/Identity/MyGym.Modules.Identity.Infrastructure/RefreshTokenConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyGym.Modules.Identity.Domain;

namespace MyGym.Modules.Identity.Infrastructure;

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => t.UserId);
    }
}
```

`src/Modules/Identity/MyGym.Modules.Identity.Infrastructure/RefreshTokenRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using MyGym.Modules.Identity.Domain;
using MyGym.SharedKernel;

namespace MyGym.Modules.Identity.Infrastructure;

internal sealed class RefreshTokenRepository(IdentityDbContext context) : IRefreshTokenRepository
{
    public Task<RefreshToken?> FindActiveAsync(string tokenHash, DateTimeOffset now, CancellationToken ct) =>
        context.RefreshTokens.SingleOrDefaultAsync(
            t => t.TokenHash == tokenHash && t.RevokedAt == null && t.ExpiresAt > now, ct);

    public async Task AddAsync(RefreshToken token, CancellationToken ct) =>
        await context.RefreshTokens.AddAsync(token, ct);

    public async Task RevokeAllForUserAsync(UserId userId, DateTimeOffset now, CancellationToken ct)
    {
        var tokens = await context.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var token in tokens)
            token.Revoke(now);
    }
}
```

Register the new set and configuration in `IdentityDbContext`:

```csharp
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfiguration(new AppUserConfiguration());
        modelBuilder.ApplyConfiguration(new RefreshTokenConfiguration());
    }
```

And in `IdentityInfrastructureExtensions.AddIdentityInfrastructure`:

```csharp
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
```

- [ ] **Step 6: Write the refresh-token persistence test**

`tests/MyGym.IntegrationTests/RefreshTokenPersistenceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using MyGym.Modules.Identity.Domain;
using MyGym.SharedKernel;
using Shouldly;

namespace MyGym.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public class RefreshTokenPersistenceTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task An_active_token_is_found_and_a_revoked_one_is_not()
    {
        var userId = UserId.New();
        var active = RefreshToken.Issue(userId, Guid.NewGuid().ToString("N"), Now, Now.AddDays(30));
        var revoked = RefreshToken.Issue(userId, Guid.NewGuid().ToString("N"), Now, Now.AddDays(30));
        revoked.Revoke(Now);

        await using var context = fixture.CreateIdentityContext();
        context.RefreshTokens.AddRange(active, revoked);
        await context.SaveChangesAsync();

        var foundActive = await context.RefreshTokens.SingleOrDefaultAsync(
            t => t.TokenHash == active.TokenHash && t.RevokedAt == null && t.ExpiresAt > Now);
        var foundRevoked = await context.RefreshTokens.SingleOrDefaultAsync(
            t => t.TokenHash == revoked.TokenHash && t.RevokedAt == null && t.ExpiresAt > Now);

        foundActive.ShouldNotBeNull();
        foundRevoked.ShouldBeNull();
    }

    [Fact]
    public async Task An_expired_token_is_not_found()
    {
        var token = RefreshToken.Issue(UserId.New(), Guid.NewGuid().ToString("N"), Now.AddDays(-60), Now.AddDays(-30));

        await using var context = fixture.CreateIdentityContext();
        context.RefreshTokens.Add(token);
        await context.SaveChangesAsync();

        var found = await context.RefreshTokens.SingleOrDefaultAsync(
            t => t.TokenHash == token.TokenHash && t.RevokedAt == null && t.ExpiresAt > Now);

        found.ShouldBeNull();
    }
}
```

- [ ] **Step 7: Add the migration and run all tests**

```bash
dotnet ef migrations add AddRefreshTokens \
  --project src/Modules/Identity/MyGym.Modules.Identity.Infrastructure \
  --startup-project src/Modules/Identity/MyGym.Modules.Identity.Infrastructure \
  --context IdentityDbContext --output-dir Migrations
```

Run: `dotnet test`
Expected: PASS — all suites, including 9 validator tests and 2 refresh-token tests.

- [ ] **Step 8: Commit**

```bash
git add src/Shared/MyGym.Shared.Infrastructure src/Modules/Identity tests
git commit -m "feat: add Telegram initData validation and JWT token issuance"
```

---

### Task 11: API host, error mapping, and the auth and profile endpoints

**Files:**
- Create: `src/Host/MyGym.Api/{Program,AppSettingsOptions}.cs`
- Create: `src/Host/MyGym.Api/Errors/{ProblemDetailsMapper,ResultExtensions}.cs`
- Create: `src/Host/MyGym.Api/Auth/{CurrentUser,TelegramAuthEndpoints}.cs`
- Create: `src/Host/MyGym.Api/Endpoints/ProfileEndpoints.cs`
- Create: `src/Modules/Identity/MyGym.Modules.Identity.Application/{AuthenticateWithTelegram,RefreshTokens,GetProfile,UpdateProfile}.cs`
- Create: `src/Host/MyGym.Api/appsettings.json`, `src/Host/MyGym.Api/appsettings.Development.json`
- Test: `tests/MyGym.IntegrationTests/{ApiFixture,AuthEndpointTests,ProfileEndpointTests}.cs`

**Interfaces:**
- Consumes: everything from Tasks 1–10.
- Produces:
  - `ResultExtensions.ToHttpResult(Result)` / `ToHttpResult<T>(Result<T>, Func<T, IResult>?)` — maps a `Result` to `IResult` or RFC 9457 `ProblemDetails`
  - `ProblemDetailsMapper.StatusFor(string errorCode) → int`
  - `ICurrentUser` with `UserId UserId`, `bool IsAuthenticated`; implementation reading the `sub` claim
  - `AuthenticateWithTelegramCommand(string InitData)` → `AuthTokensResponse(string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAt)`; handler registers the user on first sight
  - `RefreshTokensCommand(string RefreshToken)` → `AuthTokensResponse`
  - `GetProfileQuery(UserId)` → `ProfileResponse`
  - `UpdateProfileCommand(UserId, int? BirthYear, decimal? Bodyweight, WeightUnit PreferredUnit, decimal LoadingIncrement, ExperienceLevel ExperienceLevel, int? AvailableDaysPerWeek, int? SessionMinutes, string TimeZoneId)` → `ProfileResponse`
  - `ApiFixture : WebApplicationFactory<Program>` with `CreateAuthenticatedClientAsync(long telegramUserId) → Task<HttpClient>` and `BuildInitData(long, string)`

> `ApiFixture` takes the existing `PostgresFixture` rather than defining its own xUnit collection,
> so every test class shares one PostgreSQL container. A second `[CollectionDefinition]` over the
> same fixture type would silently start a second container and double the suite's startup cost.

- [ ] **Step 1: Create the projects**

```bash
dotnet new web -o src/Host/MyGym.Api
dotnet new classlib -o src/Modules/Identity/MyGym.Modules.Identity.Application
dotnet sln add src/Host/MyGym.Api src/Modules/Identity/MyGym.Modules.Identity.Application

dotnet add src/Modules/Identity/MyGym.Modules.Identity.Application reference \
           src/Modules/Identity/MyGym.Modules.Identity.Domain src/Shared/MyGym.Shared.Infrastructure
dotnet add src/Host/MyGym.Api reference \
           src/Modules/Identity/MyGym.Modules.Identity.Application \
           src/Modules/Identity/MyGym.Modules.Identity.Infrastructure \
           src/Modules/ExerciseCatalog/MyGym.Modules.ExerciseCatalog.Infrastructure \
           src/Modules/Training/MyGym.Modules.Training.Infrastructure
dotnet add src/Host/MyGym.Api package Microsoft.AspNetCore.Authentication.JwtBearer
dotnet add src/Host/MyGym.Api package OpenTelemetry.Extensions.Hosting
dotnet add src/Host/MyGym.Api package OpenTelemetry.Instrumentation.AspNetCore
dotnet add src/Host/MyGym.Api package OpenTelemetry.Exporter.OpenTelemetryProtocol
dotnet add tests/MyGym.IntegrationTests reference src/Host/MyGym.Api
dotnet add tests/MyGym.IntegrationTests package Microsoft.AspNetCore.Mvc.Testing
rm src/Modules/Identity/MyGym.Modules.Identity.Application/Class1.cs
```

- [ ] **Step 2: Write the failing tests**

`tests/MyGym.IntegrationTests/ApiFixture.cs`:

```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MyGym.IntegrationTests;

public sealed class ApiFixture(PostgresFixture postgres) : WebApplicationFactory<Program>
{
    public const string BotToken = "123456:TEST-BOT-TOKEN-FOR-INTEGRATION";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Postgres", postgres.ConnectionString);
        builder.UseSetting("Telegram:BotToken", BotToken);
        builder.UseSetting("Telegram:WebhookSecretToken", "test-webhook-secret");
        builder.UseSetting("Jwt:Issuer", "mygym-tests");
        builder.UseSetting("Jwt:Audience", "mygym-tests");
        builder.UseSetting("Jwt:SigningKey", "test-signing-key-at-least-32-bytes-long!!");
        builder.UseSetting("RunMigrationsOnStartup", "false");
    }

    public async Task<HttpClient> CreateAuthenticatedClientAsync(long telegramUserId, string firstName = "Akmal")
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/auth/telegram", new { initData = BuildInitData(telegramUserId, firstName) });

        response.EnsureSuccessStatusCode();
        var tokens = await response.Content.ReadFromJsonAsync<AuthTokens>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);

        return client;
    }

    public static string BuildInitData(long telegramUserId, string firstName = "Akmal")
    {
        var user = $"{{\"id\":{telegramUserId},\"first_name\":\"{firstName}\",\"language_code\":\"uz\"}}";
        var fields = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["auth_date"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
            ["user"] = user
        };

        var dataCheckString = string.Join('\n', fields.Select(f => $"{f.Key}={f.Value}"));
        using var secretKeyHmac = new HMACSHA256(Encoding.UTF8.GetBytes("WebAppData"));
        var secretKey = secretKeyHmac.ComputeHash(Encoding.UTF8.GetBytes(BotToken));
        using var signatureHmac = new HMACSHA256(secretKey);
        var hash = Convert.ToHexStringLower(
            signatureHmac.ComputeHash(Encoding.UTF8.GetBytes(dataCheckString)));

        var parts = fields.Select(f => $"{f.Key}={WebUtility.UrlEncode(f.Value)}").ToList();
        parts.Add($"hash={hash}");
        return string.Join('&', parts);
    }

    public sealed record AuthTokens(string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAt);
}
```

`tests/MyGym.IntegrationTests/AuthEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Shouldly;

namespace MyGym.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public class AuthEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFixture _api = null!;

    public ValueTask InitializeAsync()
    {
        _api = new ApiFixture(postgres);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task Valid_init_data_issues_tokens_and_registers_the_user_on_first_sight()
    {
        var telegramId = Random.Shared.NextInt64(1, long.MaxValue);
        var client = _api.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/telegram", new { initData = ApiFixture.BuildInitData(telegramId) });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var tokens = await response.Content.ReadFromJsonAsync<ApiFixture.AuthTokens>();
        tokens!.AccessToken.ShouldNotBeNullOrWhiteSpace();
        tokens.RefreshToken.ShouldNotBeNullOrWhiteSpace();
        tokens.AccessTokenExpiresAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Signing_in_twice_reuses_the_same_account()
    {
        var telegramId = Random.Shared.NextInt64(1, long.MaxValue);

        var first = await _api.CreateAuthenticatedClientAsync(telegramId);
        var firstMe = await first.GetFromJsonAsync<ProfilePayload>("/api/me");

        var second = await _api.CreateAuthenticatedClientAsync(telegramId);
        var secondMe = await second.GetFromJsonAsync<ProfilePayload>("/api/me");

        secondMe!.Id.ShouldBe(firstMe!.Id);
    }

    [Fact]
    public async Task Tampered_init_data_is_rejected_with_a_stable_error_code()
    {
        var client = _api.CreateClient();
        var honest = ApiFixture.BuildInitData(Random.Shared.NextInt64(1, long.MaxValue));
        var forged = honest.Replace("hash=", "hash=0") [..^1];

        var response = await client.PostAsJsonAsync("/api/auth/telegram", new { initData = forged });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var problem = await response.Content.ReadFromJsonAsync<ProblemPayload>();
        problem!.ErrorCode.ShouldStartWith("auth.");
    }

    [Fact]
    public async Task A_refresh_token_exchanges_for_a_new_access_token()
    {
        var client = _api.CreateClient();
        var signIn = await client.PostAsJsonAsync(
            "/api/auth/telegram",
            new { initData = ApiFixture.BuildInitData(Random.Shared.NextInt64(1, long.MaxValue)) });
        var tokens = await signIn.Content.ReadFromJsonAsync<ApiFixture.AuthTokens>();

        var response = await client.PostAsJsonAsync(
            "/api/auth/refresh", new { refreshToken = tokens!.RefreshToken });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var refreshed = await response.Content.ReadFromJsonAsync<ApiFixture.AuthTokens>();
        refreshed!.AccessToken.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task An_unknown_refresh_token_is_rejected()
    {
        var client = _api.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/refresh", new { refreshToken = "not-a-real-token" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Protected_endpoints_require_a_token()
    {
        var response = await _api.CreateClient().GetAsync("/api/me");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    internal sealed record ProfilePayload(Guid Id, string FirstName, string TimeZoneId);
    internal sealed record ProblemPayload(string Title, int Status, string ErrorCode);
}
```

`tests/MyGym.IntegrationTests/ProfileEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Shouldly;

namespace MyGym.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public class ProfileEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFixture _api = null!;

    public ValueTask InitializeAsync()
    {
        _api = new ApiFixture(postgres);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task A_profile_can_be_updated_and_read_back()
    {
        var client = await _api.CreateAuthenticatedClientAsync(Random.Shared.NextInt64(1, long.MaxValue));

        var update = await client.PatchAsJsonAsync("/api/me", new
        {
            birthYear = 1996,
            bodyweight = 74.5m,
            preferredUnit = "Kilograms",
            loadingIncrement = 2.5m,
            experienceLevel = "Intermediate",
            availableDaysPerWeek = 3,
            sessionMinutes = 90,
            timeZoneId = "Asia/Tashkent"
        });

        update.StatusCode.ShouldBe(HttpStatusCode.OK);

        var profile = await client.GetFromJsonAsync<ProfilePayload>("/api/me");
        profile!.BirthYear.ShouldBe(1996);
        profile.Bodyweight.ShouldBe(74.5m);
        profile.AvailableDaysPerWeek.ShouldBe(3);
        profile.TimeZoneId.ShouldBe("Asia/Tashkent");
    }

    [Fact]
    public async Task An_invalid_profile_is_rejected_with_its_domain_error_code()
    {
        var client = await _api.CreateAuthenticatedClientAsync(Random.Shared.NextInt64(1, long.MaxValue));

        var response = await client.PatchAsJsonAsync("/api/me", new
        {
            birthYear = 1850,
            preferredUnit = "Kilograms",
            loadingIncrement = 2.5m,
            experienceLevel = "Beginner",
            timeZoneId = "UTC"
        });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableContent);
        var problem = await response.Content.ReadFromJsonAsync<ProblemPayload>();
        problem!.ErrorCode.ShouldBe("profile.birth_year_out_of_bounds");
    }

    internal sealed record ProfilePayload(
        Guid Id, string FirstName, int? BirthYear, decimal? Bodyweight,
        int? AvailableDaysPerWeek, int? SessionMinutes, string TimeZoneId);

    internal sealed record ProblemPayload(string Title, int Status, string ErrorCode);
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/MyGym.IntegrationTests`
Expected: FAIL — `Program` is not accessible / endpoints do not exist.

- [ ] **Step 4: Write the error mapping**

`src/Host/MyGym.Api/Errors/ProblemDetailsMapper.cs`:

```csharp
using MyGym.Modules.ExerciseCatalog.Domain;
using MyGym.Modules.Identity.Domain;
using MyGym.Modules.Training.Application.Programs;
using MyGym.Modules.Training.Application.Sessions;
using MyGym.Modules.Training.Domain;
using MyGym.Shared.Infrastructure.Auth;

namespace MyGym.Api.Errors;

public static class ProblemDetailsMapper
{
    /// <summary>
    /// Maps a stable domain error code to an HTTP status. Conflicts are state problems the client
    /// cannot fix by editing its payload; 422 means the payload itself was unacceptable.
    /// </summary>
    public static int StatusFor(string errorCode) => errorCode switch
    {
        AuthErrors.InitDataMalformed => StatusCodes.Status400BadRequest,
        AuthErrors.InitDataSignatureInvalid => StatusCodes.Status401Unauthorized,
        AuthErrors.InitDataExpired => StatusCodes.Status401Unauthorized,
        AuthErrors.RefreshTokenInvalid => StatusCodes.Status401Unauthorized,

        IdentityErrors.UserNotFound => StatusCodes.Status404NotFound,
        CatalogErrors.ExerciseNotFound => StatusCodes.Status404NotFound,
        TrainingErrors.ProgramDayNotFound => StatusCodes.Status404NotFound,
        TrainingErrors.SessionExerciseNotFound => StatusCodes.Status404NotFound,
        TrainingErrors.SetNotFound => StatusCodes.Status404NotFound,
        ProgramErrors.NotFound => StatusCodes.Status404NotFound,
        SessionErrors.NotFound => StatusCodes.Status404NotFound,

        TrainingErrors.SessionAlreadyInProgress => StatusCodes.Status409Conflict,
        TrainingErrors.SessionClosed => StatusCodes.Status409Conflict,
        TrainingErrors.ProgramAlreadyActive => StatusCodes.Status409Conflict,
        TrainingErrors.ProgramArchived => StatusCodes.Status409Conflict,
        TrainingErrors.ProgramNotEditable => StatusCodes.Status409Conflict,

        _ => StatusCodes.Status422UnprocessableEntity
    };
}
```

`src/Host/MyGym.Api/Errors/ResultExtensions.cs`:

```csharp
using MyGym.SharedKernel;

namespace MyGym.Api.Errors;

public static class ResultExtensions
{
    public static IResult ToHttpResult(this Result result) =>
        result.IsSuccess ? Results.NoContent() : Problem(result.Error);

    public static IResult ToHttpResult<T>(this Result<T> result, Func<T, IResult>? onSuccess = null) =>
        result.IsSuccess
            ? onSuccess?.Invoke(result.Value) ?? Results.Ok(result.Value)
            : Problem(result.Error);

    private static IResult Problem(Error error)
    {
        var status = ProblemDetailsMapper.StatusFor(error.Code);

        return Results.Problem(
            title: error.Message,
            statusCode: status,
            type: $"https://mygym.app/errors/{error.Code}",
            extensions: new Dictionary<string, object?> { ["errorCode"] = error.Code });
    }
}
```

> `errorCode` is the contract the Mini App localizes from. `title` is a developer-facing English
> message and must never be shown to an athlete.

- [ ] **Step 5: Write the Identity application handlers**

`src/Modules/Identity/MyGym.Modules.Identity.Application/AuthenticateWithTelegram.cs`:

```csharp
using MyGym.Modules.Identity.Domain;
using MyGym.Shared.Infrastructure.Auth;
using MyGym.SharedKernel;

namespace MyGym.Modules.Identity.Application;

public sealed record AuthenticateWithTelegramCommand(string InitData);

public sealed record AuthTokensResponse(
    string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAt);

public sealed class AuthenticateWithTelegramCommandHandler(
    ITelegramInitDataValidator validator,
    IAppUserRepository users,
    IRefreshTokenRepository refreshTokens,
    IJwtTokenService tokens,
    IIdentityUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    Microsoft.Extensions.Options.IOptions<JwtOptions> jwtOptions)
    : ICommandHandler<AuthenticateWithTelegramCommand, AuthTokensResponse>
{
    public async Task<Result<AuthTokensResponse>> HandleAsync(
        AuthenticateWithTelegramCommand command, CancellationToken ct)
    {
        var validated = validator.Validate(command.InitData);
        if (validated.IsFailure)
            return Result<AuthTokensResponse>.Failure(validated.Error);

        var info = validated.Value;
        var now = timeProvider.GetUtcNow();

        var user = await users.GetByTelegramUserIdAsync(info.TelegramUserId, ct);
        if (user is null)
        {
            var registered = AppUser.Register(
                info.TelegramUserId, info.FirstName, info.Username, info.LanguageCode, now);

            if (registered.IsFailure)
                return Result<AuthTokensResponse>.Failure(registered.Error);

            user = registered.Value;
            await users.AddAsync(user, ct);
        }
        else
        {
            user.TouchLastSeen(now);
        }

        var (refreshToken, refreshTokenHash) = tokens.CreateRefreshToken();
        await refreshTokens.AddAsync(
            RefreshToken.Issue(user.Id, refreshTokenHash, now, now.Add(jwtOptions.Value.RefreshTokenLifetime)),
            ct);

        await unitOfWork.SaveChangesAsync(ct);

        return Result<AuthTokensResponse>.Success(new AuthTokensResponse(
            tokens.CreateAccessToken(user.Id),
            refreshToken,
            now.Add(jwtOptions.Value.AccessTokenLifetime)));
    }
}
```

`src/Modules/Identity/MyGym.Modules.Identity.Application/RefreshTokens.cs`:

```csharp
using Microsoft.Extensions.Options;
using MyGym.Modules.Identity.Domain;
using MyGym.Shared.Infrastructure.Auth;
using MyGym.SharedKernel;

namespace MyGym.Modules.Identity.Application;

public sealed record RefreshTokensCommand(string RefreshToken);

public sealed class RefreshTokensCommandHandler(
    IRefreshTokenRepository refreshTokens,
    IJwtTokenService tokens,
    IIdentityUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    IOptions<JwtOptions> jwtOptions)
    : ICommandHandler<RefreshTokensCommand, AuthTokensResponse>
{
    public async Task<Result<AuthTokensResponse>> HandleAsync(RefreshTokensCommand command, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var hash = tokens.HashRefreshToken(command.RefreshToken);

        var stored = await refreshTokens.FindActiveAsync(hash, now, ct);
        if (stored is null)
            return Result<AuthTokensResponse>.Failure(
                new Error(AuthErrors.RefreshTokenInvalid, "Refresh token is not valid."));

        // Rotate: the presented token is spent, a fresh one is issued.
        stored.Revoke(now);
        var (newToken, newHash) = tokens.CreateRefreshToken();
        await refreshTokens.AddAsync(
            RefreshToken.Issue(stored.UserId, newHash, now, now.Add(jwtOptions.Value.RefreshTokenLifetime)), ct);

        await unitOfWork.SaveChangesAsync(ct);

        return Result<AuthTokensResponse>.Success(new AuthTokensResponse(
            tokens.CreateAccessToken(stored.UserId),
            newToken,
            now.Add(jwtOptions.Value.AccessTokenLifetime)));
    }
}
```

`src/Modules/Identity/MyGym.Modules.Identity.Application/Profile.cs`:

```csharp
using MyGym.Modules.Identity.Domain;
using MyGym.SharedKernel;

namespace MyGym.Modules.Identity.Application;

public sealed record ProfileResponse(
    Guid Id,
    string FirstName,
    string? Username,
    string? LanguageCode,
    int? BirthYear,
    decimal? Bodyweight,
    WeightUnit PreferredUnit,
    decimal LoadingIncrement,
    ExperienceLevel ExperienceLevel,
    int? AvailableDaysPerWeek,
    int? SessionMinutes,
    string TimeZoneId)
{
    public static ProfileResponse From(AppUser user) => new(
        user.Id.Value, user.FirstName, user.Username, user.LanguageCode,
        user.Profile.BirthYear, user.Profile.Bodyweight?.Value, user.Profile.PreferredUnit,
        user.Profile.LoadingIncrement.Value, user.Profile.ExperienceLevel,
        user.Profile.AvailableDaysPerWeek, user.Profile.SessionMinutes, user.Profile.TimeZoneId);
}

public sealed record GetProfileQuery(UserId UserId);

public sealed class GetProfileQueryHandler(IAppUserRepository users)
    : IQueryHandler<GetProfileQuery, ProfileResponse>
{
    public async Task<Result<ProfileResponse>> HandleAsync(GetProfileQuery query, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(query.UserId, ct);
        return user is null
            ? Result<ProfileResponse>.Failure(new Error(IdentityErrors.UserNotFound, "User not found."))
            : Result<ProfileResponse>.Success(ProfileResponse.From(user));
    }
}

public sealed record UpdateProfileCommand(
    UserId UserId,
    int? BirthYear,
    decimal? Bodyweight,
    WeightUnit PreferredUnit,
    decimal LoadingIncrement,
    ExperienceLevel ExperienceLevel,
    int? AvailableDaysPerWeek,
    int? SessionMinutes,
    string TimeZoneId);

public sealed class UpdateProfileCommandHandler(IAppUserRepository users, IIdentityUnitOfWork unitOfWork)
    : ICommandHandler<UpdateProfileCommand, ProfileResponse>
{
    public async Task<Result<ProfileResponse>> HandleAsync(UpdateProfileCommand command, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(command.UserId, ct);
        if (user is null)
            return Result<ProfileResponse>.Failure(new Error(IdentityErrors.UserNotFound, "User not found."));

        Weight? bodyweight = null;
        if (command.Bodyweight is { } value)
        {
            var created = Weight.Create(value, command.PreferredUnit);
            if (created.IsFailure)
                return Result<ProfileResponse>.Failure(created.Error);

            bodyweight = created.Value;
        }

        var increment = WeightIncrement.Create(command.LoadingIncrement, command.PreferredUnit);
        if (increment.IsFailure)
            return Result<ProfileResponse>.Failure(increment.Error);

        var profile = AthleteProfile.Create(
            command.BirthYear, bodyweight, command.PreferredUnit, increment.Value,
            command.ExperienceLevel, command.AvailableDaysPerWeek, command.SessionMinutes,
            command.TimeZoneId);

        if (profile.IsFailure)
            return Result<ProfileResponse>.Failure(profile.Error);

        user.UpdateProfile(profile.Value);
        await unitOfWork.SaveChangesAsync(ct);

        return Result<ProfileResponse>.Success(ProfileResponse.From(user));
    }
}
```

- [ ] **Step 6: Write the composition root and endpoints**

`src/Host/MyGym.Api/Auth/CurrentUser.cs`:

```csharp
using System.Security.Claims;
using MyGym.SharedKernel;

namespace MyGym.Api.Auth;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    UserId UserId { get; }
}

public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public bool IsAuthenticated => TryGetUserId(out _);

    public UserId UserId => TryGetUserId(out var userId)
        ? userId
        : throw new InvalidOperationException("No authenticated user on this request.");

    private bool TryGetUserId(out UserId userId)
    {
        userId = default;
        var raw = accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? accessor.HttpContext?.User.FindFirstValue("sub");

        if (!Guid.TryParse(raw, out var parsed))
            return false;

        userId = SharedKernel.UserId.From(parsed);
        return true;
    }
}
```

`src/Host/MyGym.Api/Auth/TelegramAuthEndpoints.cs`:

```csharp
using MyGym.Api.Errors;
using MyGym.Modules.Identity.Application;
using MyGym.SharedKernel;

namespace MyGym.Api.Auth;

public static class TelegramAuthEndpoints
{
    public sealed record TelegramSignInRequest(string InitData);
    public sealed record RefreshRequest(string RefreshToken);

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").AllowAnonymous();

        group.MapPost("/telegram", async (
            TelegramSignInRequest request,
            ICommandHandler<AuthenticateWithTelegramCommand, AuthTokensResponse> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new AuthenticateWithTelegramCommand(request.InitData), ct);
            return result.ToHttpResult();
        });

        group.MapPost("/refresh", async (
            RefreshRequest request,
            ICommandHandler<RefreshTokensCommand, AuthTokensResponse> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new RefreshTokensCommand(request.RefreshToken), ct);
            return result.ToHttpResult();
        });
    }
}
```

`src/Host/MyGym.Api/Endpoints/ProfileEndpoints.cs`:

```csharp
using MyGym.Api.Auth;
using MyGym.Api.Errors;
using MyGym.Modules.Identity.Application;
using MyGym.Modules.Identity.Domain;
using MyGym.SharedKernel;

namespace MyGym.Api.Endpoints;

public static class ProfileEndpoints
{
    public sealed record UpdateProfileRequest(
        int? BirthYear,
        decimal? Bodyweight,
        WeightUnit PreferredUnit,
        decimal LoadingIncrement,
        ExperienceLevel ExperienceLevel,
        int? AvailableDaysPerWeek,
        int? SessionMinutes,
        string TimeZoneId);

    public static void MapProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/me").RequireAuthorization();

        group.MapGet("", async (
            ICurrentUser currentUser,
            IQueryHandler<GetProfileQuery, ProfileResponse> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetProfileQuery(currentUser.UserId), ct);
            return result.ToHttpResult();
        });

        group.MapPatch("", async (
            UpdateProfileRequest request,
            ICurrentUser currentUser,
            ICommandHandler<UpdateProfileCommand, ProfileResponse> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new UpdateProfileCommand(
                currentUser.UserId, request.BirthYear, request.Bodyweight, request.PreferredUnit,
                request.LoadingIncrement, request.ExperienceLevel, request.AvailableDaysPerWeek,
                request.SessionMinutes, request.TimeZoneId), ct);

            return result.ToHttpResult();
        });
    }
}
```

`src/Host/MyGym.Api/Program.cs`:

```csharp
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MyGym.Api.Auth;
using MyGym.Api.Endpoints;
using MyGym.Modules.ExerciseCatalog.Infrastructure;
using MyGym.Modules.Identity.Application;
using MyGym.Modules.Identity.Infrastructure;
using MyGym.Modules.Training.Infrastructure;
using MyGym.Shared.Infrastructure.Auth;
using MyGym.Shared.Infrastructure.Persistence;
using MyGym.SharedKernel;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required.");

builder.Services.Configure<TelegramAuthOptions>(
    builder.Configuration.GetSection(TelegramAuthOptions.SectionName));
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Modules
builder.Services.AddIdentityInfrastructure(connectionString);
builder.Services.AddCatalogInfrastructure(connectionString);
builder.Services.AddTrainingInfrastructure(connectionString);

// Shared services
builder.Services.AddSingleton<ITelegramInitDataValidator, TelegramInitDataValidator>();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

// Handlers — registered explicitly; there is no mediator scanning assemblies at startup.
builder.Services.AddScoped<ICommandHandler<AuthenticateWithTelegramCommand, AuthTokensResponse>,
    AuthenticateWithTelegramCommandHandler>();
builder.Services.AddScoped<ICommandHandler<RefreshTokensCommand, AuthTokensResponse>,
    RefreshTokensCommandHandler>();
builder.Services.AddScoped<IQueryHandler<GetProfileQuery, ProfileResponse>, GetProfileQueryHandler>();
builder.Services.AddScoped<ICommandHandler<UpdateProfileCommand, ProfileResponse>, UpdateProfileCommandHandler>();

var jwt = builder.Configuration.GetSection(JwtOptions.SectionName);
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = jwt["Issuer"],
        ValidAudience = jwt["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["SigningKey"]!)),
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateIssuerSigningKey = true,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30)
    });
builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetTokenBucketLimiter(
            context.User.FindFirst("sub")?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = 120,
                TokensPerPeriod = 120,
                ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation().AddOtlpExporter())
    .WithMetrics(metrics => metrics.AddAspNetCoreInstrumentation().AddOtlpExporter());

// Request body cap. Nothing this API accepts is legitimately larger than a program with its days,
// so 256 KB is generous; the logger has no upload endpoints at all.
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = 256 * 1024);

var app = builder.Build();

if (app.Configuration.GetValue("RunMigrationsOnStartup", defaultValue: true))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<TrainingDbContext>().Database.MigrateAsync();
    await CatalogSeeder.SeedAsync(
        scope.ServiceProvider.GetRequiredService<CatalogDbContext>(), CancellationToken.None);
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapAuthEndpoints();
app.MapProfileEndpoints();

app.Run();

public partial class Program;
```

`src/Host/MyGym.Api/appsettings.json`:

```json
{
  "ConnectionStrings": {
    "Postgres": "Host=localhost;Port=5432;Database=mygym;Username=mygym;Password=change-me"
  },
  "Telegram": {
    "BotToken": "set-via-environment",
    "WebhookSecretToken": "set-via-environment",
    "MaxInitDataAge": "01:00:00"
  },
  "Jwt": {
    "Issuer": "mygym",
    "Audience": "mygym-miniapp",
    "SigningKey": "set-via-environment-at-least-32-bytes",
    "AccessTokenLifetime": "00:15:00",
    "RefreshTokenLifetime": "30.00:00:00"
  },
  "Logging": {
    "LogLevel": { "Default": "Information", "Microsoft.AspNetCore": "Warning" }
  },
  "AllowedHosts": "*"
}
```

> Secrets are placeholders by design. Supply `Telegram__BotToken`, `Telegram__WebhookSecretToken`,
> and `Jwt__SigningKey` through environment variables; never commit real values.

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/MyGym.IntegrationTests`
Expected: PASS — 8 new endpoint tests plus the persistence suites.

- [ ] **Step 8: Commit**

```bash
git add src/Host src/Modules/Identity tests/MyGym.IntegrationTests
git commit -m "feat: add API host with Telegram sign-in, token refresh, and profile endpoints"
```

---

### Task 12: Program authoring endpoints and the day preview

The preview endpoint is where the domain work from Task 3 reaches the athlete: it returns a program
day with every warm-up already resolved to loadable weights.

**Files:**
- Create: `src/Modules/ExerciseCatalog/MyGym.Modules.ExerciseCatalog.Contracts/IExerciseCatalogQueries.cs`
- Create: `src/Modules/ExerciseCatalog/MyGym.Modules.ExerciseCatalog.Infrastructure/ExerciseCatalogQueries.cs`
- Create: `src/Modules/Identity/MyGym.Modules.Identity.Contracts/IAthleteProfileQueries.cs`
- Create: `src/Modules/Identity/MyGym.Modules.Identity.Infrastructure/AthleteProfileQueries.cs`
- Create: `src/Modules/Training/MyGym.Modules.Training.Application/Programs/{ProgramDtos,CreateProgram,ReplaceProgram,GetProgram,ListPrograms,ActivateProgram,ForkProgram,ArchiveProgram,PreviewProgramDay}.cs`
- Create: `src/Modules/Training/MyGym.Modules.Training.Application/ILastWorkingWeightQuery.cs`
- Create: `src/Modules/Training/MyGym.Modules.Training.Infrastructure/{LastWorkingWeightQuery,ListProgramsQueryHandler}.cs`
- Create: `src/Host/MyGym.Api/Endpoints/{ProgramEndpoints,ExerciseEndpoints}.cs`
- Test: `tests/MyGym.IntegrationTests/ProgramEndpointTests.cs`

**Interfaces:**
- Consumes: Tasks 1–11.
- Produces:
  - `IExerciseCatalogQueries` (in Catalog **Contracts**) with `AllExistAsync(IReadOnlyCollection<ExerciseId>, CancellationToken) → Task<bool>` and `SearchAsync(string? term, string languageCode, CancellationToken) → Task<IReadOnlyList<ExerciseSummary>>`, where `ExerciseSummary(Guid Id, string Slug, string Name, string Equipment, string[] PrimaryMuscles)`
  - `IAthleteProfileQueries` (in Identity **Contracts**) with `GetLoadingPreferencesAsync(UserId, CancellationToken) → Task<LoadingPreferences?>`, where `LoadingPreferences(WeightUnit PreferredUnit, decimal Increment)`
  - `WarmupStepInput(decimal PercentOfWorking, int Reps, bool IsOptional)`, `PrescriptionInput(Guid ExerciseId, Guid? AlternativeExerciseId, int WorkingSetCount, int RepMin, int RepMax, int RestMinSeconds, int RestMaxSeconds, IReadOnlyList<WarmupStepInput> Warmup, string? Notes)`, `ProgramDayInput(string Label, DayOfWeek? DayOfWeek, IReadOnlyList<PrescriptionInput> Prescriptions)`
  - `ProgramResponse`, `ProgramDayResponse`, `PrescriptionResponse`, `ProgramSummaryResponse`, `DayPreviewResponse`, `PreviewExerciseResponse`, `ResolvedWarmupResponse`
  - Commands/queries: `CreateProgramCommand`, `ReplaceProgramCommand`, `GetProgramQuery`, `ListProgramsQuery`, `ActivateProgramCommand`, `ForkProgramCommand`, `ArchiveProgramCommand`, `PreviewProgramDayQuery` — each with its handler
  - `ILastWorkingWeightQuery` with `GetTopWorkingWeightsAsync(UserId, IReadOnlyCollection<ExerciseId>, CancellationToken) → Task<IReadOnlyDictionary<ExerciseId, Weight>>`

> **Why the two new Contracts interfaces:** `Training.Application` must validate exercise ids and
> read the athlete's plate increment, but the boundary rule forbids it from referencing
> `ExerciseCatalog.Domain` or `Identity.Domain`. Both reads go through `Contracts` interfaces
> implemented in the owning module's Infrastructure. Task 15's architecture test enforces this.

- [ ] **Step 1: Create the projects and cross-module contracts**

```bash
dotnet new classlib -o src/Modules/Training/MyGym.Modules.Training.Application
dotnet sln add src/Modules/Training/MyGym.Modules.Training.Application
dotnet add src/Modules/Training/MyGym.Modules.Training.Application reference \
           src/Modules/Training/MyGym.Modules.Training.Domain \
           src/Modules/ExerciseCatalog/MyGym.Modules.ExerciseCatalog.Contracts \
           src/Modules/Identity/MyGym.Modules.Identity.Contracts
dotnet add src/Host/MyGym.Api reference src/Modules/Training/MyGym.Modules.Training.Application
rm src/Modules/Training/MyGym.Modules.Training.Application/Class1.cs
```

`src/Modules/ExerciseCatalog/MyGym.Modules.ExerciseCatalog.Contracts/IExerciseCatalogQueries.cs`:

```csharp
using MyGym.SharedKernel;

namespace MyGym.Modules.ExerciseCatalog.Contracts;

public sealed record ExerciseSummary(
    Guid Id, string Slug, string Name, string Equipment, string[] PrimaryMuscles);

/// <summary>The catalog's public read surface. Other modules use this and nothing else.</summary>
public interface IExerciseCatalogQueries
{
    Task<bool> AllExistAsync(IReadOnlyCollection<ExerciseId> ids, CancellationToken ct);
    Task<IReadOnlyList<ExerciseSummary>> SearchAsync(string? term, string languageCode, CancellationToken ct);
}
```

`src/Modules/ExerciseCatalog/MyGym.Modules.ExerciseCatalog.Infrastructure/ExerciseCatalogQueries.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using MyGym.Modules.ExerciseCatalog.Contracts;
using MyGym.SharedKernel;

namespace MyGym.Modules.ExerciseCatalog.Infrastructure;

internal sealed class ExerciseCatalogQueries(CatalogDbContext context) : IExerciseCatalogQueries
{
    public async Task<bool> AllExistAsync(IReadOnlyCollection<ExerciseId> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
            return true;

        var distinct = ids.Distinct().ToList();
        var found = await context.Exercises.CountAsync(e => distinct.Contains(e.Id), ct);
        return found == distinct.Count;
    }

    public async Task<IReadOnlyList<ExerciseSummary>> SearchAsync(
        string? term, string languageCode, CancellationToken ct)
    {
        var query = context.Exercises.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(term))
        {
            var pattern = $"%{term.Trim()}%";
            query = query.Where(e =>
                EF.Functions.ILike(e.Slug, pattern)
                || e.Aliases.Any(a => EF.Functions.ILike(a, pattern))
                || e.Names.Any(n => EF.Functions.ILike(n.Value, pattern)));
        }

        var exercises = await query.OrderBy(e => e.Slug).Take(100).ToListAsync(ct);

        return exercises
            .Select(e => new ExerciseSummary(
                e.Id.Value, e.Slug, e.NameFor(languageCode), e.Equipment.ToString(),
                e.PrimaryMuscles.Select(m => m.ToString()).ToArray()))
            .ToList();
    }
}
```

Register it in `CatalogInfrastructureExtensions.AddCatalogInfrastructure`:

```csharp
        services.AddScoped<IExerciseCatalogQueries, ExerciseCatalogQueries>();
```

`src/Modules/Identity/MyGym.Modules.Identity.Contracts/IAthleteProfileQueries.cs`:

```csharp
using MyGym.SharedKernel;

namespace MyGym.Modules.Identity.Contracts;

public sealed record LoadingPreferences(WeightUnit PreferredUnit, decimal Increment);

public interface IAthleteProfileQueries
{
    Task<LoadingPreferences?> GetLoadingPreferencesAsync(UserId userId, CancellationToken ct);
}
```

`src/Modules/Identity/MyGym.Modules.Identity.Infrastructure/AthleteProfileQueries.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using MyGym.Modules.Identity.Contracts;
using MyGym.SharedKernel;

namespace MyGym.Modules.Identity.Infrastructure;

internal sealed class AthleteProfileQueries(IdentityDbContext context) : IAthleteProfileQueries
{
    public Task<LoadingPreferences?> GetLoadingPreferencesAsync(UserId userId, CancellationToken ct) =>
        context.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new LoadingPreferences(
                u.Profile.PreferredUnit, u.Profile.LoadingIncrement.Value))
            .SingleOrDefaultAsync(ct);
}
```

Register it in `IdentityInfrastructureExtensions.AddIdentityInfrastructure`:

```csharp
        services.AddScoped<IAthleteProfileQueries, AthleteProfileQueries>();
```

- [ ] **Step 2: Write the failing tests**

`tests/MyGym.IntegrationTests/ProgramEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace MyGym.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public class ProgramEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFixture _api = null!;

    public async ValueTask InitializeAsync()
    {
        _api = new ApiFixture(postgres);
        await using var catalog = postgres.CreateCatalogContext();
        await MyGym.Modules.ExerciseCatalog.Infrastructure.CatalogSeeder.SeedAsync(
            catalog, CancellationToken.None);
    }

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    private async Task<(HttpClient Client, Guid BenchId, Guid RowId)> SetupAsync()
    {
        var client = await _api.CreateAuthenticatedClientAsync(Random.Shared.NextInt64(1, long.MaxValue));

        await using var catalog = postgres.CreateCatalogContext();
        var bench = await catalog.Exercises.SingleAsync(e => e.Slug == "flat-barbell-bench-press");
        var row = await catalog.Exercises.SingleAsync(e => e.Slug == "seated-cable-row");

        return (client, bench.Id.Value, row.Id.Value);
    }

    private static object BuildProgramBody(Guid benchId, Guid rowId) =>
        new { name = "3 Kunlik Muscle Gain", days = BuildDays(benchId, rowId) };

    private static object[] BuildDays(Guid benchId, Guid rowId) =>
        new object[]
        {
            new
            {
                label = "Juma — Full Body",
                dayOfWeek = "Friday",
                prescriptions = new object[]
                {
                    new
                    {
                        exerciseId = benchId,
                        alternativeExerciseId = (Guid?)null,
                        workingSetCount = 3,
                        repMin = 6,
                        repMax = 8,
                        restMinSeconds = 150,
                        restMaxSeconds = 180,
                        warmup = new[]
                        {
                            new { percentOfWorking = 0.27m, reps = 10, isOptional = false },
                            new { percentOfWorking = 0.53m, reps = 6, isOptional = false },
                            new { percentOfWorking = 0.80m, reps = 3, isOptional = false }
                        },
                        notes = (string?)null
                    },
                    new
                    {
                        exerciseId = rowId,
                        alternativeExerciseId = (Guid?)null,
                        workingSetCount = 3,
                        repMin = 8,
                        repMax = 10,
                        restMinSeconds = 180,
                        restMaxSeconds = 180,
                        warmup = new[]
                        {
                            new { percentOfWorking = 0.55m, reps = 8, isOptional = false }
                        },
                        notes = (string?)null
                    }
                }
            }
        };

    [Fact]
    public async Task A_program_is_created_as_a_draft_and_read_back_in_full()
    {
        var (client, benchId, rowId) = await SetupAsync();

        var created = await client.PostAsJsonAsync("/api/programs", BuildProgramBody(benchId, rowId));
        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        var program = await created.Content.ReadFromJsonAsync<ProgramPayload>();
        program!.Status.ShouldBe("Draft");
        program.RevisionNumber.ShouldBeGreaterThan(0);
        program.Days.ShouldHaveSingleItem();
        program.Days[0].Prescriptions.Count.ShouldBe(2);
        program.Days[0].Prescriptions[0].Warmup.Count.ShouldBe(3);

        var fetched = await client.GetFromJsonAsync<ProgramPayload>($"/api/programs/{program.Id}");
        fetched!.Name.ShouldBe("3 Kunlik Muscle Gain");
    }

    [Fact]
    public async Task An_unknown_exercise_id_is_rejected()
    {
        var (client, _, rowId) = await SetupAsync();

        var response = await client.PostAsJsonAsync(
            "/api/programs", BuildProgramBody(Guid.CreateVersion7(), rowId));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableContent);
        var problem = await response.Content.ReadFromJsonAsync<ProblemPayload>();
        problem!.ErrorCode.ShouldBe("exercise.not_found");
    }

    [Fact]
    public async Task A_descending_warmup_ramp_is_rejected_with_its_domain_error_code()
    {
        var (client, benchId, rowId) = await SetupAsync();
        var body = new
        {
            name = "Bad ramp",
            days = new[]
            {
                new
                {
                    label = "Day",
                    dayOfWeek = (string?)null,
                    prescriptions = new[]
                    {
                        new
                        {
                            exerciseId = benchId,
                            alternativeExerciseId = (Guid?)null,
                            workingSetCount = 3,
                            repMin = 6,
                            repMax = 8,
                            restMinSeconds = 180,
                            restMaxSeconds = 180,
                            warmup = new[]
                            {
                                new { percentOfWorking = 0.80m, reps = 3, isOptional = false },
                                new { percentOfWorking = 0.50m, reps = 8, isOptional = false }
                            },
                            notes = (string?)null
                        }
                    }
                }
            }
        };

        var response = await client.PostAsJsonAsync("/api/programs", body);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableContent);
        var problem = await response.Content.ReadFromJsonAsync<ProblemPayload>();
        problem!.ErrorCode.ShouldBe("warmup.percent_not_ascending");
    }

    [Fact]
    public async Task Activating_a_program_succeeds_and_a_second_activation_conflicts()
    {
        var (client, benchId, rowId) = await SetupAsync();
        var program = await CreateAsync(client, benchId, rowId);

        (await client.PostAsync($"/api/programs/{program.Id}/activate", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await client.PostAsync($"/api/programs/{program.Id}/activate", null))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Replacing_a_program_bumps_its_revision()
    {
        var (client, benchId, rowId) = await SetupAsync();
        var program = await CreateAsync(client, benchId, rowId);

        var replaced = await client.PutAsJsonAsync(
            $"/api/programs/{program.Id}",
            new { name = "Renamed block", days = BuildDays(benchId, rowId) });

        replaced.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await replaced.Content.ReadFromJsonAsync<ProgramPayload>();
        updated!.Name.ShouldBe("Renamed block");
        updated.RevisionNumber.ShouldBeGreaterThan(program.RevisionNumber);
    }

    [Fact]
    public async Task Another_athlete_cannot_read_a_private_program()
    {
        var (owner, benchId, rowId) = await SetupAsync();
        var program = await CreateAsync(owner, benchId, rowId);

        var stranger = await _api.CreateAuthenticatedClientAsync(Random.Shared.NextInt64(1, long.MaxValue));

        (await stranger.GetAsync($"/api/programs/{program.Id}")).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_public_program_can_be_forked_by_another_athlete()
    {
        var (owner, benchId, rowId) = await SetupAsync();
        var program = await CreateAsync(owner, benchId, rowId);
        await owner.PostAsync($"/api/programs/{program.Id}/activate", null);
        await owner.PatchAsJsonAsync($"/api/programs/{program.Id}/visibility", new { visibility = "Public" });

        var stranger = await _api.CreateAuthenticatedClientAsync(Random.Shared.NextInt64(1, long.MaxValue));
        var forked = await stranger.PostAsync($"/api/programs/{program.Id}/fork", null);

        forked.StatusCode.ShouldBe(HttpStatusCode.Created);
        var fork = await forked.Content.ReadFromJsonAsync<ProgramPayload>();
        fork!.Id.ShouldNotBe(program.Id);
        fork.Status.ShouldBe("Draft");
        fork.Days[0].Prescriptions.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_day_preview_resolves_every_warmup_to_a_loadable_weight()
    {
        var (client, benchId, rowId) = await SetupAsync();
        var program = await CreateAsync(client, benchId, rowId);
        var dayId = program.Days[0].Id;

        var preview = await client.GetFromJsonAsync<DayPreviewPayload>(
            $"/api/programs/{program.Id}/days/{dayId}/preview");

        preview!.Exercises.Count.ShouldBe(2);
        var bench = preview.Exercises[0];
        bench.TargetRepMin.ShouldBe(6);
        bench.TargetRepMax.ShouldBe(8);
        bench.WorkingSetCount.ShouldBe(3);
        bench.Warmup.Count.ShouldBe(3);

        // No history yet, so there is no suggested working weight and warm-ups resolve to zero.
        bench.SuggestedWorkingWeight.ShouldBeNull();
        bench.Warmup.ShouldAllBe(step => step.Weight == 0m);

        // Every resolved weight must sit on the athlete's 2.5 kg increment.
        foreach (var step in bench.Warmup)
            (step.Weight % 2.5m).ShouldBe(0m);
    }

    [Fact]
    public async Task The_catalog_can_be_searched_in_uzbek()
    {
        var client = await _api.CreateAuthenticatedClientAsync(Random.Shared.NextInt64(1, long.MaxValue));

        var results = await client.GetFromJsonAsync<List<ExercisePayload>>("/api/exercises?q=shtanga");

        results!.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Listing_programs_returns_my_own_and_flags_ownership()
    {
        var (client, benchId, rowId) = await SetupAsync();
        var mine = await CreateAsync(client, benchId, rowId);

        var programs = await client.GetFromJsonAsync<List<ProgramSummaryPayload>>("/api/programs");

        programs!.ShouldContain(p => p.Id == mine.Id && p.IsOwnedByMe && p.DayCount == 1);
    }

    [Fact]
    public async Task Listing_programs_does_not_leak_another_athletes_private_program()
    {
        var (owner, benchId, rowId) = await SetupAsync();
        var hidden = await CreateAsync(owner, benchId, rowId);

        var stranger = await _api.CreateAuthenticatedClientAsync(Random.Shared.NextInt64(1, long.MaxValue));
        var programs = await stranger.GetFromJsonAsync<List<ProgramSummaryPayload>>("/api/programs");

        programs!.ShouldNotContain(p => p.Id == hidden.Id);
    }

    private async Task<ProgramPayload> CreateAsync(HttpClient client, Guid benchId, Guid rowId)
    {
        var created = await client.PostAsJsonAsync("/api/programs", BuildProgramBody(benchId, rowId));
        created.EnsureSuccessStatusCode();
        return (await created.Content.ReadFromJsonAsync<ProgramPayload>())!;
    }

    internal sealed record ProgramPayload(
        Guid Id, string Name, string Status, string Visibility, int RevisionNumber,
        List<ProgramDayPayload> Days);

    internal sealed record ProgramSummaryPayload(
        Guid Id, string Name, string Status, string Visibility, int DayCount, bool IsOwnedByMe);

    internal sealed record ProgramDayPayload(
        Guid Id, int Order, string Label, List<PrescriptionPayload> Prescriptions);

    internal sealed record PrescriptionPayload(
        Guid Id, int Order, Guid ExerciseId, int WorkingSetCount, int RepMin, int RepMax,
        List<WarmupPayload> Warmup);

    internal sealed record WarmupPayload(decimal PercentOfWorking, int Reps, bool IsOptional);

    internal sealed record DayPreviewPayload(Guid DayId, string Label, List<PreviewExercisePayload> Exercises);

    internal sealed record PreviewExercisePayload(
        Guid ExerciseId, int WorkingSetCount, int TargetRepMin, int TargetRepMax,
        decimal? SuggestedWorkingWeight, List<ResolvedWarmupPayload> Warmup);

    internal sealed record ResolvedWarmupPayload(decimal Weight, int Reps, bool IsOptional);

    internal sealed record ExercisePayload(Guid Id, string Slug, string Name);

    internal sealed record ProblemPayload(string Title, int Status, string ErrorCode);
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/MyGym.IntegrationTests --filter ProgramEndpointTests`
Expected: FAIL — the program endpoints return 404.

- [ ] **Step 4: Write the DTOs**

`src/Modules/Training/MyGym.Modules.Training.Application/Programs/ProgramDtos.cs`:

```csharp
using MyGym.Modules.Training.Domain.Programs;
using MyGym.Modules.Training.Domain.ValueObjects;

namespace MyGym.Modules.Training.Application.Programs;

// ── Inputs ────────────────────────────────────────────────────────────────
public sealed record WarmupStepInput(decimal PercentOfWorking, int Reps, bool IsOptional);

public sealed record PrescriptionInput(
    Guid ExerciseId,
    Guid? AlternativeExerciseId,
    int WorkingSetCount,
    int RepMin,
    int RepMax,
    int RestMinSeconds,
    int RestMaxSeconds,
    IReadOnlyList<WarmupStepInput> Warmup,
    string? Notes);

public sealed record ProgramDayInput(
    string Label, DayOfWeek? DayOfWeek, IReadOnlyList<PrescriptionInput> Prescriptions);

// ── Outputs ───────────────────────────────────────────────────────────────
public sealed record WarmupStepResponse(decimal PercentOfWorking, int Reps, bool IsOptional);

public sealed record PrescriptionResponse(
    Guid Id, int Order, Guid ExerciseId, Guid? AlternativeExerciseId, int WorkingSetCount,
    int RepMin, int RepMax, int RestMinSeconds, int RestMaxSeconds,
    IReadOnlyList<WarmupStepResponse> Warmup, string? Notes)
{
    public static PrescriptionResponse From(ExercisePrescription p) => new(
        p.Id.Value, p.Order, p.ExerciseId.Value, p.AlternativeExerciseId?.Value, p.WorkingSetCount,
        p.TargetReps.Min, p.TargetReps.Max, (int)p.Rest.Min.TotalSeconds, (int)p.Rest.Max.TotalSeconds,
        p.Warmup.Steps.Select(s => new WarmupStepResponse(s.PercentOfWorking, s.Reps, s.IsOptional)).ToList(),
        p.Notes);
}

public sealed record ProgramDayResponse(
    Guid Id, int Order, string Label, DayOfWeek? DayOfWeek, IReadOnlyList<PrescriptionResponse> Prescriptions)
{
    public static ProgramDayResponse From(ProgramDay d) => new(
        d.Id.Value, d.Order, d.Label, d.DayOfWeek,
        d.Prescriptions.Select(PrescriptionResponse.From).ToList());
}

public sealed record ProgramResponse(
    Guid Id, string Name, string Status, string Visibility, int RevisionNumber,
    Guid? ForkedFromProgramId, IReadOnlyList<ProgramDayResponse> Days)
{
    public static ProgramResponse From(TrainingProgram p) => new(
        p.Id.Value, p.Name, p.Status.ToString(), p.Visibility.ToString(), p.RevisionNumber,
        p.ForkedFromProgramId?.Value, p.Days.Select(ProgramDayResponse.From).ToList());
}

public sealed record ProgramSummaryResponse(
    Guid Id, string Name, string Status, string Visibility, int DayCount, bool IsOwnedByMe);

public sealed record ResolvedWarmupResponse(decimal Weight, int Reps, bool IsOptional);

public sealed record PreviewExerciseResponse(
    Guid ExerciseId, Guid? AlternativeExerciseId, int WorkingSetCount, int TargetRepMin,
    int TargetRepMax, int RestMinSeconds, int RestMaxSeconds, decimal? SuggestedWorkingWeight,
    string WeightUnit, IReadOnlyList<ResolvedWarmupResponse> Warmup, string? Notes);

public sealed record DayPreviewResponse(
    Guid ProgramId, Guid DayId, string Label, int RevisionNumber,
    IReadOnlyList<PreviewExerciseResponse> Exercises);
```

- [ ] **Step 5: Write the program handlers**

`src/Modules/Training/MyGym.Modules.Training.Application/Programs/ProgramBuilder.cs`:

```csharp
using MyGym.Modules.ExerciseCatalog.Contracts;
using MyGym.Modules.Training.Domain;
using MyGym.Modules.Training.Domain.Programs;
using MyGym.Modules.Training.Domain.ValueObjects;
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Application.Programs;

/// <summary>
/// Turns request input into a validated aggregate. Shared by create and replace so the two paths
/// cannot drift apart.
/// </summary>
internal static class ProgramBuilder
{
    public static async Task<Result<TrainingProgram>> BuildAsync(
        UserId owner,
        string name,
        IReadOnlyList<ProgramDayInput> days,
        IExerciseCatalogQueries catalog,
        CancellationToken ct)
    {
        var exerciseIds = days
            .SelectMany(d => d.Prescriptions)
            .SelectMany(p => p.AlternativeExerciseId is { } alt
                ? new[] { ExerciseId.From(p.ExerciseId), ExerciseId.From(alt) }
                : [ExerciseId.From(p.ExerciseId)])
            .ToList();

        if (!await catalog.AllExistAsync(exerciseIds, ct))
            return Result<TrainingProgram>.Failure(
                new Error("exercise.not_found", "One or more exercises do not exist."));

        var created = TrainingProgram.Create(owner, name);
        if (created.IsFailure)
            return created;

        var program = created.Value;

        foreach (var dayInput in days)
        {
            var day = program.AddDay(dayInput.Label, dayInput.DayOfWeek);
            if (day.IsFailure)
                return Result<TrainingProgram>.Failure(day.Error);

            foreach (var input in dayInput.Prescriptions)
            {
                var applied = ApplyPrescription(program, day.Value.Id, input);
                if (applied.IsFailure)
                    return Result<TrainingProgram>.Failure(applied.Error);
            }
        }

        return Result<TrainingProgram>.Success(program);
    }

    private static Result ApplyPrescription(
        TrainingProgram program, ProgramDayId dayId, PrescriptionInput input)
    {
        var reps = RepRange.Create(input.RepMin, input.RepMax);
        if (reps.IsFailure)
            return Result.Failure(reps.Error);

        var rest = RestPeriod.Create(
            TimeSpan.FromSeconds(input.RestMinSeconds), TimeSpan.FromSeconds(input.RestMaxSeconds));
        if (rest.IsFailure)
            return Result.Failure(rest.Error);

        var steps = new List<WarmupStep>();
        foreach (var stepInput in input.Warmup)
        {
            var step = WarmupStep.Create(stepInput.PercentOfWorking, stepInput.Reps, stepInput.IsOptional);
            if (step.IsFailure)
                return Result.Failure(step.Error);

            steps.Add(step.Value);
        }

        var ramp = WarmupRamp.Create(steps);
        if (ramp.IsFailure)
            return Result.Failure(ramp.Error);

        var added = program.AddPrescription(
            dayId,
            ExerciseId.From(input.ExerciseId),
            input.AlternativeExerciseId is { } alt ? ExerciseId.From(alt) : null,
            input.WorkingSetCount, reps.Value, rest.Value, ramp.Value, input.Notes);

        return added.IsFailure ? Result.Failure(added.Error) : Result.Success();
    }
}
```

`src/Modules/Training/MyGym.Modules.Training.Application/Programs/ProgramCommands.cs`:

```csharp
using MyGym.Modules.ExerciseCatalog.Contracts;
using MyGym.Modules.Training.Domain;
using MyGym.Modules.Training.Domain.Programs;
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Application.Programs;

public static class ProgramErrors
{
    /// <summary>
    /// Also returned when a program exists but the actor may not see it. Deliberate: a distinct
    /// "forbidden" code would let anyone probe which program ids are real.
    /// </summary>
    public const string NotFound = "program.not_found";
}

public sealed record CreateProgramCommand(UserId Owner, string Name, IReadOnlyList<ProgramDayInput> Days);

public sealed class CreateProgramCommandHandler(
    ITrainingProgramRepository programs,
    IExerciseCatalogQueries catalog,
    ITrainingUnitOfWork unitOfWork) : ICommandHandler<CreateProgramCommand, ProgramResponse>
{
    public async Task<Result<ProgramResponse>> HandleAsync(CreateProgramCommand command, CancellationToken ct)
    {
        var built = await ProgramBuilder.BuildAsync(
            command.Owner, command.Name, command.Days, catalog, ct);

        if (built.IsFailure)
            return Result<ProgramResponse>.Failure(built.Error);

        await programs.AddAsync(built.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result<ProgramResponse>.Success(ProgramResponse.From(built.Value));
    }
}

public sealed record ReplaceProgramCommand(
    UserId Actor, ProgramId ProgramId, string Name, IReadOnlyList<ProgramDayInput> Days);

/// <summary>
/// Replaces a program's structure wholesale. The athlete edits one form in the Mini App, so a
/// single replace is both simpler and safer than a patch language over days and prescriptions —
/// and already-logged sessions are unaffected because they carry their own snapshot.
/// </summary>
public sealed class ReplaceProgramCommandHandler(
    ITrainingProgramRepository programs,
    IExerciseCatalogQueries catalog,
    ITrainingUnitOfWork unitOfWork) : ICommandHandler<ReplaceProgramCommand, ProgramResponse>
{
    public async Task<Result<ProgramResponse>> HandleAsync(ReplaceProgramCommand command, CancellationToken ct)
    {
        var existing = await programs.GetByIdAsync(command.ProgramId, ct);
        if (existing is null || existing.OwnerUserId != command.Actor)
            return Result<ProgramResponse>.Failure(new Error(ProgramErrors.NotFound, "Program not found."));

        if (existing.Status == ProgramStatus.Archived)
            return Result<ProgramResponse>.Failure(
                new Error(TrainingErrors.ProgramNotEditable, "An archived program cannot be edited."));

        var rename = existing.Rename(command.Name);
        if (rename.IsFailure)
            return Result<ProgramResponse>.Failure(rename.Error);

        foreach (var day in existing.Days)
        {
            var removed = existing.RemoveDay(day.Id);
            if (removed.IsFailure)
                return Result<ProgramResponse>.Failure(removed.Error);
        }

        var exerciseIds = command.Days
            .SelectMany(d => d.Prescriptions)
            .SelectMany(p => p.AlternativeExerciseId is { } alt
                ? new[] { ExerciseId.From(p.ExerciseId), ExerciseId.From(alt) }
                : [ExerciseId.From(p.ExerciseId)])
            .ToList();

        if (!await catalog.AllExistAsync(exerciseIds, ct))
            return Result<ProgramResponse>.Failure(
                new Error("exercise.not_found", "One or more exercises do not exist."));

        foreach (var dayInput in command.Days)
        {
            var day = existing.AddDay(dayInput.Label, dayInput.DayOfWeek);
            if (day.IsFailure)
                return Result<ProgramResponse>.Failure(day.Error);

            foreach (var prescription in dayInput.Prescriptions)
            {
                var reps = Domain.ValueObjects.RepRange.Create(prescription.RepMin, prescription.RepMax);
                if (reps.IsFailure)
                    return Result<ProgramResponse>.Failure(reps.Error);

                var rest = Domain.ValueObjects.RestPeriod.Create(
                    TimeSpan.FromSeconds(prescription.RestMinSeconds),
                    TimeSpan.FromSeconds(prescription.RestMaxSeconds));
                if (rest.IsFailure)
                    return Result<ProgramResponse>.Failure(rest.Error);

                var steps = new List<Domain.ValueObjects.WarmupStep>();
                foreach (var step in prescription.Warmup)
                {
                    var created = Domain.ValueObjects.WarmupStep.Create(
                        step.PercentOfWorking, step.Reps, step.IsOptional);
                    if (created.IsFailure)
                        return Result<ProgramResponse>.Failure(created.Error);

                    steps.Add(created.Value);
                }

                var ramp = Domain.ValueObjects.WarmupRamp.Create(steps);
                if (ramp.IsFailure)
                    return Result<ProgramResponse>.Failure(ramp.Error);

                var added = existing.AddPrescription(
                    day.Value.Id,
                    ExerciseId.From(prescription.ExerciseId),
                    prescription.AlternativeExerciseId is { } alt ? ExerciseId.From(alt) : null,
                    prescription.WorkingSetCount, reps.Value, rest.Value, ramp.Value, prescription.Notes);

                if (added.IsFailure)
                    return Result<ProgramResponse>.Failure(added.Error);
            }
        }

        await unitOfWork.SaveChangesAsync(ct);
        return Result<ProgramResponse>.Success(ProgramResponse.From(existing));
    }
}

public sealed record ActivateProgramCommand(UserId Actor, ProgramId ProgramId);

public sealed class ActivateProgramCommandHandler(
    ITrainingProgramRepository programs, ITrainingUnitOfWork unitOfWork)
    : ICommandHandler<ActivateProgramCommand>
{
    public async Task<Result> HandleAsync(ActivateProgramCommand command, CancellationToken ct)
    {
        var program = await programs.GetByIdAsync(command.ProgramId, ct);
        if (program is null || program.OwnerUserId != command.Actor)
            return Result.Failure(new Error(ProgramErrors.NotFound, "Program not found."));

        var activated = program.Activate();
        if (activated.IsFailure)
            return activated;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public sealed record ArchiveProgramCommand(UserId Actor, ProgramId ProgramId);

public sealed class ArchiveProgramCommandHandler(
    ITrainingProgramRepository programs, ITrainingUnitOfWork unitOfWork)
    : ICommandHandler<ArchiveProgramCommand>
{
    public async Task<Result> HandleAsync(ArchiveProgramCommand command, CancellationToken ct)
    {
        var program = await programs.GetByIdAsync(command.ProgramId, ct);
        if (program is null || program.OwnerUserId != command.Actor)
            return Result.Failure(new Error(ProgramErrors.NotFound, "Program not found."));

        var archived = program.Archive();
        if (archived.IsFailure)
            return archived;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public sealed record SetProgramVisibilityCommand(UserId Actor, ProgramId ProgramId, ProgramVisibility Visibility);

public sealed class SetProgramVisibilityCommandHandler(
    ITrainingProgramRepository programs, ITrainingUnitOfWork unitOfWork)
    : ICommandHandler<SetProgramVisibilityCommand>
{
    public async Task<Result> HandleAsync(SetProgramVisibilityCommand command, CancellationToken ct)
    {
        var program = await programs.GetByIdAsync(command.ProgramId, ct);
        if (program is null || program.OwnerUserId != command.Actor)
            return Result.Failure(new Error(ProgramErrors.NotFound, "Program not found."));

        var set = program.SetVisibility(command.Visibility);
        if (set.IsFailure)
            return set;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public sealed record ForkProgramCommand(UserId Actor, ProgramId ProgramId);

public sealed class ForkProgramCommandHandler(
    ITrainingProgramRepository programs, ITrainingUnitOfWork unitOfWork)
    : ICommandHandler<ForkProgramCommand, ProgramResponse>
{
    public async Task<Result<ProgramResponse>> HandleAsync(ForkProgramCommand command, CancellationToken ct)
    {
        var source = await programs.GetByIdAsync(command.ProgramId, ct);

        // A program is forkable if the actor owns it, or it is public.
        var visible = source is not null
            && (source.OwnerUserId == command.Actor || source.Visibility == ProgramVisibility.Public);

        if (!visible)
            return Result<ProgramResponse>.Failure(new Error(ProgramErrors.NotFound, "Program not found."));

        var fork = source!.Fork(command.Actor);
        await programs.AddAsync(fork, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result<ProgramResponse>.Success(ProgramResponse.From(fork));
    }
}
```

`src/Modules/Training/MyGym.Modules.Training.Application/Programs/ProgramQueries.cs`:

```csharp
using MyGym.Modules.Identity.Contracts;
using MyGym.Modules.Training.Domain.Programs;
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Application.Programs;

public sealed record GetProgramQuery(UserId Actor, ProgramId ProgramId);

public sealed class GetProgramQueryHandler(ITrainingProgramRepository programs)
    : IQueryHandler<GetProgramQuery, ProgramResponse>
{
    public async Task<Result<ProgramResponse>> HandleAsync(GetProgramQuery query, CancellationToken ct)
    {
        var program = await programs.GetByIdAsync(query.ProgramId, ct);

        var visible = program is not null
            && (program.OwnerUserId == query.Actor || program.Visibility == ProgramVisibility.Public);

        return visible
            ? Result<ProgramResponse>.Success(ProgramResponse.From(program!))
            : Result<ProgramResponse>.Failure(new Error(ProgramErrors.NotFound, "Program not found."));
    }
}

/// <summary>Handler lives in Infrastructure — it is a read-side projection, not a domain operation.</summary>
public sealed record ListProgramsQuery(UserId Actor);

public sealed record PreviewProgramDayQuery(UserId Actor, ProgramId ProgramId, ProgramDayId DayId);

public sealed class PreviewProgramDayQueryHandler(
    ITrainingProgramRepository programs,
    ILastWorkingWeightQuery lastWeights,
    IAthleteProfileQueries profiles) : IQueryHandler<PreviewProgramDayQuery, DayPreviewResponse>
{
    public async Task<Result<DayPreviewResponse>> HandleAsync(
        PreviewProgramDayQuery query, CancellationToken ct)
    {
        var program = await programs.GetByIdAsync(query.ProgramId, ct);
        var visible = program is not null
            && (program.OwnerUserId == query.Actor || program.Visibility == ProgramVisibility.Public);

        if (!visible)
            return Result<DayPreviewResponse>.Failure(new Error(ProgramErrors.NotFound, "Program not found."));

        var day = program!.Days.SingleOrDefault(d => d.Id == query.DayId);
        if (day is null)
            return Result<DayPreviewResponse>.Failure(
                new Error(Domain.TrainingErrors.ProgramDayNotFound, "Day not found."));

        var preferences = await profiles.GetLoadingPreferencesAsync(query.Actor, ct)
                          ?? new LoadingPreferences(WeightUnit.Kilograms, 2.5m);

        var increment = WeightIncrement.Create(preferences.Increment, preferences.PreferredUnit).Value;

        var exerciseIds = day.Prescriptions.Select(p => p.ExerciseId).ToList();
        var history = await lastWeights.GetTopWorkingWeightsAsync(query.Actor, exerciseIds, ct);

        var exercises = day.Prescriptions.Select(prescription =>
        {
            history.TryGetValue(prescription.ExerciseId, out var lastWorking);

            // No history yet: resolve against zero so the shape is stable and the Mini App can
            // prompt for a starting weight. Auto-progression is sub-project 2's job.
            var working = lastWorking ?? Weight.Create(0m, preferences.PreferredUnit).Value;
            var resolved = prescription.Warmup.Resolve(working, increment);

            return new PreviewExerciseResponse(
                prescription.ExerciseId.Value,
                prescription.AlternativeExerciseId?.Value,
                prescription.WorkingSetCount,
                prescription.TargetReps.Min,
                prescription.TargetReps.Max,
                (int)prescription.Rest.Min.TotalSeconds,
                (int)prescription.Rest.Max.TotalSeconds,
                lastWorking?.Value,
                preferences.PreferredUnit.ToString(),
                resolved.Select(r => new ResolvedWarmupResponse(r.Weight.Value, r.Reps, r.IsOptional)).ToList(),
                prescription.Notes);
        }).ToList();

        return Result<DayPreviewResponse>.Success(new DayPreviewResponse(
            program.Id.Value, day.Id.Value, day.Label, program.RevisionNumber, exercises));
    }
}
```

`src/Modules/Training/MyGym.Modules.Training.Application/ILastWorkingWeightQuery.cs`:

```csharp
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Application;

public interface ILastWorkingWeightQuery
{
    /// <summary>
    /// The heaviest working set logged for each exercise in the athlete's most recent completed
    /// session that contained it. Exercises with no history are absent from the result.
    /// </summary>
    Task<IReadOnlyDictionary<ExerciseId, Weight>> GetTopWorkingWeightsAsync(
        UserId userId, IReadOnlyCollection<ExerciseId> exerciseIds, CancellationToken ct);
}
```

`src/Modules/Training/MyGym.Modules.Training.Infrastructure/LastWorkingWeightQuery.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using MyGym.Modules.Training.Application;
using MyGym.Modules.Training.Domain.Sessions;
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Infrastructure;

internal sealed class LastWorkingWeightQuery(TrainingDbContext context) : ILastWorkingWeightQuery
{
    public async Task<IReadOnlyDictionary<ExerciseId, Weight>> GetTopWorkingWeightsAsync(
        UserId userId, IReadOnlyCollection<ExerciseId> exerciseIds, CancellationToken ct)
    {
        if (exerciseIds.Count == 0)
            return new Dictionary<ExerciseId, Weight>();

        var ids = exerciseIds.Distinct().ToList();

        // Read-side query: projects straight out of the owned tables, no aggregate materialised.
        var rows = await context.Sessions
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.Status == SessionStatus.Completed)
            .OrderByDescending(s => s.CompletedAt)
            .Take(40)
            .SelectMany(s => s.Exercises.Select(e => new
            {
                s.CompletedAt,
                ExerciseId = e.Prescription.ExerciseId,
                TopWorkingValue = e.Sets
                    .Where(set => set.Kind == SetKind.Working)
                    .Max(set => (decimal?)set.Weight.Value),
                Unit = e.Sets
                    .Where(set => set.Kind == SetKind.Working)
                    .Select(set => set.Weight.Unit)
                    .FirstOrDefault()
            }))
            .Where(row => ids.Contains(row.ExerciseId) && row.TopWorkingValue != null)
            .ToListAsync(ct);

        return rows
            .GroupBy(row => row.ExerciseId)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var latest = group.OrderByDescending(row => row.CompletedAt).First();
                    return Weight.Create(latest.TopWorkingValue!.Value, latest.Unit).Value;
                });
    }
}
```

`src/Modules/Training/MyGym.Modules.Training.Infrastructure/ListProgramsQueryHandler.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using MyGym.Modules.Training.Application.Programs;
using MyGym.Modules.Training.Domain.Programs;
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Infrastructure;

/// <summary>
/// Returns the athlete's own programs plus every public template, newest first. A read-side
/// projection: no aggregate is materialised, and the days are counted in SQL.
/// </summary>
internal sealed class ListProgramsQueryHandler(TrainingDbContext context)
    : IQueryHandler<ListProgramsQuery, IReadOnlyList<ProgramSummaryResponse>>
{
    public async Task<Result<IReadOnlyList<ProgramSummaryResponse>>> HandleAsync(
        ListProgramsQuery query, CancellationToken ct)
    {
        var rows = await context.Programs
            .AsNoTracking()
            .Where(p => p.OwnerUserId == query.Actor
                        || (p.Visibility == ProgramVisibility.Public && p.Status == ProgramStatus.Active))
            .OrderByDescending(p => p.OwnerUserId == query.Actor)
            .ThenBy(p => p.Name)
            .Select(p => new ProgramSummaryResponse(
                p.Id.Value,
                p.Name,
                p.Status.ToString(),
                p.Visibility.ToString(),
                p.Days.Count,
                p.OwnerUserId == query.Actor))
            .ToListAsync(ct);

        return Result<IReadOnlyList<ProgramSummaryResponse>>.Success(rows);
    }
}
```

> Archived programs are hidden from other athletes but remain visible to their owner, which is why
> the `Status` filter applies only to the public branch of the predicate.

Register both read-side handlers in `TrainingInfrastructureExtensions.AddTrainingInfrastructure`:

```csharp
        services.AddScoped<ILastWorkingWeightQuery, LastWorkingWeightQuery>();
        services.AddScoped<IQueryHandler<ListProgramsQuery, IReadOnlyList<ProgramSummaryResponse>>,
            ListProgramsQueryHandler>();
```

> If EF Core cannot translate the `SelectMany` over owned collections, replace the projection with
> two round trips: select the candidate session ids first, then query `logged_sets` joined to
> `session_exercises` filtered by those ids. Correctness over cleverness — the preview runs once per
> session start, not per set.

- [ ] **Step 6: Write the endpoints**

`src/Host/MyGym.Api/Endpoints/ProgramEndpoints.cs`:

```csharp
using MyGym.Api.Auth;
using MyGym.Api.Errors;
using MyGym.Modules.Training.Application.Programs;
using MyGym.Modules.Training.Domain.Programs;
using MyGym.SharedKernel;

namespace MyGym.Api.Endpoints;

public static class ProgramEndpoints
{
    public sealed record SaveProgramRequest(string Name, IReadOnlyList<ProgramDayInput> Days);
    public sealed record SetVisibilityRequest(ProgramVisibility Visibility);

    public static void MapProgramEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/programs").RequireAuthorization();

        group.MapPost("", async (
            SaveProgramRequest request,
            ICurrentUser me,
            ICommandHandler<CreateProgramCommand, ProgramResponse> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(
                new CreateProgramCommand(me.UserId, request.Name, request.Days), ct);

            return result.ToHttpResult(program => Results.Created($"/api/programs/{program.Id}", program));
        });

        group.MapGet("", async (
            ICurrentUser me,
            IQueryHandler<ListProgramsQuery, IReadOnlyList<ProgramSummaryResponse>> handler,
            CancellationToken ct) =>
            (await handler.HandleAsync(new ListProgramsQuery(me.UserId), ct)).ToHttpResult());

        group.MapGet("/{id:guid}", async (
            Guid id,
            ICurrentUser me,
            IQueryHandler<GetProgramQuery, ProgramResponse> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetProgramQuery(me.UserId, ProgramId.From(id)), ct);
            return result.ToHttpResult();
        });

        group.MapPut("/{id:guid}", async (
            Guid id,
            SaveProgramRequest request,
            ICurrentUser me,
            ICommandHandler<ReplaceProgramCommand, ProgramResponse> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(
                new ReplaceProgramCommand(me.UserId, ProgramId.From(id), request.Name, request.Days), ct);

            return result.ToHttpResult();
        });

        group.MapPost("/{id:guid}/activate", async (
            Guid id, ICurrentUser me, ICommandHandler<ActivateProgramCommand> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ActivateProgramCommand(me.UserId, ProgramId.From(id)), ct))
                .ToHttpResult());

        group.MapDelete("/{id:guid}", async (
            Guid id, ICurrentUser me, ICommandHandler<ArchiveProgramCommand> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new ArchiveProgramCommand(me.UserId, ProgramId.From(id)), ct))
                .ToHttpResult());

        group.MapPatch("/{id:guid}/visibility", async (
            Guid id,
            SetVisibilityRequest request,
            ICurrentUser me,
            ICommandHandler<SetProgramVisibilityCommand> handler,
            CancellationToken ct) =>
            (await handler.HandleAsync(
                new SetProgramVisibilityCommand(me.UserId, ProgramId.From(id), request.Visibility), ct))
                .ToHttpResult());

        group.MapPost("/{id:guid}/fork", async (
            Guid id,
            ICurrentUser me,
            ICommandHandler<ForkProgramCommand, ProgramResponse> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new ForkProgramCommand(me.UserId, ProgramId.From(id)), ct);
            return result.ToHttpResult(fork => Results.Created($"/api/programs/{fork.Id}", fork));
        });

        group.MapGet("/{id:guid}/days/{dayId:guid}/preview", async (
            Guid id,
            Guid dayId,
            ICurrentUser me,
            IQueryHandler<PreviewProgramDayQuery, DayPreviewResponse> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(
                new PreviewProgramDayQuery(me.UserId, ProgramId.From(id), ProgramDayId.From(dayId)), ct);

            return result.ToHttpResult();
        });
    }
}
```

`src/Host/MyGym.Api/Endpoints/ExerciseEndpoints.cs`:

```csharp
using MyGym.Modules.ExerciseCatalog.Contracts;

namespace MyGym.Api.Endpoints;

public static class ExerciseEndpoints
{
    public static void MapExerciseEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/exercises", async (
                string? q,
                string? lang,
                IExerciseCatalogQueries catalog,
                CancellationToken ct) =>
            Results.Ok(await catalog.SearchAsync(q, lang ?? "en", ct)))
            .RequireAuthorization();
    }
}
```

Register the handlers and endpoints in `Program.cs`:

```csharp
builder.Services.AddScoped<ICommandHandler<CreateProgramCommand, ProgramResponse>, CreateProgramCommandHandler>();
builder.Services.AddScoped<ICommandHandler<ReplaceProgramCommand, ProgramResponse>, ReplaceProgramCommandHandler>();
builder.Services.AddScoped<ICommandHandler<ActivateProgramCommand>, ActivateProgramCommandHandler>();
builder.Services.AddScoped<ICommandHandler<ArchiveProgramCommand>, ArchiveProgramCommandHandler>();
builder.Services.AddScoped<ICommandHandler<SetProgramVisibilityCommand>, SetProgramVisibilityCommandHandler>();
builder.Services.AddScoped<ICommandHandler<ForkProgramCommand, ProgramResponse>, ForkProgramCommandHandler>();
builder.Services.AddScoped<IQueryHandler<GetProgramQuery, ProgramResponse>, GetProgramQueryHandler>();
builder.Services.AddScoped<IQueryHandler<PreviewProgramDayQuery, DayPreviewResponse>, PreviewProgramDayQueryHandler>();

// ...after MapProfileEndpoints():
app.MapProgramEndpoints();
app.MapExerciseEndpoints();
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/MyGym.IntegrationTests --filter ProgramEndpointTests`
Expected: PASS — 11 tests.

- [ ] **Step 8: Commit**

```bash
git add src tests
git commit -m "feat: add program authoring endpoints and resolved day preview"
```

---

### Task 13: Session endpoints and the critical path

**Files:**
- Create: `src/Modules/Training/MyGym.Modules.Training.Application/Sessions/{SessionDtos,StartSession,LogSet,EditSet,DeleteSet,CompleteSession,AbandonSession,GetSession,GetCurrentSession,ListSessions}.cs`
- Create: `src/Host/MyGym.Api/Endpoints/SessionEndpoints.cs`
- Test: `tests/MyGym.IntegrationTests/SessionEndpointTests.cs`

**Interfaces:**
- Consumes: Tasks 1–12.
- Produces:
  - `SessionResponse(Guid Id, string Status, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt, Guid? ProgramId, int? ProgramRevision, Guid? ProgramDayId, decimal? BodyweightAtSession, int WorkingSetCount, IReadOnlyList<SessionExerciseResponse> Exercises)`
  - `SessionExerciseResponse(Guid Id, int Order, Guid ExerciseId, Guid? AlternativeExerciseId, int WorkingSetCount, int TargetRepMin, int TargetRepMax, int RestMinSeconds, int RestMaxSeconds, IReadOnlyList<ResolvedWarmupResponse> SuggestedWarmup, decimal? SuggestedWorkingWeight, IReadOnlyList<LoggedSetResponse> Sets)`
  - `LoggedSetResponse(Guid Id, int Order, string Kind, decimal Weight, string Unit, int Reps, int? Rir, Guid ClientSetId, DateTimeOffset LoggedAt)`
  - `SessionSummaryResponse(Guid Id, string Status, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt, int WorkingSetCount)`
  - Commands: `StartSessionCommand(UserId, ProgramId?, ProgramDayId?, decimal? Bodyweight)`, `LogSetCommand(UserId, SessionId, SessionExerciseId, Guid ClientSetId, SetKind, decimal Weight, int Reps, int? Rir)`, `EditSetCommand`, `DeleteSetCommand`, `CompleteSessionCommand`, `AbandonSessionCommand`, `AddAdHocExerciseCommand(UserId, SessionId, ExerciseId, int WorkingSetCount, int RepMin, int RepMax, int RestMinSeconds, int RestMaxSeconds)` — each with its handler
  - Queries: `GetSessionQuery`, `GetCurrentSessionQuery`, `ListSessionsQuery(UserId, DateTimeOffset? From, DateTimeOffset? To, int Page, int PageSize)`

- [ ] **Step 1: Write the failing end-to-end test**

`tests/MyGym.IntegrationTests/SessionEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace MyGym.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public class SessionEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFixture _api = null!;

    public async ValueTask InitializeAsync()
    {
        _api = new ApiFixture(postgres);
        await using var catalog = postgres.CreateCatalogContext();
        await MyGym.Modules.ExerciseCatalog.Infrastructure.CatalogSeeder.SeedAsync(
            catalog, CancellationToken.None);
    }

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    /// <summary>
    /// The whole point of sub-project 1: author a program, start its day, log a real set, finish,
    /// and come back next week to a suggested working weight drawn from what was actually lifted.
    /// </summary>
    [Fact]
    public async Task The_critical_path_runs_end_to_end()
    {
        var (client, programId, dayId) = await CreateActiveProgramAsync();

        // 1. Start the session.
        var started = await client.PostAsJsonAsync(
            "/api/sessions", new { programId, programDayId = dayId, bodyweight = 74.5m });
        started.StatusCode.ShouldBe(HttpStatusCode.Created);

        var session = (await started.Content.ReadFromJsonAsync<SessionPayload>())!;
        session.Status.ShouldBe("InProgress");
        session.ProgramId.ShouldBe(programId);
        session.ProgramRevision.ShouldNotBeNull();
        session.Exercises.Count.ShouldBe(1);

        var exercise = session.Exercises[0];
        exercise.TargetRepMin.ShouldBe(6);
        exercise.TargetRepMax.ShouldBe(8);
        exercise.WorkingSetCount.ShouldBe(3);
        exercise.SuggestedWarmup.Count.ShouldBe(3);

        // 2. Log warm-ups and working sets.
        foreach (var (weight, reps, kind) in new[]
                 {
                     (20m, 10, "Warmup"), (42.5m, 6, "Warmup"), (62.5m, 3, "Warmup"),
                     (80m, 8, "Working"), (80m, 7, "Working"), (80m, 6, "Working")
                 })
        {
            var logged = await client.PostAsJsonAsync(
                $"/api/sessions/{session.Id}/exercises/{exercise.Id}/sets",
                new { clientSetId = Guid.NewGuid(), kind, weight, reps, rir = 2 });

            logged.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        // 3. Finish.
        var completed = await client.PostAsync($"/api/sessions/{session.Id}/complete", null);
        completed.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var stored = await client.GetFromJsonAsync<SessionPayload>($"/api/sessions/{session.Id}");
        stored!.Status.ShouldBe("Completed");
        stored.WorkingSetCount.ShouldBe(3);
        stored.BodyweightAtSession.ShouldBe(74.5m);
        stored.Exercises[0].Sets.Count.ShouldBe(6);

        // 4. The next preview now suggests the weight actually lifted, with real warm-ups.
        var preview = await client.GetFromJsonAsync<DayPreviewPayload>(
            $"/api/programs/{programId}/days/{dayId}/preview");

        preview!.Exercises[0].SuggestedWorkingWeight.ShouldBe(80m);
        preview.Exercises[0].Warmup.Select(w => w.Weight).ShouldBe([20m, 40m, 62.5m]);
    }

    [Fact]
    public async Task Logging_the_same_client_set_id_twice_creates_one_set()
    {
        var (client, programId, dayId) = await CreateActiveProgramAsync();
        var session = await StartAsync(client, programId, dayId);
        var exerciseId = session.Exercises[0].Id;
        var clientSetId = Guid.NewGuid();

        var body = new { clientSetId, kind = "Working", weight = 80m, reps = 8, rir = (int?)2 };

        var first = await client.PostAsJsonAsync(
            $"/api/sessions/{session.Id}/exercises/{exerciseId}/sets", body);
        var retry = await client.PostAsJsonAsync(
            $"/api/sessions/{session.Id}/exercises/{exerciseId}/sets", body);

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        retry.StatusCode.ShouldBe(HttpStatusCode.Created);

        var firstSet = await first.Content.ReadFromJsonAsync<LoggedSetPayload>();
        var retrySet = await retry.Content.ReadFromJsonAsync<LoggedSetPayload>();
        retrySet!.Id.ShouldBe(firstSet!.Id);

        var stored = await client.GetFromJsonAsync<SessionPayload>($"/api/sessions/{session.Id}");
        stored!.Exercises[0].Sets.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Starting_a_second_session_while_one_is_open_conflicts()
    {
        var (client, programId, dayId) = await CreateActiveProgramAsync();
        await StartAsync(client, programId, dayId);

        var second = await client.PostAsJsonAsync(
            "/api/sessions", new { programId, programDayId = dayId, bodyweight = (decimal?)null });

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await second.Content.ReadFromJsonAsync<ProblemPayload>();
        problem!.ErrorCode.ShouldBe("session.already_in_progress");
    }

    [Fact]
    public async Task The_current_session_is_retrievable_and_absent_once_finished()
    {
        var (client, programId, dayId) = await CreateActiveProgramAsync();
        var session = await StartAsync(client, programId, dayId);

        (await client.GetFromJsonAsync<SessionPayload>("/api/sessions/current"))!.Id.ShouldBe(session.Id);

        await client.PostAsJsonAsync(
            $"/api/sessions/{session.Id}/exercises/{session.Exercises[0].Id}/sets",
            new { clientSetId = Guid.NewGuid(), kind = "Working", weight = 80m, reps = 8, rir = (int?)null });
        await client.PostAsync($"/api/sessions/{session.Id}/complete", null);

        (await client.GetAsync("/api/sessions/current")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_session_cannot_be_completed_without_a_working_set()
    {
        var (client, programId, dayId) = await CreateActiveProgramAsync();
        var session = await StartAsync(client, programId, dayId);

        await client.PostAsJsonAsync(
            $"/api/sessions/{session.Id}/exercises/{session.Exercises[0].Id}/sets",
            new { clientSetId = Guid.NewGuid(), kind = "Warmup", weight = 20m, reps = 10, rir = (int?)null });

        var completed = await client.PostAsync($"/api/sessions/{session.Id}/complete", null);

        completed.StatusCode.ShouldBe(HttpStatusCode.UnprocessableContent);
        var problem = await completed.Content.ReadFromJsonAsync<ProblemPayload>();
        problem!.ErrorCode.ShouldBe("session.needs_at_least_one_working_set");
    }

    [Fact]
    public async Task A_closed_session_rejects_further_sets()
    {
        var (client, programId, dayId) = await CreateActiveProgramAsync();
        var session = await StartAsync(client, programId, dayId);
        var exerciseId = session.Exercises[0].Id;

        await client.PostAsJsonAsync(
            $"/api/sessions/{session.Id}/exercises/{exerciseId}/sets",
            new { clientSetId = Guid.NewGuid(), kind = "Working", weight = 80m, reps = 8, rir = (int?)null });
        await client.PostAsync($"/api/sessions/{session.Id}/complete", null);

        var late = await client.PostAsJsonAsync(
            $"/api/sessions/{session.Id}/exercises/{exerciseId}/sets",
            new { clientSetId = Guid.NewGuid(), kind = "Working", weight = 85m, reps = 5, rir = (int?)null });

        late.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await late.Content.ReadFromJsonAsync<ProblemPayload>())!.ErrorCode.ShouldBe("session.closed");
    }

    [Fact]
    public async Task Another_athlete_cannot_touch_someone_elses_session()
    {
        var (owner, programId, dayId) = await CreateActiveProgramAsync();
        var session = await StartAsync(owner, programId, dayId);

        var stranger = await _api.CreateAuthenticatedClientAsync(Random.Shared.NextInt64(1, long.MaxValue));

        (await stranger.GetAsync($"/api/sessions/{session.Id}")).StatusCode
            .ShouldBe(HttpStatusCode.NotFound);

        var write = await stranger.PostAsJsonAsync(
            $"/api/sessions/{session.Id}/exercises/{session.Exercises[0].Id}/sets",
            new { clientSetId = Guid.NewGuid(), kind = "Working", weight = 80m, reps = 8, rir = (int?)null });

        write.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_abandoned_session_frees_the_athlete_to_start_another()
    {
        var (client, programId, dayId) = await CreateActiveProgramAsync();
        var session = await StartAsync(client, programId, dayId);

        (await client.PostAsync($"/api/sessions/{session.Id}/abandon", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var second = await client.PostAsJsonAsync(
            "/api/sessions", new { programId, programDayId = dayId, bodyweight = (decimal?)null });

        second.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Session_history_lists_completed_sessions_newest_first()
    {
        var (client, programId, dayId) = await CreateActiveProgramAsync();

        for (var i = 0; i < 2; i++)
        {
            var session = await StartAsync(client, programId, dayId);
            await client.PostAsJsonAsync(
                $"/api/sessions/{session.Id}/exercises/{session.Exercises[0].Id}/sets",
                new { clientSetId = Guid.NewGuid(), kind = "Working", weight = 80m + i, reps = 8, rir = (int?)null });
            await client.PostAsync($"/api/sessions/{session.Id}/complete", null);
        }

        var history = await client.GetFromJsonAsync<List<SessionSummaryPayload>>("/api/sessions");

        history!.Count.ShouldBe(2);
        history[0].CompletedAt.ShouldBeGreaterThanOrEqualTo(history[1].CompletedAt!.Value);
    }

    [Fact]
    public async Task An_ad_hoc_session_accepts_exercises_added_mid_workout()
    {
        var client = await _api.CreateAuthenticatedClientAsync(Random.Shared.NextInt64(1, long.MaxValue));

        Guid curlId;
        await using (var catalog = postgres.CreateCatalogContext())
            curlId = (await catalog.Exercises.SingleAsync(e => e.Slug == "ez-bar-curl")).Id.Value;

        var started = await client.PostAsJsonAsync("/api/sessions",
            new { programId = (Guid?)null, programDayId = (Guid?)null, bodyweight = (decimal?)null });

        started.StatusCode.ShouldBe(HttpStatusCode.Created);
        var session = (await started.Content.ReadFromJsonAsync<SessionPayload>())!;
        session.ProgramId.ShouldBeNull();
        session.Exercises.ShouldBeEmpty();

        var added = await client.PostAsJsonAsync($"/api/sessions/{session.Id}/exercises", new
        {
            exerciseId = curlId,
            workingSetCount = 2,
            repMin = 10,
            repMax = 12,
            restMinSeconds = 60,
            restMaxSeconds = 120
        });

        added.StatusCode.ShouldBe(HttpStatusCode.Created);
        var exercise = (await added.Content.ReadFromJsonAsync<AddedExercisePayload>())!;

        var logged = await client.PostAsJsonAsync(
            $"/api/sessions/{session.Id}/exercises/{exercise.SessionExerciseId}/sets",
            new { clientSetId = Guid.NewGuid(), kind = "Working", weight = 25m, reps = 11, rir = (int?)1 });

        logged.StatusCode.ShouldBe(HttpStatusCode.Created);

        (await client.PostAsync($"/api/sessions/{session.Id}/complete", null))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private async Task<SessionPayload> StartAsync(HttpClient client, Guid programId, Guid dayId)
    {
        var started = await client.PostAsJsonAsync(
            "/api/sessions", new { programId, programDayId = dayId, bodyweight = (decimal?)null });
        started.EnsureSuccessStatusCode();
        return (await started.Content.ReadFromJsonAsync<SessionPayload>())!;
    }

    private async Task<(HttpClient Client, Guid ProgramId, Guid DayId)> CreateActiveProgramAsync()
    {
        var client = await _api.CreateAuthenticatedClientAsync(Random.Shared.NextInt64(1, long.MaxValue));

        await using var catalog = postgres.CreateCatalogContext();
        var bench = await catalog.Exercises.SingleAsync(e => e.Slug == "flat-barbell-bench-press");

        var created = await client.PostAsJsonAsync("/api/programs", new
        {
            name = "Critical path program",
            days = new[]
            {
                new
                {
                    label = "Juma — Full Body",
                    dayOfWeek = (string?)null,
                    prescriptions = new[]
                    {
                        new
                        {
                            exerciseId = bench.Id.Value,
                            alternativeExerciseId = (Guid?)null,
                            workingSetCount = 3,
                            repMin = 6,
                            repMax = 8,
                            restMinSeconds = 150,
                            restMaxSeconds = 180,
                            warmup = new[]
                            {
                                new { percentOfWorking = 0.27m, reps = 10, isOptional = false },
                                new { percentOfWorking = 0.53m, reps = 6, isOptional = false },
                                new { percentOfWorking = 0.80m, reps = 3, isOptional = false }
                            },
                            notes = (string?)null
                        }
                    }
                }
            }
        });

        created.EnsureSuccessStatusCode();
        var program = (await created.Content.ReadFromJsonAsync<ProgramPayload>())!;
        await client.PostAsync($"/api/programs/{program.Id}/activate", null);

        return (client, program.Id, program.Days[0].Id);
    }

    internal sealed record ProgramPayload(Guid Id, List<DayPayload> Days);
    internal sealed record DayPayload(Guid Id);

    internal sealed record SessionPayload(
        Guid Id, string Status, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt,
        Guid? ProgramId, int? ProgramRevision, Guid? ProgramDayId, decimal? BodyweightAtSession,
        int WorkingSetCount, List<SessionExercisePayload> Exercises);

    internal sealed record SessionExercisePayload(
        Guid Id, int Order, Guid ExerciseId, int WorkingSetCount, int TargetRepMin, int TargetRepMax,
        List<ResolvedWarmupPayload> SuggestedWarmup, decimal? SuggestedWorkingWeight,
        List<LoggedSetPayload> Sets);

    internal sealed record LoggedSetPayload(
        Guid Id, int Order, string Kind, decimal Weight, int Reps, int? Rir, Guid ClientSetId);

    internal sealed record AddedExercisePayload(Guid SessionExerciseId);

    internal sealed record ResolvedWarmupPayload(decimal Weight, int Reps, bool IsOptional);

    internal sealed record DayPreviewPayload(List<PreviewExercisePayload> Exercises);

    internal sealed record PreviewExercisePayload(
        decimal? SuggestedWorkingWeight, List<ResolvedWarmupPayload> Warmup);

    internal sealed record SessionSummaryPayload(
        Guid Id, string Status, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt, int WorkingSetCount);

    internal sealed record ProblemPayload(string Title, int Status, string ErrorCode);
}
```

> The warm-up expectation `[20m, 40m, 62.5m]` is the arithmetic from Task 3 applied to an 80 kg
> working weight on a 2.5 kg increment: 21.6→20.0, 42.4→40.0, 64.0→62.5. If this assertion fails,
> `WarmupRamp.Resolve` or the athlete's increment is wrong, not the test.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/MyGym.IntegrationTests --filter SessionEndpointTests`
Expected: FAIL — session endpoints return 404.

- [ ] **Step 3: Write the session DTOs**

`src/Modules/Training/MyGym.Modules.Training.Application/Sessions/SessionDtos.cs`:

```csharp
using MyGym.Modules.Training.Application.Programs;
using MyGym.Modules.Training.Domain.Sessions;
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Application.Sessions;

public sealed record LoggedSetResponse(
    Guid Id, int Order, string Kind, decimal Weight, string Unit, int Reps, int? Rir,
    Guid ClientSetId, DateTimeOffset LoggedAt)
{
    public static LoggedSetResponse From(LoggedSet s) => new(
        s.Id.Value, s.Order, s.Kind.ToString(), s.Weight.Value, s.Weight.Unit.ToString(),
        s.Reps, s.Rir, s.ClientSetId, s.LoggedAt);
}

public sealed record SessionExerciseResponse(
    Guid Id, int Order, Guid ExerciseId, Guid? AlternativeExerciseId, int WorkingSetCount,
    int TargetRepMin, int TargetRepMax, int RestMinSeconds, int RestMaxSeconds,
    IReadOnlyList<ResolvedWarmupResponse> SuggestedWarmup, decimal? SuggestedWorkingWeight,
    IReadOnlyList<LoggedSetResponse> Sets);

public sealed record SessionResponse(
    Guid Id, string Status, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt,
    Guid? ProgramId, int? ProgramRevision, Guid? ProgramDayId, decimal? BodyweightAtSession,
    int WorkingSetCount, IReadOnlyList<SessionExerciseResponse> Exercises);

public sealed record SessionSummaryResponse(
    Guid Id, string Status, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt, int WorkingSetCount);

internal static class SessionMapper
{
    /// <summary>
    /// Maps a session, enriching each exercise with resolved warm-ups and the suggested working
    /// weight so the Mini App needs exactly one request to render the whole workout screen.
    /// </summary>
    public static SessionResponse ToResponse(
        WorkoutSession session,
        IReadOnlyDictionary<ExerciseId, Weight> suggestedWeights,
        WeightIncrement increment,
        WeightUnit unit)
    {
        var exercises = session.Exercises.Select(exercise =>
        {
            suggestedWeights.TryGetValue(exercise.Prescription.ExerciseId, out var suggested);
            var working = suggested ?? Weight.Create(0m, unit).Value;
            var resolved = exercise.Prescription.Warmup.Resolve(working, increment);

            return new SessionExerciseResponse(
                exercise.Id.Value,
                exercise.Order,
                exercise.Prescription.ExerciseId.Value,
                exercise.Prescription.AlternativeExerciseId?.Value,
                exercise.Prescription.WorkingSetCount,
                exercise.Prescription.TargetReps.Min,
                exercise.Prescription.TargetReps.Max,
                (int)exercise.Prescription.Rest.Min.TotalSeconds,
                (int)exercise.Prescription.Rest.Max.TotalSeconds,
                resolved.Select(r => new ResolvedWarmupResponse(r.Weight.Value, r.Reps, r.IsOptional)).ToList(),
                suggested?.Value,
                exercise.Sets.Select(LoggedSetResponse.From).ToList());
        }).ToList();

        return new SessionResponse(
            session.Id.Value,
            session.Status.ToString(),
            session.StartedAt,
            session.CompletedAt,
            session.Origin.ProgramId?.Value,
            session.Origin.ProgramRevision,
            session.Origin.ProgramDayId?.Value,
            session.BodyweightAtSession?.Value,
            session.WorkingSetCount,
            exercises);
    }
}
```

- [ ] **Step 4: Write the StartSession handler**

`src/Modules/Training/MyGym.Modules.Training.Application/Sessions/StartSession.cs`:

```csharp
using MyGym.Modules.Identity.Contracts;
using MyGym.Modules.Training.Application.Programs;
using MyGym.Modules.Training.Domain;
using MyGym.Modules.Training.Domain.Programs;
using MyGym.Modules.Training.Domain.Sessions;
using MyGym.Modules.Training.Domain.ValueObjects;
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Application.Sessions;

public sealed record StartSessionCommand(
    UserId Actor, ProgramId? ProgramId, ProgramDayId? ProgramDayId, decimal? Bodyweight);

public sealed class StartSessionCommandHandler(
    IWorkoutSessionRepository sessions,
    ITrainingProgramRepository programs,
    ILastWorkingWeightQuery lastWeights,
    IAthleteProfileQueries profiles,
    ITrainingUnitOfWork unitOfWork,
    TimeProvider timeProvider) : ICommandHandler<StartSessionCommand, SessionResponse>
{
    public async Task<Result<SessionResponse>> HandleAsync(StartSessionCommand command, CancellationToken ct)
    {
        // Application-level guard for the cross-aggregate invariant. The partial unique index in the
        // database is the real guarantee under concurrency; this gives a clean 409 instead of a 500.
        var open = await sessions.GetInProgressForUserAsync(command.Actor, ct);
        if (open is not null)
            return Result<SessionResponse>.Failure(new Error(
                TrainingErrors.SessionAlreadyInProgress, "Finish or abandon the open session first."));

        var preferences = await profiles.GetLoadingPreferencesAsync(command.Actor, ct)
                          ?? new LoadingPreferences(WeightUnit.Kilograms, 2.5m);

        var increment = WeightIncrement.Create(preferences.Increment, preferences.PreferredUnit).Value;

        var origin = SessionOrigin.AdHoc;
        var snapshots = new List<PrescriptionSnapshot>();

        if (command.ProgramId is { } programId)
        {
            if (command.ProgramDayId is not { } dayId)
                return Result<SessionResponse>.Failure(new Error(
                    TrainingErrors.ProgramDayNotFound, "A program day is required when starting from a program."));

            var program = await programs.GetByIdAsync(programId, ct);
            var visible = program is not null
                && (program.OwnerUserId == command.Actor || program.Visibility == ProgramVisibility.Public);

            if (!visible)
                return Result<SessionResponse>.Failure(
                    new Error(ProgramErrors.NotFound, "Program not found."));

            var day = program!.Days.SingleOrDefault(d => d.Id == dayId);
            if (day is null)
                return Result<SessionResponse>.Failure(
                    new Error(TrainingErrors.ProgramDayNotFound, "Day not found."));

            foreach (var prescription in day.Prescriptions)
            {
                var snapshot = PrescriptionSnapshot.Create(
                    prescription.ExerciseId, prescription.AlternativeExerciseId,
                    prescription.WorkingSetCount, prescription.TargetReps, prescription.Rest,
                    prescription.Warmup, prescription.Notes);

                if (snapshot.IsFailure)
                    return Result<SessionResponse>.Failure(snapshot.Error);

                snapshots.Add(snapshot.Value);
            }

            origin = SessionOrigin.FromProgram(programId, program.RevisionNumber, dayId);
        }

        var started = WorkoutSession.Start(command.Actor, origin, snapshots, timeProvider.GetUtcNow());
        if (started.IsFailure)
            return Result<SessionResponse>.Failure(started.Error);

        var session = started.Value;

        if (command.Bodyweight is { } bodyweight)
        {
            var weight = Weight.Create(bodyweight, preferences.PreferredUnit);
            if (weight.IsFailure)
                return Result<SessionResponse>.Failure(weight.Error);

            session.RecordBodyweight(weight.Value);
        }

        await sessions.AddAsync(session, ct);
        await unitOfWork.SaveChangesAsync(ct);

        var suggested = await lastWeights.GetTopWorkingWeightsAsync(
            command.Actor, snapshots.Select(s => s.ExerciseId).ToList(), ct);

        return Result<SessionResponse>.Success(
            SessionMapper.ToResponse(session, suggested, increment, preferences.PreferredUnit));
    }
}
```

- [ ] **Step 5: Write the remaining session handlers**

`src/Modules/Training/MyGym.Modules.Training.Application/Sessions/SessionCommands.cs`:

```csharp
using MyGym.Modules.Identity.Contracts;
using MyGym.Modules.Training.Domain;
using MyGym.Modules.Training.Domain.Sessions;
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Application.Sessions;

public static class SessionErrors
{
    public const string NotFound = "session.not_found";
}

public sealed record LogSetCommand(
    UserId Actor, SessionId SessionId, SessionExerciseId SessionExerciseId, Guid ClientSetId,
    SetKind Kind, decimal Weight, int Reps, int? Rir);

public sealed class LogSetCommandHandler(
    IWorkoutSessionRepository sessions,
    IAthleteProfileQueries profiles,
    ITrainingUnitOfWork unitOfWork,
    TimeProvider timeProvider) : ICommandHandler<LogSetCommand, LoggedSetResponse>
{
    public async Task<Result<LoggedSetResponse>> HandleAsync(LogSetCommand command, CancellationToken ct)
    {
        var session = await sessions.GetByIdAsync(command.SessionId, ct);
        if (session is null || session.UserId != command.Actor)
            return Result<LoggedSetResponse>.Failure(new Error(SessionErrors.NotFound, "Session not found."));

        var preferences = await profiles.GetLoadingPreferencesAsync(command.Actor, ct)
                          ?? new LoadingPreferences(WeightUnit.Kilograms, 2.5m);

        var weight = Weight.Create(command.Weight, preferences.PreferredUnit);
        if (weight.IsFailure)
            return Result<LoggedSetResponse>.Failure(weight.Error);

        var logged = session.LogSet(
            command.SessionExerciseId, command.ClientSetId, command.Kind, weight.Value,
            command.Reps, command.Rir, timeProvider.GetUtcNow());

        if (logged.IsFailure)
            return Result<LoggedSetResponse>.Failure(logged.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result<LoggedSetResponse>.Success(LoggedSetResponse.From(logged.Value));
    }
}

public sealed record EditSetCommand(
    UserId Actor, SessionId SessionId, SessionExerciseId SessionExerciseId, LoggedSetId SetId,
    decimal Weight, int Reps, int? Rir);

public sealed class EditSetCommandHandler(
    IWorkoutSessionRepository sessions,
    IAthleteProfileQueries profiles,
    ITrainingUnitOfWork unitOfWork) : ICommandHandler<EditSetCommand>
{
    public async Task<Result> HandleAsync(EditSetCommand command, CancellationToken ct)
    {
        var session = await sessions.GetByIdAsync(command.SessionId, ct);
        if (session is null || session.UserId != command.Actor)
            return Result.Failure(new Error(SessionErrors.NotFound, "Session not found."));

        var preferences = await profiles.GetLoadingPreferencesAsync(command.Actor, ct)
                          ?? new LoadingPreferences(WeightUnit.Kilograms, 2.5m);

        var weight = Weight.Create(command.Weight, preferences.PreferredUnit);
        if (weight.IsFailure)
            return Result.Failure(weight.Error);

        var edited = session.EditSet(
            command.SessionExerciseId, command.SetId, weight.Value, command.Reps, command.Rir);

        if (edited.IsFailure)
            return edited;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public sealed record DeleteSetCommand(
    UserId Actor, SessionId SessionId, SessionExerciseId SessionExerciseId, LoggedSetId SetId);

public sealed class DeleteSetCommandHandler(
    IWorkoutSessionRepository sessions, ITrainingUnitOfWork unitOfWork) : ICommandHandler<DeleteSetCommand>
{
    public async Task<Result> HandleAsync(DeleteSetCommand command, CancellationToken ct)
    {
        var session = await sessions.GetByIdAsync(command.SessionId, ct);
        if (session is null || session.UserId != command.Actor)
            return Result.Failure(new Error(SessionErrors.NotFound, "Session not found."));

        var deleted = session.DeleteSet(command.SessionExerciseId, command.SetId);
        if (deleted.IsFailure)
            return deleted;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public sealed record AddedExerciseResponse(Guid SessionExerciseId);

public sealed record AddAdHocExerciseCommand(
    UserId Actor, SessionId SessionId, ExerciseId ExerciseId, int WorkingSetCount,
    int RepMin, int RepMax, int RestMinSeconds, int RestMaxSeconds);

/// <summary>
/// Adds an exercise to a session in progress — the ad-hoc path, and also the escape hatch when a
/// machine is occupied and the athlete substitutes something mid-workout. The program is untouched.
/// </summary>
public sealed class AddAdHocExerciseCommandHandler(
    IWorkoutSessionRepository sessions,
    MyGym.Modules.ExerciseCatalog.Contracts.IExerciseCatalogQueries catalog,
    ITrainingUnitOfWork unitOfWork) : ICommandHandler<AddAdHocExerciseCommand, AddedExerciseResponse>
{
    public async Task<Result<AddedExerciseResponse>> HandleAsync(
        AddAdHocExerciseCommand command, CancellationToken ct)
    {
        var session = await sessions.GetByIdAsync(command.SessionId, ct);
        if (session is null || session.UserId != command.Actor)
            return Result<AddedExerciseResponse>.Failure(
                new Error(SessionErrors.NotFound, "Session not found."));

        if (!await catalog.AllExistAsync([command.ExerciseId], ct))
            return Result<AddedExerciseResponse>.Failure(
                new Error("exercise.not_found", "Exercise does not exist."));

        var reps = Domain.ValueObjects.RepRange.Create(command.RepMin, command.RepMax);
        if (reps.IsFailure)
            return Result<AddedExerciseResponse>.Failure(reps.Error);

        var rest = Domain.ValueObjects.RestPeriod.Create(
            TimeSpan.FromSeconds(command.RestMinSeconds), TimeSpan.FromSeconds(command.RestMaxSeconds));
        if (rest.IsFailure)
            return Result<AddedExerciseResponse>.Failure(rest.Error);

        // An ad-hoc exercise has no prescribed ramp: the athlete warms up by feel.
        var snapshot = Domain.ValueObjects.PrescriptionSnapshot.Create(
            command.ExerciseId, null, command.WorkingSetCount, reps.Value, rest.Value,
            Domain.ValueObjects.WarmupRamp.Empty, null);

        if (snapshot.IsFailure)
            return Result<AddedExerciseResponse>.Failure(snapshot.Error);

        var added = session.AddExercise(snapshot.Value);
        if (added.IsFailure)
            return Result<AddedExerciseResponse>.Failure(added.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result<AddedExerciseResponse>.Success(new AddedExerciseResponse(added.Value.Id.Value));
    }
}

public sealed record CompleteSessionCommand(UserId Actor, SessionId SessionId);

public sealed class CompleteSessionCommandHandler(
    IWorkoutSessionRepository sessions, ITrainingUnitOfWork unitOfWork, TimeProvider timeProvider)
    : ICommandHandler<CompleteSessionCommand>
{
    public async Task<Result> HandleAsync(CompleteSessionCommand command, CancellationToken ct)
    {
        var session = await sessions.GetByIdAsync(command.SessionId, ct);
        if (session is null || session.UserId != command.Actor)
            return Result.Failure(new Error(SessionErrors.NotFound, "Session not found."));

        var completed = session.Complete(timeProvider.GetUtcNow());
        if (completed.IsFailure)
            return completed;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public sealed record AbandonSessionCommand(UserId Actor, SessionId SessionId);

public sealed class AbandonSessionCommandHandler(
    IWorkoutSessionRepository sessions, ITrainingUnitOfWork unitOfWork, TimeProvider timeProvider)
    : ICommandHandler<AbandonSessionCommand>
{
    public async Task<Result> HandleAsync(AbandonSessionCommand command, CancellationToken ct)
    {
        var session = await sessions.GetByIdAsync(command.SessionId, ct);
        if (session is null || session.UserId != command.Actor)
            return Result.Failure(new Error(SessionErrors.NotFound, "Session not found."));

        var abandoned = session.Abandon(timeProvider.GetUtcNow());
        if (abandoned.IsFailure)
            return abandoned;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
```

`src/Modules/Training/MyGym.Modules.Training.Application/Sessions/SessionQueries.cs`:

```csharp
using MyGym.Modules.Identity.Contracts;
using MyGym.Modules.Training.Domain.Sessions;
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Application.Sessions;

public sealed record GetSessionQuery(UserId Actor, SessionId SessionId);

public sealed class GetSessionQueryHandler(
    IWorkoutSessionRepository sessions,
    ILastWorkingWeightQuery lastWeights,
    IAthleteProfileQueries profiles) : IQueryHandler<GetSessionQuery, SessionResponse>
{
    public async Task<Result<SessionResponse>> HandleAsync(GetSessionQuery query, CancellationToken ct)
    {
        var session = await sessions.GetByIdAsync(query.SessionId, ct);
        if (session is null || session.UserId != query.Actor)
            return Result<SessionResponse>.Failure(new Error(SessionErrors.NotFound, "Session not found."));

        return Result<SessionResponse>.Success(await EnrichAsync(session, query.Actor, lastWeights, profiles, ct));
    }

    internal static async Task<SessionResponse> EnrichAsync(
        WorkoutSession session,
        UserId actor,
        ILastWorkingWeightQuery lastWeights,
        IAthleteProfileQueries profiles,
        CancellationToken ct)
    {
        var preferences = await profiles.GetLoadingPreferencesAsync(actor, ct)
                          ?? new LoadingPreferences(WeightUnit.Kilograms, 2.5m);

        var increment = WeightIncrement.Create(preferences.Increment, preferences.PreferredUnit).Value;

        var suggested = await lastWeights.GetTopWorkingWeightsAsync(
            actor, session.Exercises.Select(e => e.Prescription.ExerciseId).ToList(), ct);

        return SessionMapper.ToResponse(session, suggested, increment, preferences.PreferredUnit);
    }
}

public sealed record GetCurrentSessionQuery(UserId Actor);

public sealed class GetCurrentSessionQueryHandler(
    IWorkoutSessionRepository sessions,
    ILastWorkingWeightQuery lastWeights,
    IAthleteProfileQueries profiles) : IQueryHandler<GetCurrentSessionQuery, SessionResponse?>
{
    public async Task<Result<SessionResponse?>> HandleAsync(GetCurrentSessionQuery query, CancellationToken ct)
    {
        var session = await sessions.GetInProgressForUserAsync(query.Actor, ct);

        return session is null
            ? Result<SessionResponse?>.Success(null)
            : Result<SessionResponse?>.Success(
                await GetSessionQueryHandler.EnrichAsync(session, query.Actor, lastWeights, profiles, ct));
    }
}

public sealed record ListSessionsQuery(
    UserId Actor, DateTimeOffset? From, DateTimeOffset? To, int Page, int PageSize);
```

`src/Modules/Training/MyGym.Modules.Training.Infrastructure/ListSessionsQueryHandler.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using MyGym.Modules.Training.Application.Sessions;
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Infrastructure;

/// <summary>
/// A read-side handler, so it lives in Infrastructure and projects straight to the response type.
/// No aggregate is materialised to render a list.
/// </summary>
internal sealed class ListSessionsQueryHandler(TrainingDbContext context)
    : IQueryHandler<ListSessionsQuery, IReadOnlyList<SessionSummaryResponse>>
{
    public async Task<Result<IReadOnlyList<SessionSummaryResponse>>> HandleAsync(
        ListSessionsQuery query, CancellationToken ct)
    {
        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, 100);

        var sessions = context.Sessions.AsNoTracking().Where(s => s.UserId == query.Actor);

        if (query.From is { } from)
            sessions = sessions.Where(s => s.StartedAt >= from);
        if (query.To is { } to)
            sessions = sessions.Where(s => s.StartedAt <= to);

        var rows = await sessions
            .OrderByDescending(s => s.StartedAt)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(s => new SessionSummaryResponse(
                s.Id.Value,
                s.Status.ToString(),
                s.StartedAt,
                s.CompletedAt,
                s.Exercises.SelectMany(e => e.Sets)
                    .Count(set => set.Kind == Domain.Sessions.SetKind.Working)))
            .ToListAsync(ct);

        return Result<IReadOnlyList<SessionSummaryResponse>>.Success(rows);
    }
}
```

- [ ] **Step 6: Write the session endpoints**

`src/Host/MyGym.Api/Endpoints/SessionEndpoints.cs`:

```csharp
using MyGym.Api.Auth;
using MyGym.Api.Errors;
using MyGym.Modules.Training.Application.Sessions;
using MyGym.Modules.Training.Domain.Programs;
using MyGym.Modules.Training.Domain.Sessions;
using MyGym.SharedKernel;

namespace MyGym.Api.Endpoints;

public static class SessionEndpoints
{
    public sealed record StartSessionRequest(Guid? ProgramId, Guid? ProgramDayId, decimal? Bodyweight);
    public sealed record LogSetRequest(Guid ClientSetId, SetKind Kind, decimal Weight, int Reps, int? Rir);
    public sealed record EditSetRequest(decimal Weight, int Reps, int? Rir);

    public sealed record AddExerciseRequest(
        Guid ExerciseId, int WorkingSetCount, int RepMin, int RepMax,
        int RestMinSeconds, int RestMaxSeconds);

    public static void MapSessionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/sessions").RequireAuthorization();

        group.MapPost("", async (
            StartSessionRequest request,
            ICurrentUser me,
            ICommandHandler<StartSessionCommand, SessionResponse> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new StartSessionCommand(
                me.UserId,
                request.ProgramId is { } p ? ProgramId.From(p) : null,
                request.ProgramDayId is { } d ? ProgramDayId.From(d) : null,
                request.Bodyweight), ct);

            return result.ToHttpResult(session => Results.Created($"/api/sessions/{session.Id}", session));
        });

        group.MapGet("/current", async (
            ICurrentUser me,
            IQueryHandler<GetCurrentSessionQuery, SessionResponse?> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new GetCurrentSessionQuery(me.UserId), ct);
            return result.ToHttpResult(session =>
                session is null ? Results.NoContent() : Results.Ok(session));
        });

        group.MapGet("", async (
            DateTimeOffset? from,
            DateTimeOffset? to,
            int? page,
            int? pageSize,
            ICurrentUser me,
            IQueryHandler<ListSessionsQuery, IReadOnlyList<SessionSummaryResponse>> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(
                new ListSessionsQuery(me.UserId, from, to, page ?? 1, pageSize ?? 20), ct);

            return result.ToHttpResult();
        });

        group.MapGet("/{id:guid}", async (
            Guid id,
            ICurrentUser me,
            IQueryHandler<GetSessionQuery, SessionResponse> handler,
            CancellationToken ct) =>
            (await handler.HandleAsync(new GetSessionQuery(me.UserId, SessionId.From(id)), ct))
                .ToHttpResult());

        group.MapPost("/{id:guid}/exercises", async (
            Guid id,
            AddExerciseRequest request,
            ICurrentUser me,
            ICommandHandler<AddAdHocExerciseCommand, AddedExerciseResponse> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new AddAdHocExerciseCommand(
                me.UserId, SessionId.From(id), ExerciseId.From(request.ExerciseId),
                request.WorkingSetCount, request.RepMin, request.RepMax,
                request.RestMinSeconds, request.RestMaxSeconds), ct);

            return result.ToHttpResult(added => Results.Created(
                $"/api/sessions/{id}/exercises/{added.SessionExerciseId}", added));
        });

        group.MapPost("/{id:guid}/exercises/{exerciseId:guid}/sets", async (
            Guid id,
            Guid exerciseId,
            LogSetRequest request,
            ICurrentUser me,
            ICommandHandler<LogSetCommand, LoggedSetResponse> handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new LogSetCommand(
                me.UserId, SessionId.From(id), SessionExerciseId.From(exerciseId),
                request.ClientSetId, request.Kind, request.Weight, request.Reps, request.Rir), ct);

            return result.ToHttpResult(set => Results.Created(
                $"/api/sessions/{id}/exercises/{exerciseId}/sets/{set.Id}", set));
        });

        group.MapPatch("/{id:guid}/exercises/{exerciseId:guid}/sets/{setId:guid}", async (
            Guid id, Guid exerciseId, Guid setId, EditSetRequest request,
            ICurrentUser me, ICommandHandler<EditSetCommand> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new EditSetCommand(
                me.UserId, SessionId.From(id), SessionExerciseId.From(exerciseId),
                LoggedSetId.From(setId), request.Weight, request.Reps, request.Rir), ct))
                .ToHttpResult());

        group.MapDelete("/{id:guid}/exercises/{exerciseId:guid}/sets/{setId:guid}", async (
            Guid id, Guid exerciseId, Guid setId,
            ICurrentUser me, ICommandHandler<DeleteSetCommand> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new DeleteSetCommand(
                me.UserId, SessionId.From(id), SessionExerciseId.From(exerciseId),
                LoggedSetId.From(setId)), ct))
                .ToHttpResult());

        group.MapPost("/{id:guid}/complete", async (
            Guid id, ICurrentUser me, ICommandHandler<CompleteSessionCommand> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new CompleteSessionCommand(me.UserId, SessionId.From(id)), ct))
                .ToHttpResult());

        group.MapPost("/{id:guid}/abandon", async (
            Guid id, ICurrentUser me, ICommandHandler<AbandonSessionCommand> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new AbandonSessionCommand(me.UserId, SessionId.From(id)), ct))
                .ToHttpResult());
    }
}
```

Register in `Program.cs`:

```csharp
builder.Services.AddScoped<ICommandHandler<StartSessionCommand, SessionResponse>, StartSessionCommandHandler>();
builder.Services.AddScoped<ICommandHandler<LogSetCommand, LoggedSetResponse>, LogSetCommandHandler>();
builder.Services.AddScoped<ICommandHandler<AddAdHocExerciseCommand, AddedExerciseResponse>,
    AddAdHocExerciseCommandHandler>();
builder.Services.AddScoped<ICommandHandler<EditSetCommand>, EditSetCommandHandler>();
builder.Services.AddScoped<ICommandHandler<DeleteSetCommand>, DeleteSetCommandHandler>();
builder.Services.AddScoped<ICommandHandler<CompleteSessionCommand>, CompleteSessionCommandHandler>();
builder.Services.AddScoped<ICommandHandler<AbandonSessionCommand>, AbandonSessionCommandHandler>();
builder.Services.AddScoped<IQueryHandler<GetSessionQuery, SessionResponse>, GetSessionQueryHandler>();
builder.Services.AddScoped<IQueryHandler<GetCurrentSessionQuery, SessionResponse?>, GetCurrentSessionQueryHandler>();

// ...after MapExerciseEndpoints():
app.MapSessionEndpoints();
```

`ListSessionsQueryHandler` is `internal` to Training.Infrastructure, so register it from
`TrainingInfrastructureExtensions.AddTrainingInfrastructure`:

```csharp
        services.AddScoped<IQueryHandler<ListSessionsQuery, IReadOnlyList<SessionSummaryResponse>>,
            ListSessionsQueryHandler>();
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/MyGym.IntegrationTests --filter SessionEndpointTests`
Expected: PASS — 10 tests, including the full critical path.

- [ ] **Step 8: Run the whole suite and commit**

Run: `dotnet test`
Expected: PASS — every suite.

```bash
git add src tests
git commit -m "feat: add session endpoints with idempotent set logging and critical path coverage"
```

---

### Task 14: Account deletion and the Telegram bot webhook

**Files:**
- Create: `src/Modules/Identity/MyGym.Modules.Identity.Application/DeleteAccount.cs`
- Create: `src/Modules/Training/MyGym.Modules.Training.Application/PurgeAthleteData.cs`
- Create: `src/Modules/Training/MyGym.Modules.Training.Infrastructure/TrainingDataPurger.cs`
- Create: `src/Host/MyGym.Api/Endpoints/TelegramWebhookEndpoints.cs`
- Test: `tests/MyGym.IntegrationTests/{AccountDeletionTests,WebhookTests}.cs`

**Interfaces:**
- Consumes: Tasks 1–13.
- Produces:
  - `ITrainingDataPurger` (declared in `Training.Contracts`) with `PurgeForUserAsync(UserId, CancellationToken) → Task<int>`; implemented by `TrainingDataPurger`
  - `DeleteAccountCommand(UserId)` and its handler, which purges training data, revokes refresh tokens, and removes the user
  - `TelegramWebhookEndpoints.MapTelegramWebhook(IEndpointRouteBuilder)` — secret-token guarded, answers `/start` with a Mini App launch button

- [ ] **Step 1: Write the failing tests**

`tests/MyGym.IntegrationTests/AccountDeletionTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace MyGym.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public class AccountDeletionTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFixture _api = null!;

    public async ValueTask InitializeAsync()
    {
        _api = new ApiFixture(postgres);
        await using var catalog = postgres.CreateCatalogContext();
        await MyGym.Modules.ExerciseCatalog.Infrastructure.CatalogSeeder.SeedAsync(
            catalog, CancellationToken.None);
    }

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task Deleting_an_account_removes_the_user_and_all_training_data()
    {
        var telegramId = Random.Shared.NextInt64(1, long.MaxValue);
        var client = await _api.CreateAuthenticatedClientAsync(telegramId);
        var me = await client.GetFromJsonAsync<MePayload>("/api/me");

        await using var catalog = postgres.CreateCatalogContext();
        var bench = await catalog.Exercises.SingleAsync(e => e.Slug == "flat-barbell-bench-press");

        var program = await client.PostAsJsonAsync("/api/programs", new
        {
            name = "To be deleted",
            days = new[]
            {
                new
                {
                    label = "Day",
                    dayOfWeek = (string?)null,
                    prescriptions = new[]
                    {
                        new
                        {
                            exerciseId = bench.Id.Value,
                            alternativeExerciseId = (Guid?)null,
                            workingSetCount = 3, repMin = 6, repMax = 8,
                            restMinSeconds = 180, restMaxSeconds = 180,
                            warmup = Array.Empty<object>(),
                            notes = (string?)null
                        }
                    }
                }
            }
        });
        program.EnsureSuccessStatusCode();

        var deleted = await client.DeleteAsync("/api/me");
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await using var identity = postgres.CreateIdentityContext();
        (await identity.Users.AnyAsync(u => u.TelegramUserId == telegramId)).ShouldBeFalse();
        (await identity.RefreshTokens.CountAsync(t => t.RevokedAt == null
            && t.UserId == MyGym.SharedKernel.UserId.From(me!.Id))).ShouldBe(0);

        await using var training = postgres.CreateTrainingContext();
        (await training.Programs.CountAsync(
            p => p.OwnerUserId == MyGym.SharedKernel.UserId.From(me.Id))).ShouldBe(0);
    }

    [Fact]
    public async Task Tokens_stop_working_after_deletion()
    {
        var client = await _api.CreateAuthenticatedClientAsync(Random.Shared.NextInt64(1, long.MaxValue));
        await client.DeleteAsync("/api/me");

        // The access token is still cryptographically valid until it expires, but the account is
        // gone, so the profile lookup must fail rather than resurrect anything.
        (await client.GetAsync("/api/me")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_deleted_athlete_can_sign_up_again_as_a_new_account()
    {
        var telegramId = Random.Shared.NextInt64(1, long.MaxValue);
        var first = await _api.CreateAuthenticatedClientAsync(telegramId);
        var firstMe = await first.GetFromJsonAsync<MePayload>("/api/me");
        await first.DeleteAsync("/api/me");

        var second = await _api.CreateAuthenticatedClientAsync(telegramId);
        var secondMe = await second.GetFromJsonAsync<MePayload>("/api/me");

        secondMe!.Id.ShouldNotBe(firstMe!.Id);
    }

    internal sealed record MePayload(Guid Id);
}
```

`tests/MyGym.IntegrationTests/WebhookTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Shouldly;

namespace MyGym.IntegrationTests;

[Collection(nameof(PostgresCollection))]
public class WebhookTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFixture _api = null!;

    public ValueTask InitializeAsync()
    {
        _api = new ApiFixture(postgres);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    private static object StartUpdate() => new
    {
        update_id = 1,
        message = new
        {
            message_id = 1,
            date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            chat = new { id = 555L, type = "private" },
            from = new { id = 555L, is_bot = false, first_name = "Akmal" },
            text = "/start"
        }
    };

    [Fact]
    public async Task A_webhook_request_without_the_secret_token_is_rejected()
    {
        var response = await _api.CreateClient().PostAsJsonAsync("/api/telegram/webhook", StartUpdate());

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_webhook_request_with_a_wrong_secret_token_is_rejected()
    {
        var client = _api.CreateClient();
        client.DefaultRequestHeaders.Add("X-Telegram-Bot-Api-Secret-Token", "wrong");

        var response = await client.PostAsJsonAsync("/api/telegram/webhook", StartUpdate());

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_valid_start_update_is_accepted()
    {
        var client = _api.CreateClient();
        client.DefaultRequestHeaders.Add("X-Telegram-Bot-Api-Secret-Token", "test-webhook-secret");

        var response = await client.PostAsJsonAsync("/api/telegram/webhook", StartUpdate());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
```

> Telegram retries any non-2xx response, so the webhook must answer 200 even for update types it
> decides to ignore. The 401 cases are the exception: an unauthenticated caller is not Telegram.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/MyGym.IntegrationTests --filter "AccountDeletionTests|WebhookTests"`
Expected: FAIL — `DELETE /api/me` and the webhook return 404.

- [ ] **Step 3: Write the training data purger**

`src/Modules/Training/MyGym.Modules.Training.Contracts/ITrainingDataPurger.cs`:

```csharp
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Contracts;

/// <summary>
/// Erases every trace of an athlete's training data. Exposed through Contracts so Identity can call
/// it during account deletion without referencing Training's internals.
/// </summary>
public interface ITrainingDataPurger
{
    Task<int> PurgeForUserAsync(UserId userId, CancellationToken ct);
}
```

`src/Modules/Training/MyGym.Modules.Training.Infrastructure/TrainingDataPurger.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using MyGym.Modules.Training.Contracts;
using MyGym.SharedKernel;

namespace MyGym.Modules.Training.Infrastructure;

internal sealed class TrainingDataPurger(TrainingDbContext context) : ITrainingDataPurger
{
    public async Task<int> PurgeForUserAsync(UserId userId, CancellationToken ct)
    {
        // Owned child rows (session_exercises, logged_sets, program_days, exercise_prescriptions)
        // cascade from their roots, so deleting the roots is sufficient and leaves nothing behind.
        var sessions = await context.Sessions.Where(s => s.UserId == userId).ExecuteDeleteAsync(ct);
        var programs = await context.Programs.Where(p => p.OwnerUserId == userId).ExecuteDeleteAsync(ct);

        return sessions + programs;
    }
}
```

Register it in `TrainingInfrastructureExtensions.AddTrainingInfrastructure`:

```csharp
        services.AddScoped<ITrainingDataPurger, TrainingDataPurger>();
```

- [ ] **Step 4: Write the delete-account handler and endpoint**

`src/Modules/Identity/MyGym.Modules.Identity.Application/DeleteAccount.cs`:

```csharp
using MyGym.Modules.Identity.Domain;
using MyGym.Modules.Training.Contracts;
using MyGym.SharedKernel;

namespace MyGym.Modules.Identity.Application;

public sealed record DeleteAccountCommand(UserId UserId);

public sealed class DeleteAccountCommandHandler(
    IAppUserRepository users,
    IRefreshTokenRepository refreshTokens,
    ITrainingDataPurger trainingData,
    IIdentityUnitOfWork unitOfWork,
    TimeProvider timeProvider) : ICommandHandler<DeleteAccountCommand>
{
    public async Task<Result> HandleAsync(DeleteAccountCommand command, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(command.UserId, ct);
        if (user is null)
            return Result.Failure(new Error(IdentityErrors.UserNotFound, "User not found."));

        // Training data first: if that fails, the account survives and the athlete can retry.
        // The reverse order could orphan sessions with no owner to delete them.
        await trainingData.PurgeForUserAsync(command.UserId, ct);

        await refreshTokens.RevokeAllForUserAsync(command.UserId, timeProvider.GetUtcNow(), ct);
        users.Remove(user);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
```

Reference `Training.Contracts` from Identity.Application:

```bash
dotnet add src/Modules/Identity/MyGym.Modules.Identity.Application reference \
           src/Modules/Training/MyGym.Modules.Training.Contracts
```

Add to `ProfileEndpoints.MapProfileEndpoints`:

```csharp
        group.MapDelete("", async (
            ICurrentUser me, ICommandHandler<DeleteAccountCommand> handler, CancellationToken ct) =>
            (await handler.HandleAsync(new DeleteAccountCommand(me.UserId), ct)).ToHttpResult());
```

And register in `Program.cs`:

```csharp
builder.Services.AddScoped<ICommandHandler<DeleteAccountCommand>, DeleteAccountCommandHandler>();
```

- [ ] **Step 5: Write the webhook**

`src/Host/MyGym.Api/Endpoints/TelegramWebhookEndpoints.cs`:

```csharp
using Microsoft.Extensions.Options;
using MyGym.Shared.Infrastructure.Auth;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace MyGym.Api.Endpoints;

public static class TelegramWebhookEndpoints
{
    public const string SecretTokenHeader = "X-Telegram-Bot-Api-Secret-Token";

    public static void MapTelegramWebhook(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/telegram/webhook", async (
                HttpContext context,
                Update update,
                IOptions<TelegramAuthOptions> telegram,
                IOptions<MiniAppOptions> miniApp,
                ITelegramBotClient bot,
                ILoggerFactory loggerFactory,
                CancellationToken ct) =>
            {
                var presented = context.Request.Headers[SecretTokenHeader].ToString();
                if (!CryptographicEquals(presented, telegram.Value.WebhookSecretToken))
                    return Results.Unauthorized();

                if (update.Message?.Text is "/start" && update.Message.Chat.Id is var chatId)
                {
                    await bot.SendMessage(
                        chatId: chatId,
                        text: "Salom! Mashg'ulotlarni yozib borish uchun ilovani ochadi.",
                        replyMarkup: new InlineKeyboardMarkup(
                            InlineKeyboardButton.WithWebApp("MyGym", new WebAppInfo { Url = miniApp.Value.Url })),
                        cancellationToken: ct);
                }

                // Always 200: Telegram retries anything else, and a retry storm is worse than a
                // silently ignored update type we do not handle yet.
                return Results.Ok();
            })
            .AllowAnonymous();
    }

    private static bool CryptographicEquals(string presented, string expected)
    {
        var a = System.Text.Encoding.UTF8.GetBytes(presented);
        var b = System.Text.Encoding.UTF8.GetBytes(expected);
        return a.Length == b.Length && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(a, b);
    }
}

public sealed class MiniAppOptions
{
    public const string SectionName = "MiniApp";

    public required string Url { get; init; }
}
```

Register in `Program.cs`:

```csharp
builder.Services.Configure<MiniAppOptions>(builder.Configuration.GetSection(MiniAppOptions.SectionName));
builder.Services.AddSingleton<ITelegramBotClient>(sp =>
    new TelegramBotClient(sp.GetRequiredService<IOptions<TelegramAuthOptions>>().Value.BotToken));

// ...after MapSessionEndpoints():
app.MapTelegramWebhook();
```

Add to `appsettings.json`:

```json
  "MiniApp": { "Url": "https://localhost:5001" },
```

Add the package:

```bash
dotnet add src/Host/MyGym.Api package Telegram.Bot
```

> The `/start` test asserts only that a valid update is accepted with 200. It does not assert that a
> message was sent, because that would require calling the real Telegram API. Verifying the button
> actually appears is a manual step: set the webhook with
> `curl -F "url=https://<host>/api/telegram/webhook" -F "secret_token=<secret>" https://api.telegram.org/bot<token>/setWebhook`
> and send `/start` to the bot.

- [ ] **Step 6: Run tests and commit**

Run: `dotnet test`
Expected: PASS — all suites including 3 deletion tests and 3 webhook tests.

```bash
git add src tests
git commit -m "feat: add account deletion with training data purge and Telegram webhook"
```

---

### Task 15: Architecture tests, containerisation, and CI

The boundary rules from the design become executable here. Without this task, "modular monolith" is
a claim; with it, a violation fails the build.

**Files:**
- Create: `tests/MyGym.ArchitectureTests/{ModuleBoundaryTests,DomainPurityTests}.cs`
- Create: `Dockerfile`, `docker-compose.yml`, `.dockerignore`
- Create: `.github/workflows/ci.yml`
- Create: `README.md`

**Interfaces:**
- Consumes: every project built in Tasks 1–14.
- Produces: CI that runs `dotnet test` with Docker available, and a runnable `docker compose up`.

- [ ] **Step 1: Create the project and write the failing tests**

```bash
dotnet new xunit3 -o tests/MyGym.ArchitectureTests
dotnet sln add tests/MyGym.ArchitectureTests
dotnet add tests/MyGym.ArchitectureTests package NetArchTest.Rules
dotnet add tests/MyGym.ArchitectureTests package Shouldly
dotnet add tests/MyGym.ArchitectureTests reference \
  src/Shared/MyGym.SharedKernel \
  src/Modules/Training/MyGym.Modules.Training.Domain \
  src/Modules/Training/MyGym.Modules.Training.Application \
  src/Modules/Training/MyGym.Modules.Training.Infrastructure \
  src/Modules/Identity/MyGym.Modules.Identity.Domain \
  src/Modules/Identity/MyGym.Modules.Identity.Application \
  src/Modules/Identity/MyGym.Modules.Identity.Infrastructure \
  src/Modules/ExerciseCatalog/MyGym.Modules.ExerciseCatalog.Domain \
  src/Modules/ExerciseCatalog/MyGym.Modules.ExerciseCatalog.Infrastructure
```

`tests/MyGym.ArchitectureTests/DomainPurityTests.cs`:

```csharp
using System.Reflection;
using MyGym.Modules.ExerciseCatalog.Domain;
using MyGym.Modules.Identity.Domain;
using MyGym.Modules.Training.Domain.Programs;
using NetArchTest.Rules;
using Shouldly;

namespace MyGym.ArchitectureTests;

public class DomainPurityTests
{
    public static TheoryData<Assembly> DomainAssemblies => new()
    {
        typeof(TrainingProgram).Assembly,
        typeof(AppUser).Assembly,
        typeof(Exercise).Assembly
    };

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

    private static string Describe(TestResult result) =>
        result.FailingTypeNames is null
            ? "no failing types reported"
            : string.Join(Environment.NewLine, result.FailingTypeNames);
}
```

`tests/MyGym.ArchitectureTests/ModuleBoundaryTests.cs`:

```csharp
using System.Reflection;
using Shouldly;

namespace MyGym.ArchitectureTests;

public class ModuleBoundaryTests
{
    private static readonly string[] Modules = ["Training", "Identity", "ExerciseCatalog"];

    public static TheoryData<Assembly> ModuleAssemblies
    {
        get
        {
            var data = new TheoryData<Assembly>();
            foreach (var assembly in new[]
                     {
                         typeof(Modules.Training.Domain.Programs.TrainingProgram).Assembly,
                         typeof(Modules.Training.Application.Programs.CreateProgramCommand).Assembly,
                         typeof(Modules.Training.Infrastructure.TrainingDbContext).Assembly,
                         typeof(Modules.Identity.Domain.AppUser).Assembly,
                         typeof(Modules.Identity.Application.GetProfileQuery).Assembly,
                         typeof(Modules.Identity.Infrastructure.IdentityDbContext).Assembly,
                         typeof(Modules.ExerciseCatalog.Domain.Exercise).Assembly,
                         typeof(Modules.ExerciseCatalog.Infrastructure.CatalogDbContext).Assembly
                     })
            {
                data.Add(assembly);
            }

            return data;
        }
    }

    /// <summary>
    /// The load-bearing rule of the whole design: a module may reach another module only through
    /// its Contracts assembly.
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

    [Fact]
    public void The_api_host_is_the_only_project_that_references_every_module()
    {
        var host = typeof(Program).Assembly;

        host.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Count(name => name.StartsWith("MyGym.Modules.", StringComparison.Ordinal))
            .ShouldBeGreaterThan(3);
    }
}
```

> The last test needs a reference to the host: `dotnet add tests/MyGym.ArchitectureTests reference
> src/Host/MyGym.Api`. If that creates an awkward cycle with the integration tests, delete this one
> test — the two `[Theory]` rules above are the ones that matter.

- [ ] **Step 2: Run the architecture tests**

Run: `dotnet test tests/MyGym.ArchitectureTests`
Expected: PASS. If `A_module_only_reaches_other_modules_through_their_contracts` fails, the fix is to
move the shared type into the owning module's `Contracts` project — never to relax the rule.

- [ ] **Step 3: Write the container files**

`Dockerfile`:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props MyGym.sln ./
COPY src/ src/
RUN dotnet restore src/Host/MyGym.Api/MyGym.Api.csproj
RUN dotnet publish src/Host/MyGym.Api/MyGym.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "MyGym.Api.dll"]
```

`.dockerignore`:

```
**/bin/
**/obj/
**/node_modules/
.git/
docs/
tests/
clients/
```

`docker-compose.yml`:

```yaml
services:
  postgres:
    image: postgres:17-alpine
    environment:
      POSTGRES_DB: mygym
      POSTGRES_USER: mygym
      POSTGRES_PASSWORD: mygym-local
    ports: ["5432:5432"]
    volumes: ["pgdata:/var/lib/postgresql/data"]
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U mygym"]
      interval: 5s
      retries: 10

  api:
    build: .
    depends_on:
      postgres: { condition: service_healthy }
    environment:
      ConnectionStrings__Postgres: "Host=postgres;Database=mygym;Username=mygym;Password=mygym-local"
      Telegram__BotToken: "${TELEGRAM_BOT_TOKEN}"
      Telegram__WebhookSecretToken: "${TELEGRAM_WEBHOOK_SECRET}"
      Jwt__Issuer: mygym
      Jwt__Audience: mygym-miniapp
      Jwt__SigningKey: "${JWT_SIGNING_KEY}"
      MiniApp__Url: "${MINIAPP_URL}"
    ports: ["8080:8080"]

volumes:
  pgdata:
```

> Compose reads these from a local `.env` file, which `.gitignore` already excludes. Never commit
> real tokens.

- [ ] **Step 4: Write CI**

`.github/workflows/ci.yml`:

```yaml
name: CI

on:
  push:
    branches: [main]
  pull_request:

jobs:
  build-and-test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Restore
        run: dotnet restore

      - name: Build
        run: dotnet build --no-restore --configuration Release

      # Testcontainers starts PostgreSQL itself; the Docker daemon on the runner is enough.
      - name: Test
        run: dotnet test --no-build --configuration Release --logger "trx;LogFileName=results.trx"

      - name: Upload test results
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: test-results
          path: '**/results.trx'
```

- [ ] **Step 5: Write the README**

`README.md`:

```markdown
# MyGym

Telegram Mini App for tracking strength training, with a .NET 10 modular-monolith backend.

## Status

Sub-project 1 of 4 — the workout logger. See `docs/superpowers/specs/` for the design and
`docs/superpowers/plans/` for the implementation plan.

Planned next: progress engine (volume, estimated 1RM, PR and stall detection), social layer,
then the AI coach.

## Architecture

Three bounded contexts — Identity, ExerciseCatalog, Training — each with clean-architecture layers
inside it. One PostgreSQL database, a schema per module, no cross-schema foreign keys. A module may
reference another module's `Contracts` project and nothing else; `tests/MyGym.ArchitectureTests`
fails the build if that is violated.

## Running locally

Requires .NET 10 SDK and Docker.

    cp .env.example .env     # then fill in the values
    docker compose up --build

The API listens on http://localhost:8080; `GET /health` should return `{"status":"ok"}`.

## Tests

    dotnet test

Integration tests start a real PostgreSQL 17 container through Testcontainers, so Docker must be
running. EF Core's InMemory provider is deliberately not used: it ignores the unique indexes that
enforce one-session-per-athlete and idempotent set logging.

## Configuration

Supply secrets through environment variables, never in `appsettings.json`:

| Variable | Purpose |
|---|---|
| `ConnectionStrings__Postgres` | Database connection string |
| `Telegram__BotToken` | Bot token, used for initData validation and sending messages |
| `Telegram__WebhookSecretToken` | Shared secret for the webhook endpoint |
| `Jwt__SigningKey` | Access-token signing key, at least 32 bytes |
| `MiniApp__Url` | HTTPS URL the `/start` button opens |
```

Also create `.env.example` listing the same variable names with empty values.

- [ ] **Step 6: Run everything and commit**

Run: `dotnet test`
Expected: PASS — every suite, architecture tests included.

Run: `docker compose config`
Expected: the compose file parses with no errors.

```bash
git add tests/MyGym.ArchitectureTests Dockerfile .dockerignore docker-compose.yml .env.example .github README.md
git commit -m "feat: add architecture tests, containerisation, and CI"
```

---

## Done when

- `dotnet test` passes: domain unit tests, application and integration tests against real
  PostgreSQL, and architecture tests.
- `docker compose up --build` serves `GET /health`.
- The critical path works end to end against a running instance: create a program, activate it,
  start its day, log sets, complete, and see the next preview suggest the weight actually lifted.
- Every endpoint in spec §7 exists, and the two database-enforced invariants from spec §5 have
  tests that fail without their indexes.

## Deliberately not built

Carried forward from spec §13: auto-progression, 1RM estimates, volume analytics, PR and stall
detection, the transactional outbox, the social layer, the AI coach, and local-first offline sync.
The React Mini App client is the subject of its own plan.
