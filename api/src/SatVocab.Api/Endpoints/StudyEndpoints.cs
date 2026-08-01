using Microsoft.AspNetCore.Mvc;
using SatVocab.Api.Auth;
using SatVocab.Contracts;
using SatVocab.Core;
using SatVocab.Data;

namespace SatVocab.Api.Endpoints;

public static class StudyEndpoints
{
    public static void MapStudyEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/v1/study").WithTags("Study").RequireAuthorization();

        group.MapGet("/queue", GetQueueAsync);
        group.MapPost("/reviews", SubmitReviewsAsync);
        group.MapPost("/extra-round", AddExtraRoundAsync);
    }

    private static async Task<IResult> GetQueueAsync(CurrentUser current, StudyRepository study, CancellationToken ct)
    {
        var user = await current.RequireAsync(ct);
        return Results.Ok(await study.BuildQueueAsync(user.DbPath, await current.TodayAsync(ct), ct));
    }

    private static async Task<IResult> SubmitReviewsAsync(
        [FromBody] SubmitReviewsRequest request,
        CurrentUser current,
        StudyRepository study,
        CancellationToken ct
    )
    {
        if (request.Ratings is null || request.Ratings.Count == 0)
        {
            return Results.Ok(new SubmitReviewsResponse(0));
        }
        if (request.Ratings.Any(r => !Sm2.IsValidGrade(r.Grade)))
        {
            return Results.Problem(detail: "Grades must be between 0 and 5.", statusCode: StatusCodes.Status400BadRequest);
        }

        var user = await current.RequireAsync(ct);
        var updated = await study.ApplyReviewsAsync(user.DbPath, request.Ratings, await current.TodayAsync(ct), ct);
        return Results.Ok(new SubmitReviewsResponse(updated));
    }

    private static async Task<IResult> AddExtraRoundAsync(
        CurrentUser current,
        StudyRepository study,
        CancellationToken ct
    )
    {
        var user = await current.RequireAsync(ct);
        return Results.Ok(await study.AddExtraRoundAsync(user.DbPath, await current.TodayAsync(ct), ct));
    }
}
