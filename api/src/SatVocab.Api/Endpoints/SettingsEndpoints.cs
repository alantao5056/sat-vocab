using Microsoft.AspNetCore.Mvc;
using SatVocab.Api.Auth;
using SatVocab.Contracts;
using SatVocab.Core;
using SatVocab.Data;

namespace SatVocab.Api.Endpoints;

public static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/v1/settings").WithTags("Settings").RequireAuthorization();

        group.MapGet("/", GetAsync);
        group.MapPut("/", UpdateAsync);
    }

    private static async Task<IResult> GetAsync(CurrentUser current, SettingsRepository settings, CancellationToken ct)
    {
        var user = await current.RequireAsync(ct);
        var (newWordsPerDay, wordsPerRound) = await settings.GetAsync(user.DbPath, ct);

        return Results.Ok(
            new SettingsResponse(
                newWordsPerDay,
                wordsPerRound,
                user.Timezone ?? TimeZoneInfo.Local.Id,
                SatVocabDefaults.IntensityPresets,
                SatVocabDefaults.WordsPerRoundOptions,
                Sm2.Grades
            )
        );
    }

    private static async Task<IResult> UpdateAsync(
        [FromBody] UpdateSettingsRequest request,
        CurrentUser current,
        SettingsRepository settings,
        ManagementDb db,
        CancellationToken ct
    )
    {
        // Only values from the fixed option sets are accepted — these caps shape the
        // whole schedule, so free-form input is deliberately not allowed.
        if (request.NewWordsPerDay is { } perDay && !SatVocabDefaults.IntensityPresets.Any(p => p.Value == perDay))
        {
            return Results.Problem(
                detail: "New words per day must be one of the offered intensity presets.",
                statusCode: StatusCodes.Status400BadRequest
            );
        }
        if (request.WordsPerRound is { } perRound && !SatVocabDefaults.WordsPerRoundOptions.Contains(perRound))
        {
            return Results.Problem(
                detail: "Words per round must be one of the offered options.",
                statusCode: StatusCodes.Status400BadRequest
            );
        }
        if (request.Timezone is { } timezone && !UserClock.IsKnownZone(timezone))
        {
            return Results.Problem(
                detail: $"Unknown time zone '{timezone}'.",
                statusCode: StatusCodes.Status400BadRequest
            );
        }

        var user = await current.RequireAsync(ct);
        await settings.UpdateAsync(user.DbPath, request.NewWordsPerDay, request.WordsPerRound, ct);
        if (request.Timezone is { } zone)
        {
            await db.SetTimezoneAsync(user.Id, zone, ct);
        }

        var (updatedPerDay, updatedPerRound) = await settings.GetAsync(user.DbPath, ct);
        return Results.Ok(
            new SettingsResponse(
                updatedPerDay,
                updatedPerRound,
                request.Timezone ?? user.Timezone ?? TimeZoneInfo.Local.Id,
                SatVocabDefaults.IntensityPresets,
                SatVocabDefaults.WordsPerRoundOptions,
                Sm2.Grades
            )
        );
    }
}
