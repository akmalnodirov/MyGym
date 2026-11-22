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
            await write.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var read = fixture.CreateTrainingContext();
        var loaded = await read.Programs.SingleAsync(p => p.Id == program.Id, TestContext.Current.CancellationToken);

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
            await write.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var read = fixture.CreateTrainingContext();
        var loaded = await read.Programs.AsNoTracking().SingleAsync(p => p.Id == program.Id, TestContext.Current.CancellationToken);

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
            await write.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // Now change the program the way an athlete would in a later block.
        await using (var edit = fixture.CreateTrainingContext())
        {
            var loaded = await edit.Programs.SingleAsync(p => p.Id == program.Id, TestContext.Current.CancellationToken);
            var day = loaded.Days[0];
            loaded.AddPrescription(
                day.Id, ExerciseId.New(), null, 5,
                RepRange.Create(12, 15).Value,
                RestPeriod.Create(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2)).Value,
                WarmupRamp.Empty, null);
            await edit.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var read = fixture.CreateTrainingContext();
        var storedSession = await read.Sessions.AsNoTracking().SingleAsync(s => s.Id == session.Id, TestContext.Current.CancellationToken);

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
            await write.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var read = fixture.CreateTrainingContext();
        var loaded = await read.Sessions.AsNoTracking().SingleAsync(s => s.Id == session.Id, TestContext.Current.CancellationToken);
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
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

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
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        context.Sessions.Add(WorkoutSession.Start(athlete, SessionOrigin.AdHoc, [], Start.AddDays(2)).Value);
        await Should.NotThrowAsync(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task The_database_refuses_a_duplicate_client_set_id_within_one_exercise()
    {
        var session = AdHocSessionWithOneSet(out var clientSetId);

        await using var context = fixture.CreateTrainingContext();
        context.Sessions.Add(session);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

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
