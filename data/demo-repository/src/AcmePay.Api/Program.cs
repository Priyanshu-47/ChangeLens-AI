using AcmePay.Api.Middleware;
using AcmePay.Application.Auth;
using AcmePay.Application.Payments;
using AcmePay.External;
using AcmePay.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Configuration is environment-driven. In staging, some keys come from a vault;
// never log the values (see ApiKeyAuthMiddleware and TokenService).
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddAcmePayExternal(builder.Configuration);
builder.Services.AddAcmePayInfrastructure(builder.Configuration);

// Application services.
builder.Services.AddSingleton<ApiKeyValidator>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddScoped<ProcessPaymentHandler>();
builder.Services.AddScoped<RefundPaymentHandler>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseMiddleware<ApiKeyAuthMiddleware>();
app.MapControllers();

app.Run();

// Exposed for integration tests.
public partial class Program;
