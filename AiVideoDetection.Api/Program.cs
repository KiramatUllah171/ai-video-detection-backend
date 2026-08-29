using AiVideoDetection.Api.Middleware;
using AiVideoDetection.Application;
using AiVideoDetection.Application.Videos.Interfaces;
using AiVideoDetection.Infrastructure;
using AiVideoDetection.Infrastructure.Data;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
const long MaxUploadBodySizeBytes = 524_288_000;

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = MaxUploadBodySizeBytes;
});

builder.Host.UseSerilog((context, loggerConfiguration) =>
{
    loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .WriteTo.Console();
});

ValidateProductionConfiguration(builder.Configuration, builder.Environment);

builder.Services.AddControllers();
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = MaxUploadBodySizeBytes;
    options.ValueLengthLimit = 1024 * 1024;
    options.MultipartHeadersLengthLimit = 128 * 1024;
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "SachAI API",
        Version = "v1"
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter JWT token. If Swagger uses HTTP bearer scheme, paste only the token without the word Bearer."
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document, null)] = []
    });
});

builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);

var hangfireConnectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' was not found.");

builder.Services.AddHangfire(configuration =>
{
    configuration
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UsePostgreSqlStorage(
            storage => storage.UseNpgsqlConnection(hangfireConnectionString, null),
            new PostgreSqlStorageOptions
            {
                PrepareSchemaIfNecessary = true,
                QueuePollInterval = TimeSpan.FromSeconds(5),
                InvisibilityTimeout = TimeSpan.FromMinutes(30)
            });
});

builder.Services.AddHangfireServer(options =>
{
    options.Queues = ["analysis"];
    options.WorkerCount = Math.Max(1, Environment.ProcessorCount / 2);
});

var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? ["http://localhost:3000", "http://localhost:5173"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("ReactFrontend", policy =>
    {
        policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
    options.AddPolicy("EmailConfirmed", policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("email_confirmed", "true"));
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await dbContext.Database.MigrateAsync();

    var toolValidator = scope.ServiceProvider.GetRequiredService<IVideoProcessingToolValidator>();
    _ = await toolValidator.ValidateAsync();
}

app.UseMiddleware<ApiResponseLocalizationMiddleware>();
app.UseMiddleware<GlobalExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseHangfireDashboard("/hangfire");
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseCors("ReactFrontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

static void ValidateProductionConfiguration(IConfiguration configuration, IWebHostEnvironment environment)
{
    if (environment.IsDevelopment())
    {
        return;
    }

    var connectionString = configuration.GetConnectionString("DefaultConnection");
    if (string.IsNullOrWhiteSpace(connectionString)
        || connectionString.Contains("Password=123456789", StringComparison.OrdinalIgnoreCase)
        || connectionString.Contains("Host=localhost", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Production database connection string must be provided through secure configuration.");
    }

    var jwtSecret = configuration["Jwt:Secret"];
    if (string.IsNullOrWhiteSpace(jwtSecret)
        || jwtSecret.Length < 32
        || string.Equals(jwtSecret, "development-only-jwt-secret-change-before-production", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("Production JWT secret must be provided through secure configuration.");
    }

    if (string.IsNullOrWhiteSpace(configuration["AiService:ApiKey"]))
    {
        throw new InvalidOperationException("Production AI service API key must be configured.");
    }
}
