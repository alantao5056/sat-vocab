using SatVocab.Api.Auth;
using SatVocab.Contracts;
using SatVocab.Data;

namespace SatVocab.Api.Endpoints;

public static class ProgressEndpoints
{
    private const int MaxPageSize = 500;

    public static void MapProgressEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/v1/progress").WithTags("Progress").RequireAuthorization();

        group.MapGet("/", GetSummaryAsync);
        group.MapGet("/words", GetWordsAsync);
    }

    private static async Task<IResult> GetSummaryAsync(
        CurrentUser current,
        ProgressRepository progress,
        CancellationToken ct
    )
    {
        var user = await current.RequireAsync(ct);
        return Results.Ok(await progress.GetSummaryAsync(user.DbPath, await current.TodayAsync(ct), ct));
    }

    private static async Task<IResult> GetWordsAsync(
        string bucket,
        CurrentUser current,
        ProgressRepository progress,
        CancellationToken ct,
        int offset = 0,
        int limit = 100
    )
    {
        if (!ProgressBuckets.Listable.Contains(bucket))
        {
            return Results.Problem(
                detail: $"Unknown bucket '{bucket}'. Expected one of: {string.Join(", ", ProgressBuckets.Listable)}.",
                statusCode: StatusCodes.Status400BadRequest
            );
        }

        var user = await current.RequireAsync(ct);
        return Results.Ok(
            await progress.GetWordsAsync(
                user.DbPath,
                bucket,
                await current.TodayAsync(ct),
                Math.Max(0, offset),
                Math.Clamp(limit, 1, MaxPageSize),
                ct
            )
        );
    }
}
