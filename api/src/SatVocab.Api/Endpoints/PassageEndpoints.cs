using SatVocab.Api.Auth;
using SatVocab.Api.Passage;
using SatVocab.Contracts;
using SatVocab.Core;
using SatVocab.Data;

namespace SatVocab.Api.Endpoints;

public static class PassageEndpoints
{
    public static void MapPassageEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/v1/passage").WithTags("Passage").RequireAuthorization();

        group.MapGet("/", GetPassageAsync);
        group.MapPost("/generate", GeneratePassageAsync);
    }

    private static async Task<IResult> GetPassageAsync(
        CurrentUser current,
        StudyRepository study,
        PassageRepository passages,
        CancellationToken ct
    )
    {
        var user = await current.RequireAsync(ct);
        var today = await current.TodayAsync(ct);

        var queue = await study.BuildQueueAsync(user.DbPath, today, ct);
        var segments = await passages.GetCachedAsync(user.DbPath, WordIds(queue), ct);
        var error = await passages.GetErrorAsync(user.DbPath, ct);

        return Results.Ok(await BuildResponseAsync(current, passages, user.DbPath, today, queue, segments, error, ct));
    }

    private static async Task<IResult> GeneratePassageAsync(
        CurrentUser current,
        StudyRepository study,
        PassageRepository passages,
        PassageGenerator generator,
        AnthropicOptions anthropic,
        ILoggerFactory loggerFactory,
        CancellationToken ct
    )
    {
        if (!anthropic.IsConfigured)
        {
            return Results.Problem(
                detail: "Passage generation is not configured on this server.",
                statusCode: StatusCodes.Status503ServiceUnavailable
            );
        }

        var user = await current.RequireAsync(ct);
        var today = await current.TodayAsync(ct);

        var queue = await study.BuildQueueAsync(user.DbPath, today, ct);
        if (queue.Words.Count == 0)
        {
            return Results.Problem(
                detail: "There are no words to build a passage from right now.",
                statusCode: StatusCodes.Status409Conflict
            );
        }

        var isDev = await current.IsDevAsync(ct);
        var used = isDev ? 0 : await passages.GetGenerationsTodayAsync(user.DbPath, today, ct);
        if (!isDev && used >= SatVocabDefaults.PassageDailyLimit)
        {
            return Results.Problem(
                detail: $"You've used all {SatVocabDefaults.PassageDailyLimit} passage generations for today.",
                statusCode: StatusCodes.Status429TooManyRequests
            );
        }

        // Counted before the call, not after: an attempt costs an API call whether or not
        // it produces a passage, so a failing key cannot be retried around the quota.
        if (!isDev)
        {
            await passages.RecordGenerationAsync(user.DbPath, today, ct);
        }

        IReadOnlyList<PassageSegmentResponse>? segments = null;
        string? error = null;
        try
        {
            segments = await generator.GenerateAsync(queue.Words, ct);
            await passages.SaveAsync(user.DbPath, WordIds(queue), segments, ct);
        }
        catch (PassageException e)
        {
            // A failed generation is passage state, not a transport failure: it is stored
            // and returned so every client explains it the same way and offers a retry.
            loggerFactory.CreateLogger(nameof(PassageEndpoints)).LogError(e, "Passage generation failed.");
            error = e.Message;
            await passages.SetErrorAsync(user.DbPath, error, ct);
        }

        return Results.Ok(await BuildResponseAsync(current, passages, user.DbPath, today, queue, segments, error, ct));
    }

    private static async Task<PassageResponse> BuildResponseAsync(
        CurrentUser current,
        PassageRepository passages,
        string dbPath,
        DateOnly today,
        StudyQueueResponse queue,
        IReadOnlyList<PassageSegmentResponse>? segments,
        string? error,
        CancellationToken ct
    )
    {
        if (await current.IsDevAsync(ct))
        {
            return new PassageResponse(queue, segments, error, 0, null);
        }

        var used = await passages.GetGenerationsTodayAsync(dbPath, today, ct);
        return new PassageResponse(queue, segments, error, used, SatVocabDefaults.PassageDailyLimit);
    }

    private static List<long> WordIds(StudyQueueResponse queue) => queue.Words.Select(w => w.Id).ToList();
}
