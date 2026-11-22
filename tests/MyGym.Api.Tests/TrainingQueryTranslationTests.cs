using Microsoft.EntityFrameworkCore;
using MyGym.Modules.Training.Domain.Programs;
using MyGym.Modules.Training.Domain.Sessions;
using MyGym.Modules.Training.Infrastructure;
using MyGym.SharedKernel;
using Shouldly;

namespace MyGym.Api.Tests;

/// <summary>
/// RULING R2: <c>TrainingProgram.Days</c>, <c>ProgramDay.Prescriptions</c>,
/// <c>WorkoutSession.Exercises</c> and <c>SessionExercise.Sets</c> are computed accessors that
/// re-sort a fresh copy of a private backing field on every access; EF Core maps only the backing
/// field, so a database-side query that touches the public accessor is a client-side projection EF
/// cannot translate and throws <see cref="InvalidOperationException"/> at query time — a failure
/// mode that never shows up at compile time and (without Docker) never shows up in this task's
/// deferred integration tests either.
///
/// <see cref="DbContext"/> model building and LINQ-to-SQL translation both happen without opening
/// a connection, so <see cref="EntityFrameworkQueryableExtensions.ToQueryString"/> can prove a
/// query translates — or catch the exact "could not be translated" failure — entirely offline.
/// These tests mirror (but intentionally do not call into) the exact LINQ shapes used by
/// <c>ListProgramsQueryHandler</c> and <c>LastWorkingWeightQuery</c> in
/// MyGym.Modules.Training.Infrastructure, both internal and therefore not directly reachable from
/// this test project. Keeping the two shapes in sync is a manual responsibility this test cannot
/// enforce — see the task report for this trade-off.
/// </summary>
public class TrainingQueryTranslationTests
{
    private static TrainingDbContext CreateContext()
    {
        // A syntactically valid Npgsql connection string that is never actually opened: EF Core
        // only needs it to pick the Npgsql query-translation pipeline. ToQueryString() does not
        // touch the network.
        var options = new DbContextOptionsBuilder<TrainingDbContext>()
            .UseNpgsql("Host=localhost;Database=translation-check;Username=postgres;Password=postgres")
            .UseSnakeCaseNamingConvention()
            .Options;

        return new TrainingDbContext(options, dispatcher: null);
    }

    [Fact]
    public void Program_day_count_projection_translates_via_EF_Property_on_the_backing_field()
    {
        using var context = CreateContext();
        var userId = UserId.New();

        // Same shape as ListProgramsQueryHandler: EF.Property<List<ProgramDay>>(p, "_days").Count
        // reaches the mapped backing field instead of the Ignore'd Days accessor.
        var query = context.Programs
            .AsNoTracking()
            .Where(p => p.OwnerUserId == userId
                        || (p.Visibility == ProgramVisibility.Public && p.Status == ProgramStatus.Active))
            .Select(p => EF.Property<List<ProgramDay>>(p, "_days").Count);

        var sql = query.ToQueryString();

        // The real fix produces a genuine server-side aggregate: a correlated
        // "SELECT count(*) ... FROM training.program_days ... WHERE p.id = p0.program_id" scalar
        // subquery, not a full fetch of the owned graph.
        sql.ShouldContain("count(*)");
        sql.ShouldContain("program_days");
    }

    [Fact]
    public void Program_day_count_via_the_public_Days_accessor_does_not_produce_a_count_aggregate()
    {
        // Negative control, with the actual (not assumed) failure mode. The brief's original,
        // un-ruled version — p.Days.Count, reaching the Ignore'd computed accessor instead of the
        // "_days" backing field — turns out NOT to throw at translation time under this EF Core
        // version: because the projection is the query's outermost/terminal operator, EF Core 3.0+'s
        // "client evaluation only in the top-level projection" carve-out applies, and it silently
        // falls back to fetching every column of the program AND its entire owned "_days" (and, in
        // the real handler's multi-program case, every program's days) and computing .Count in
        // memory per row afterwards — confirmed below: the generated SQL selects every program and
        // program_day column, with no COUNT/aggregate anywhere in it.
        //
        // That is arguably worse than an exception: it is silently correct but pulls the entire
        // owned graph over the wire for every program just to report a day count on a list screen,
        // which is exactly what "queries bypass the domain: project straight to response DTOs;
        // never load an aggregate to serve a read" forbids. The EF.Property-based fix above avoids
        // this: it never touches "_days" at all except as a correlated COUNT.
        using var context = CreateContext();
        var userId = UserId.New();

        var query = context.Programs
            .AsNoTracking()
            .Where(p => p.OwnerUserId == userId)
            .Select(p => p.Days.Count);

        var sql = query.ToQueryString();

        sql.ShouldNotContain("count(*)");
        sql.ShouldContain("program_days"); // the whole table's columns, not a count of it
    }

