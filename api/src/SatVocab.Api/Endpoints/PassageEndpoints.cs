using SatVocab.Api.Auth;
using SatVocab.Api.Passage;
using SatVocab.Contracts;
using SatVocab.Core;
using SatVocab.Data;

namespace SatVocab.Api.Endpoints;

public static class PassageEndpoints
{
    private const int MaxPageSize = 50;

    public static void MapPassageEndpoints(this IEndpointRouteBuilder routes)
    {
        // Passage mode for the current round.
        var group = routes.MapGroup("/v1/passage").WithTags("Passage").RequireAuthorization();

        group.MapGet("/", GetPassageAsync);
        group.MapPost("/generate", GeneratePassageAsync);

        // The saved history: passages the user has already generated, readable and gradable
        // long after the round that produced them has gone.
        var saved = routes.MapGroup("/v1/passages").WithTags("Passages").RequireAuthorization();

        saved.MapGet("/", ListPassagesAsync);
        saved.MapGet("/{id:long}", GetSavedPassageAsync);
        saved.MapPost("/{id:long}/reviews", SubmitPassageReviewsAsync);
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
        var cached = await passages.GetCachedAsync(user.DbPath, WordIds(queue), ct);
        var error = await passages.GetErrorAsync(user.DbPath, ct);

        return Results.Ok(
            await BuildResponseAsync(
                current,
                passages,
                user.DbPath,
                today,
                queue,
                cached?.Segments,
                cached?.Title,
                error,
                ct
            )
        );
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
        string? title = null;
        string? error = null;
        try
        {
            var generated = await generator.GenerateAsync(queue.Words, ct);
            segments = generated.Segments;
            title = generated.Title;

            var wordIds = WordIds(queue);
            await passages.SaveAsync(user.DbPath, wordIds, segments, title, ct);
            // Cached for the current round *and* kept: the cache is overwritten by the next
            // generation, while the history row is what the Passages tab lists.
            await passages.AddAsync(user.DbPath, title, today, wordIds, segments, ct);
        }
        catch (PassageException e)
        {
            // A failed generation is passage state, not a transport failure: it is stored
            // and returned so every client explains it the same way and offers a retry.
            loggerFactory.CreateLogger(nameof(PassageEndpoints)).LogError(e, "Passage generation failed.");
            error = e.Message;
            await passages.SetErrorAsync(user.DbPath, error, ct);
        }

        return Results.Ok(
            await BuildResponseAsync(current, passages, user.DbPath, today, queue, segments, title, error, ct)
        );
    }

    private static async Task<IResult> ListPassagesAsync(
        CurrentUser current,
        PassageRepository passages,
        CancellationToken ct,
        int offset = 0,
        int limit = 10
    )
    {
        var user = await current.RequireAsync(ct);
        return Results.Ok(
            await passages.ListAsync(user.DbPath, Math.Max(0, offset), Math.Clamp(limit, 1, MaxPageSize), ct)
        );
    }

    private static async Task<IResult> GetSavedPassageAsync(
        long id,
        CurrentUser current,
        PassageRepository passages,
        StudyRepository study,
        CancellationToken ct
    )
    {
        var user = await current.RequireAsync(ct);

        var saved = await passages.GetByIdAsync(user.DbPath, id, ct);
        if (saved is null)
        {
            return NotFound(id);
        }

        var words = await study.GetWordsByIdsAsync(user.DbPath, saved.WordIds, ct);
        return Results.Ok(
            new SavedPassageResponse(saved.Id, saved.Title, saved.CreatedDate, saved.Segments, words)
        );
    }

    private static async Task<IResult> SubmitPassageReviewsAsync(
        long id,
        SubmitReviewsRequest request,
        CurrentUser current,
        PassageRepository passages,
        StudyRepository study,
        CancellationToken ct
    )
    {
        var user = await current.RequireAsync(ct);

        var saved = await passages.GetByIdAsync(user.DbPath, id, ct);
        if (saved is null)
        {
            return NotFound(id);
        }

        // This route grades one passage, so it only accepts that passage's words. Anything
        // else belongs on /v1/study/reviews.
        var wordIds = saved.WordIds.ToHashSet();
        var ratings = request.Ratings.Where(r => wordIds.Contains(r.WordId)).ToList();

        var updated = await study.ApplyReviewsAsync(
            user.DbPath,
            ratings,
            await current.TodayAsync(ct),
            ct,
            // Grading history must not cost the user the passage they have open on the
            // Study tab.
            clearPassageCache: false
        );
        return Results.Ok(new SubmitReviewsResponse(updated));
    }

    private static IResult NotFound(long id) =>
        Results.Problem(detail: $"Passage {id} was not found.", statusCode: StatusCodes.Status404NotFound);

    private static async Task<PassageResponse> BuildResponseAsync(
        CurrentUser current,
        PassageRepository passages,
        string dbPath,
        DateOnly today,
        StudyQueueResponse queue,
        IReadOnlyList<PassageSegmentResponse>? segments,
        string? title,
        string? error,
        CancellationToken ct
    )
    {
        if (await current.IsDevAsync(ct))
        {
            return new PassageResponse(queue, segments, title, error, 0, null);
        }

        var used = await passages.GetGenerationsTodayAsync(dbPath, today, ct);
        return new PassageResponse(queue, segments, title, error, used, SatVocabDefaults.PassageDailyLimit);
    }

    private static List<long> WordIds(StudyQueueResponse queue) => queue.Words.Select(w => w.Id).ToList();
}
