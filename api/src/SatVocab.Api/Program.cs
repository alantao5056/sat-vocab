using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using SatVocab.Api.Auth;
using SatVocab.Api.Endpoints;
using SatVocab.Api.Passage;
using SatVocab.Data;

var builder = WebApplication.CreateBuilder(args);

// --- Configuration ----------------------------------------------------------
// Bound from appsettings/environment, with the flat variable names the original
// deployment already sets (MANAGEMENT_DB_PATH and friends) accepted as fallbacks so
// the server's existing environment keeps working.
var satVocabOptions = builder.Configuration.GetSection("SatVocab").Get<SatVocabOptions>() ?? new SatVocabOptions();
ApplyEnvironmentFallbacks(satVocabOptions, builder.Configuration);
// Relative database paths are resolved against the content root, not the working
// directory, so `dotnet run` from anywhere and systemd both find the same files.
satVocabOptions.BasePath = builder.Environment.ContentRootPath;
satVocabOptions.Validate();

var authOptions = builder.Configuration.GetSection("Auth").Get<AuthOptions>() ?? new AuthOptions();
authOptions.SigningKey = Fallback(authOptions.SigningKey, builder.Configuration["JWT_SIGNING_KEY"]);
authOptions.WebAppUrl = Fallback(builder.Configuration["WEB_APP_URL"] ?? "", authOptions.WebAppUrl);
authOptions.Validate();

var googleOptions = builder.Configuration.GetSection("Google").Get<GoogleOptions>() ?? new GoogleOptions();
googleOptions.ClientId = Fallback(googleOptions.ClientId, builder.Configuration["GOOGLE_CLIENT_ID"]);
googleOptions.ClientSecret = Fallback(googleOptions.ClientSecret, builder.Configuration["GOOGLE_CLIENT_SECRET"]);
googleOptions.RedirectUri = Fallback(googleOptions.RedirectUri, builder.Configuration["GOOGLE_REDIRECT_URI"]);

var anthropicOptions = builder.Configuration.GetSection("Anthropic").Get<AnthropicOptions>() ?? new AnthropicOptions();
anthropicOptions.ApiKey = Fallback(anthropicOptions.ApiKey, builder.Configuration["ANTHROPIC_API_KEY"]);

builder.Services.AddSingleton(satVocabOptions);
builder.Services.AddSingleton(authOptions);
builder.Services.AddSingleton(googleOptions);
builder.Services.AddSingleton(anthropicOptions);
builder.Services.AddSingleton(TimeProvider.System);

// --- Data and domain services ----------------------------------------------
builder.Services.AddSingleton<ManagementDb>();
builder.Services.AddSingleton<VocabDbFactory>();
builder.Services.AddSingleton<StudyRepository>();
builder.Services.AddSingleton<SettingsRepository>();
builder.Services.AddSingleton<ProgressRepository>();
builder.Services.AddSingleton<PassageRepository>();
builder.Services.AddSingleton<PassageGenerator>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddHttpClient<GoogleOAuthService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUser>();

// --- HTTP pipeline services -------------------------------------------------
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

builder
    .Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(jwt =>
    {
        jwt.MapInboundClaims = false;
        jwt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = authOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = authOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(authOptions.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });
builder.Services.AddAuthorization();

// Sign-in endpoints are the ones worth throttling: they are unauthenticated and each
// attempt runs a deliberately slow scrypt hash.
builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limiter.AddPolicy(
        "auth",
        context =>
            RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = authOptions.AuthRequestsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }
            )
    );
});

// Normally unused: the web app is served same-origin behind the reverse proxy and
// native clients are not subject to CORS. Configured only for unusual deployments.
if (authOptions.AllowedOrigins.Length > 0)
{
    builder.Services.AddCors(cors =>
        cors.AddDefaultPolicy(policy =>
            policy.WithOrigins(authOptions.AllowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()
        )
    );
}

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
if (authOptions.AllowedOrigins.Length > 0)
{
    app.UseCors();
}
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/v1/health", () => Results.Ok(new { status = "ok" })).WithTags("Meta").AllowAnonymous();
app.MapAuthEndpoints();
app.MapStudyEndpoints();
app.MapSettingsEndpoints();
app.MapProgressEndpoints();
app.MapPassageEndpoints();

// The OpenAPI document is the contract the desktop client will be generated from.
app.MapOpenApi();

app.Run();

static string Fallback(string current, string? candidate) => string.IsNullOrWhiteSpace(current) ? candidate ?? "" : current;

static void ApplyEnvironmentFallbacks(SatVocabOptions options, IConfiguration configuration)
{
    options.ManagementDbPath = Fallback(options.ManagementDbPath, configuration["MANAGEMENT_DB_PATH"]);
    options.TemplateDbPath = Fallback(options.TemplateDbPath, configuration["TEMPLATE_DB_PATH"]);
    options.UserDbDir = Fallback(options.UserDbDir, configuration["USER_DB_DIR"]);
    options.DevEmail = Fallback(options.DevEmail, configuration["DEV_EMAIL"]);
}

/// <summary>Exposed so integration tests can boot the real application.</summary>
public partial class Program;