    [Fact]
    public void Last_working_weight_round_trips_translate_without_touching_the_ignored_collection_accessors()
    {
        using var context = CreateContext();
        var userId = UserId.New();
        var exerciseIds = new List<ExerciseId> { ExerciseId.New() };

        // Round trip 1: candidate completed sessions, touching only Sessions' own columns.
        var sessionsQuery = context.Sessions
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.Status == SessionStatus.Completed)
            .OrderByDescending(s => s.CompletedAt)
            .Take(40)
            .Select(s => new { s.Id, s.CompletedAt });

        var sessionIds = new List<SessionId>();

        // Round trip 2: SessionExercise and LoggedSet queried directly via context.Set<T>() —
        // both are regular (non-owned) entity types, so this needs no EF.Property at all; it never
        // goes near WorkoutSession.Exercises or SessionExercise.Sets.
        var exercisesQuery = context.Set<SessionExercise>()
            .AsNoTracking()
            .Where(e => sessionIds.Contains(e.SessionId) && exerciseIds.Contains(e.Prescription.ExerciseId))
            .Select(e => new
            {
                e.SessionId,
                ExerciseId = e.Prescription.ExerciseId,
                TopWorkingValue = context.Set<LoggedSet>()
                    .Where(set => set.SessionExerciseId == e.Id && set.Kind == SetKind.Working)
                    .Max(set => (decimal?)set.Weight.Value),
                Unit = context.Set<LoggedSet>()
                    .Where(set => set.SessionExerciseId == e.Id && set.Kind == SetKind.Working)
                    .Select(set => set.Weight.Unit)
                    .FirstOrDefault()
            })
            .Where(row => row.TopWorkingValue != null);

        var sessionsSql = sessionsQuery.ToQueryString();
        var exercisesSql = exercisesQuery.ToQueryString();

        sessionsSql.ToUpperInvariant().ShouldContain("SELECT");
        exercisesSql.ToUpperInvariant().ShouldContain("SELECT");

        // Confirms this is a real correlated-subquery translation (per-row MAX over logged_sets),
        // not a full fetch of every session's exercises and sets followed by client-side grouping.
        exercisesSql.ShouldContain("max(");
        exercisesSql.ShouldContain("logged_sets");
    }

    [Fact]
    public void Session_history_working_set_count_translates_via_SessionExercise_and_LoggedSet_entity_sets()
    {
        // Same shape as ListSessionsQueryHandler (Task 13): the brief's original version —
        // s.Exercises.SelectMany(e => e.Sets).Count(set => set.Kind == SetKind.Working) — reaches
        // WorkoutSession.Exercises and SessionExercise.Sets, the exact pair of Ignore'd computed
        // accessors this class already proves (above) silently falls back to a full client-side
        // fetch-and-count instead of throwing. The fix never touches either accessor: it reaches
        // SessionExercise and LoggedSet directly via context.Set<T>(), with the working-set count
        // expressed as a correlated Count(...) subquery per session-exercise row.
        using var context = CreateContext();
        var sessionIds = new List<SessionId> { SessionId.New() };

        var query = context.Set<SessionExercise>()
            .AsNoTracking()
            .Where(e => sessionIds.Contains(e.SessionId))
            .Select(e => new
            {
                e.SessionId,
                WorkingCount = context.Set<LoggedSet>()
                    .Count(set => set.SessionExerciseId == e.Id && set.Kind == SetKind.Working)
            });

        var sql = query.ToQueryString();

        // A genuine correlated "SELECT count(*) ... FROM training.logged_sets ... WHERE
        // session_exercise_id = s.id" scalar subquery per row, not a fetch of every session's
        // exercises and sets followed by in-memory counting.
        sql.ShouldContain("count(*)");
        sql.ShouldContain("logged_sets");
    }
}
