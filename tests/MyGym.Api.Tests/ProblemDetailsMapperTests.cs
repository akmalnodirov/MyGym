using Microsoft.AspNetCore.Http;
using MyGym.Api.Errors;
using MyGym.Modules.ExerciseCatalog.Domain;
using MyGym.Modules.Identity.Domain;
using MyGym.Modules.Training.Domain;
using MyGym.Shared.Infrastructure.Auth;
using Shouldly;

namespace MyGym.Api.Tests;

/// <summary>
/// ProblemDetailsMapper.StatusFor is a pure function from string to int: no host, no DI, no
/// database. It is also the piece most likely to be silently wrong — a typo'd error code falls
/// through to the 422 default rather than failing loudly, so every bucket in the switch is worth
/// pinning down explicitly rather than trusting it by inspection alone.
/// </summary>
public class ProblemDetailsMapperTests
{
    [Theory]
    [InlineData(AuthErrors.InitDataMalformed, StatusCodes.Status400BadRequest)]
    [InlineData(AuthErrors.InitDataSignatureInvalid, StatusCodes.Status401Unauthorized)]
    [InlineData(AuthErrors.InitDataExpired, StatusCodes.Status401Unauthorized)]
    [InlineData(AuthErrors.RefreshTokenInvalid, StatusCodes.Status401Unauthorized)]
    [InlineData(IdentityErrors.UserNotFound, StatusCodes.Status404NotFound)]
    [InlineData(CatalogErrors.ExerciseNotFound, StatusCodes.Status404NotFound)]
    [InlineData(TrainingErrors.ProgramNotFound, StatusCodes.Status404NotFound)]
    [InlineData(TrainingErrors.ProgramDayNotFound, StatusCodes.Status404NotFound)]
    [InlineData(TrainingErrors.SessionNotFound, StatusCodes.Status404NotFound)]
    [InlineData(TrainingErrors.SessionExerciseNotFound, StatusCodes.Status404NotFound)]
    [InlineData(TrainingErrors.SetNotFound, StatusCodes.Status404NotFound)]
    [InlineData(TrainingErrors.SessionAlreadyInProgress, StatusCodes.Status409Conflict)]
    [InlineData(TrainingErrors.SessionClosed, StatusCodes.Status409Conflict)]
    [InlineData(TrainingErrors.ProgramAlreadyActive, StatusCodes.Status409Conflict)]
    [InlineData(TrainingErrors.ProgramArchived, StatusCodes.Status409Conflict)]
    [InlineData(TrainingErrors.ProgramNotEditable, StatusCodes.Status409Conflict)]
    [InlineData(IdentityErrors.BirthYearOutOfBounds, StatusCodes.Status422UnprocessableEntity)]
    [InlineData(TrainingErrors.SetRepsOutOfBounds, StatusCodes.Status422UnprocessableEntity)]
    [InlineData("some.unmapped_code", StatusCodes.Status422UnprocessableEntity)]
    public void Maps_each_known_bucket_and_falls_back_to_422_for_anything_unmapped(
        string errorCode, int expectedStatus)
    {
        ProblemDetailsMapper.StatusFor(errorCode).ShouldBe(expectedStatus);
    }
}
