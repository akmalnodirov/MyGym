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
        List<PrescriptionSnapshot> list = snapshots.Length == 0 ? [Snapshot()] : snapshots.ToList();
        var session = WorkoutSession.Start(Athlete, origin, list, Start).Value;
        session.ClearDomainEvents();
        return session;
    }

    private static readonly ExerciseId BaseExerciseId = ExerciseId.New();
    private static readonly ExerciseId OtherExerciseId = ExerciseId.New();
    private static readonly RepRange BaseReps = RepRange.Create(6, 8).Value;
    private static readonly RepRange OtherReps = RepRange.Create(10, 12).Value;
    private static readonly RestPeriod BaseRest = RestPeriod.Create(TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(3)).Value;
    private static readonly RestPeriod OtherRest = RestPeriod.Create(TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(2)).Value;
    private static readonly WarmupRamp BaseWarmup = WarmupRamp.Empty;
    private static readonly WarmupRamp OtherWarmup =
        WarmupRamp.Create([WarmupStep.Create(0.5m, 5, isOptional: false).Value]).Value;

    private static PrescriptionSnapshot BuildSnapshot(
        ExerciseId? exerciseId = null,
        ExerciseId? alternativeExerciseId = null,
        int workingSetCount = 3,
        RepRange? targetReps = null,
        RestPeriod? rest = null,
        WarmupRamp? warmup = null,
        string? notes = "baseline") =>
        PrescriptionSnapshot.Create(
            exerciseId ?? BaseExerciseId,
            alternativeExerciseId,
            workingSetCount,
            targetReps ?? BaseReps,
            rest ?? BaseRest,
            warmup ?? BaseWarmup,
            notes).Value;

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
    public void Independently_constructed_snapshots_with_identical_fields_are_equal()
    {
        BuildSnapshot().ShouldBe(BuildSnapshot());
    }

    [Fact]
    public void Snapshots_differing_only_in_exercise_id_are_not_equal()
    {
        BuildSnapshot(exerciseId: BaseExerciseId).ShouldNotBe(BuildSnapshot(exerciseId: OtherExerciseId));
    }

    [Fact]
    public void Snapshots_differing_only_in_alternative_exercise_id_are_not_equal()
    {
        BuildSnapshot(alternativeExerciseId: null).ShouldNotBe(BuildSnapshot(alternativeExerciseId: OtherExerciseId));
    }

    [Fact]
    public void Snapshots_differing_only_in_working_set_count_are_not_equal()
    {
        BuildSnapshot(workingSetCount: 3).ShouldNotBe(BuildSnapshot(workingSetCount: 4));
    }

    [Fact]
    public void Snapshots_differing_only_in_target_reps_are_not_equal()
    {
        BuildSnapshot(targetReps: BaseReps).ShouldNotBe(BuildSnapshot(targetReps: OtherReps));
    }

    [Fact]
    public void Snapshots_differing_only_in_rest_are_not_equal()
    {
        BuildSnapshot(rest: BaseRest).ShouldNotBe(BuildSnapshot(rest: OtherRest));
    }

    [Fact]
    public void Snapshots_differing_only_in_warmup_are_not_equal()
    {
        BuildSnapshot(warmup: BaseWarmup).ShouldNotBe(BuildSnapshot(warmup: OtherWarmup));
    }

    [Fact]
    public void Snapshots_differing_only_in_notes_are_not_equal()
    {
        BuildSnapshot(notes: "baseline").ShouldNotBe(BuildSnapshot(notes: "different"));
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
    public void The_same_client_set_id_can_be_used_on_two_different_exercises()
    {
        var session = ProgramSession(Snapshot(), Snapshot());
        var first = session.Exercises[0];
        var second = session.Exercises[1];
        var clientSetId = Guid.NewGuid();

        var setOnFirst = session.LogSet(
            first.Id, clientSetId, SetKind.Working, Weight.Kg(27.5m), 8, null, Start).Value;
        var setOnSecond = session.LogSet(
            second.Id, clientSetId, SetKind.Working, Weight.Kg(30m), 6, null, Start).Value;

        setOnFirst.Id.ShouldNotBe(setOnSecond.Id);
        session.Exercises[0].Sets.ShouldHaveSingleItem();
        session.Exercises[1].Sets.ShouldHaveSingleItem();
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
    public void WorkingSetCount_sums_working_sets_across_every_exercise()
    {
        var session = ProgramSession(Snapshot(), Snapshot(), Snapshot());
        var exercises = session.Exercises;

        session.LogSet(exercises[0].Id, Guid.NewGuid(), SetKind.Warmup, Weight.Kg(20m), 10, null, Start);
        session.LogSet(exercises[0].Id, Guid.NewGuid(), SetKind.Working, Weight.Kg(27.5m), 8, null, Start);
        session.LogSet(exercises[0].Id, Guid.NewGuid(), SetKind.Working, Weight.Kg(27.5m), 7, null, Start);
        session.LogSet(exercises[1].Id, Guid.NewGuid(), SetKind.Working, Weight.Kg(50m), 5, null, Start);
        session.LogSet(exercises[2].Id, Guid.NewGuid(), SetKind.Warmup, Weight.Kg(15m), 10, null, Start);

        session.WorkingSetCount.ShouldBe(3);

        session.Complete(Start.AddHours(1)).IsSuccess.ShouldBeTrue();

        session.DomainEvents.OfType<WorkoutSessionCompleted>().ShouldHaveSingleItem().WorkingSetCount.ShouldBe(3);
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
    public void Retrying_a_previously_logged_set_against_a_closed_session_is_rejected()
    {
        var session = ProgramSession();
        var exercise = session.Exercises[0];
        var clientSetId = Guid.NewGuid();
        session.LogSet(exercise.Id, clientSetId, SetKind.Working, Weight.Kg(27.5m), 8, null, Start);
        session.Complete(Start.AddHours(1));

        var retry = session.LogSet(
            exercise.Id, clientSetId, SetKind.Working, Weight.Kg(27.5m), 8, null, Start.AddMinutes(90));

        retry.Error.Code.ShouldBe(TrainingErrors.SessionClosed);
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
