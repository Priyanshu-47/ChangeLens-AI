using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ChangeLens.Api.Http;
using ChangeLens.Api.Middleware;
using ChangeLens.Application;
using ChangeLens.Application.Ports;
using ChangeLens.Infrastructure;
using ChangeLens.Infrastructure.Options;
using ChangeLens.Infrastructure.Persistence;
using ChangeLens.Infrastructure.Seeding;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "ChangeLens API",
        Version = "v1",
        Description = "AI-powered production change risk & incident intelligence platform — Phase 1 backend."
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter the JWT from POST /api/v1/auth/login (or /register)."
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
    });
});

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database", HealthStatus.Unhealthy);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication();

var jwtSection = builder.Configuration.GetSection(JwtOptions.SectionName);
builder.Services.Configure<JwtOptions>(jwtSection);
var jwt = jwtSection.Get<JwtOptions>() ?? new JwtOptions();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = System.Security.Claims.ClaimTypes.Name,
            RoleClaimType = System.Security.Claims.ClaimTypes.Role
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();

var app = builder.Build();

// Fail fast outside Development if JWT signing is missing or still the dev placeholder.
if (!app.Environment.IsDevelopment() &&
    (string.IsNullOrWhiteSpace(jwt.SigningKey) || jwt.SigningKey.StartsWith("dev-only", StringComparison.Ordinal)))
{
    throw new InvalidOperationException(
        "Jwt:SigningKey must be a real secret for non-development environments. Set JWT__SIGNING_KEY.");
}

// The AI service internal key is a shared secret: never ship the dev placeholder outside Development.
var aiOptions = builder.Configuration.GetSection(AiOptions.SectionName).Get<AiOptions>() ?? new AiOptions();
if (!app.Environment.IsDevelopment() &&
    (string.IsNullOrWhiteSpace(aiOptions.ApiKey) || aiOptions.ApiKey.StartsWith("change-me", StringComparison.Ordinal)))
{
    throw new InvalidOperationException(
        "Ai:ApiKey must be a real shared secret for non-development environments. Set AI__APIKEY.");
}

app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "ChangeLens API v1"));
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new
{
    status = "Healthy",
    service = "changelens-backend",
    version = ChangeLens.Api.Controllers.VersionProvider.Current,
    timestampUtc = DateTime.UtcNow
}));

app.MapControllers();

if (builder.Configuration.GetValue<bool>("Seed:Enabled"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<SeedData>().EnsureSeededAsync();
}

app.Run();

// Exposed for integration tests (WebApplicationFactory<Program>).
public partial class Program;
