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

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_day_label_is_rejected_and_does_not_bump_the_revision(string label)
    {
        var program = NewProgram();
        var before = program.RevisionNumber;

        var result = program.AddDay(label, null);

        result.Error.Code.ShouldBe(TrainingErrors.ProgramDayLabelRequired);
        program.RevisionNumber.ShouldBe(before);
    }

    [Fact]
    public void Renaming_with_a_blank_name_does_not_bump_the_revision()
    {
        var program = NewProgram();
        var before = program.RevisionNumber;

        var result = program.Rename("   ");

        result.Error.Code.ShouldBe(TrainingErrors.ProgramNameRequired);
        program.RevisionNumber.ShouldBe(before);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void Adding_a_prescription_with_an_out_of_range_working_set_count_does_not_bump_the_revision(int count)
    {
        var program = NewProgram();
        var day = program.AddDay("Upper", null).Value;
        var before = program.RevisionNumber;

        var result = program.AddPrescription(
            day.Id, ExerciseId.New(), null, count,
            RepRange.Create(6, 8).Value,
            RestPeriod.Create(TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(3)).Value,
            WarmupRamp.Empty, null);

        result.Error.Code.ShouldBe(TrainingErrors.PrescriptionWorkingSetsOutOfBounds);
        program.RevisionNumber.ShouldBe(before);
    }

    [Fact]
    public void Adding_a_prescription_with_an_alternative_identical_to_the_primary_does_not_bump_the_revision()
    {
        var program = NewProgram();
        var day = program.AddDay("Legs", null).Value;
        var before = program.RevisionNumber;
        var legPress = ExerciseId.New();

        var result = program.AddPrescription(
            day.Id, legPress, legPress, 3,
            RepRange.Create(10, 12).Value,
            RestPeriod.Create(TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(3)).Value,
            WarmupRamp.Empty, null);

        result.Error.Code.ShouldBe(TrainingErrors.PrescriptionAlternativeSameAsPrimary);
        program.RevisionNumber.ShouldBe(before);
    }

    [Fact]
    public void Adding_a_prescription_to_an_unknown_day_does_not_bump_the_revision()
    {
        var program = NewProgram();
        var before = program.RevisionNumber;

        var result = program.AddPrescription(
            ProgramDayId.New(), ExerciseId.New(), null, 3,
            RepRange.Create(6, 8).Value,
            RestPeriod.Create(TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(3)).Value,
            WarmupRamp.Empty, null);

        result.Error.Code.ShouldBe(TrainingErrors.ProgramDayNotFound);
        program.RevisionNumber.ShouldBe(before);
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

    [Fact]
    public void Forking_raises_no_events_and_leaves_the_sources_events_untouched()
    {
        // Deliberately not using ActiveProgram() here: it clears domain events before returning,
        // which would leave the source-side assertion below unable to fail on a regression.
        var program = NewProgram();
        var day = program.AddDay("Upper", null).Value;
        AddBench(program, day.Id);
        program.Activate();
        var eventsBeforeFork = program.DomainEvents.ToList();
        eventsBeforeFork.ShouldNotBeEmpty();

        var fork = program.Fork(UserId.New());

        fork.DomainEvents.ShouldBeEmpty();
        program.DomainEvents.ShouldBe(eventsBeforeFork);
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
