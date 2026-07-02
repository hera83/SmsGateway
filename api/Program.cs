using api.Auth;
using api.Data;
using api.Dtos.Errors;
using api.Logging;
using api.Services;
using api.Services.Interfaces;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System.IO;

var builder = WebApplication.CreateBuilder(args);
var environmentName = builder.Environment.EnvironmentName.ToUpperInvariant();
var environmentColor = builder.Environment.IsDevelopment()
    ? "f59e0b"
    : builder.Environment.IsProduction()
        ? "22c55e"
        : "94a3b8";
var environmentBadgeUrl = $"https://img.shields.io/badge/Miljø-{Uri.EscapeDataString(environmentName)}-{environmentColor}?style=plastic";
var environmentBadge = $"![Environment]({environmentBadgeUrl})";

// Add services to the container.

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddDbContext<LogDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("LogsConnection")));

builder.Services.AddSingleton<AppDbLogEventSink>();
builder.Services.Configure<SmsServiceOptions>(builder.Configuration.GetSection("SmsService"));
builder.Services.PostConfigure<SmsServiceOptions>(options =>
{
    var serialPortEnv = Environment.GetEnvironmentVariable("SERIAL_PORT");
    if (!string.IsNullOrWhiteSpace(serialPortEnv))
    {
        options.PortName = serialPortEnv;
    }
    else if (string.IsNullOrWhiteSpace(options.PortName))
    {
        options.PortName = OperatingSystem.IsWindows() ? "COM1" : "/dev/COM1";
    }
});
builder.Services.AddSingleton<ISmsService, SmsService>();
builder.Services.AddHostedService<SmsQueueWorker>();
builder.Services.AddHostedService<SmsInboxWorker>();
builder.Services.AddHttpClient("Webhook");

builder.Host.UseSerilog((context, services, loggerConfiguration) =>
{
    loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.Sink(services.GetRequiredService<AppDbLogEventSink>());
});

builder.Services
    .AddIdentityCore<IdentityUser<Guid>>()
    .AddEntityFrameworkStores<AppDbContext>();

builder.Services
    .AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, _ => { });

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .AddAuthenticationSchemes(ApiKeyAuthenticationHandler.SchemeName)
        .RequireAuthenticatedUser()
        .Build();

    options.AddPolicy("MasterKeyOnly", policy =>
    {
        policy.AddAuthenticationSchemes(ApiKeyAuthenticationHandler.SchemeName);
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("master_key", "true");
    });
});

builder.Services.AddControllers();
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(x => x.Value?.Errors.Count > 0)
            .ToDictionary(
                x => x.Key,
                x => x.Value!.Errors
                    .Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage) ? "The input was not valid." : e.ErrorMessage)
                    .ToArray());

        var response = new ValidationErrorDto
        {
            Message = "One or more validation errors occurred.",
            TraceId = context.HttpContext.TraceIdentifier,
            Errors = errors
        };

        return new BadRequestObjectResult(response);
    };
});
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "SMS Gateway",
        Version = "v1",
        Description = environmentBadge
    });

    options.AddSecurityDefinition("ApiKey", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Name = "x-api-key",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Description = "Provide API key in x-api-key header."
    });

    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "ApiKey"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(exceptionApp =>
    {
        exceptionApp.Run(async context =>
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";

            var exceptionFeature = context.Features.Get<IExceptionHandlerFeature>();

            var response = new InternalServerErrorDto
            {
                Message = exceptionFeature?.Error.Message ?? "An unexpected error occurred.",
                TraceId = context.TraceIdentifier
            };

            await context.Response.WriteAsJsonAsync(response);
        });
    });
}
else
{
    app.UseExceptionHandler(exceptionApp =>
    {
        exceptionApp.Run(async context =>
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";

            var response = new InternalServerErrorDto
            {
                Message = "An unexpected error occurred.",
                TraceId = context.TraceIdentifier
            };

            await context.Response.WriteAsJsonAsync(response);
        });
    });
}

app.UseStatusCodePages(async statusCodeContext =>
{
    var httpContext = statusCodeContext.HttpContext;
    if (httpContext.Response.HasStarted)
    {
        return;
    }

    var statusCode = httpContext.Response.StatusCode;
    ErrorResponseDto? response = statusCode switch
    {
        StatusCodes.Status401Unauthorized => new UnauthorizedErrorDto
        {
            Message = "Authentication is required.",
            TraceId = httpContext.TraceIdentifier
        },
        StatusCodes.Status403Forbidden => new ForbiddenErrorDto
        {
            Message = "You do not have permission to access this resource.",
            TraceId = httpContext.TraceIdentifier
        },
        StatusCodes.Status404NotFound => new NotFoundErrorDto
        {
            Message = "The requested resource was not found.",
            TraceId = httpContext.TraceIdentifier
        },
        _ => null
    };

    if (response is null)
    {
        return;
    }

    httpContext.Response.ContentType = "application/json";
    await httpContext.Response.WriteAsJsonAsync(response);
});

Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "App_dbs"));

using (var scope = app.Services.CreateScope())
{
    MigrateSqliteContext(scope.ServiceProvider.GetRequiredService<AppDbContext>(), app.Environment.ContentRootPath);

    // LogEntries live in their own SQLite file so the AppDbLogEventSink's frequent, synchronous
    // writes (one per log event, including per HTTP request) never lock-contend with SmsRecord/
    // ApiKey writes from SmsController, SmsQueueWorker and SmsInboxWorker.
    MigrateSqliteContext(scope.ServiceProvider.GetRequiredService<LogDbContext>(), app.Environment.ContentRootPath);
}

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.DefaultModelsExpandDepth(-1);
    options.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.None);
});
app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    // Production runs HTTP-only behind the container's single :8080 listener
    // (no TLS endpoint configured) — see README.md. Redirecting would just
    // fail to find an HTTPS port and log noise on every request.
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

static void MigrateSqliteContext<TContext>(TContext dbContext, string contentRootPath) where TContext : DbContext
{
    if (dbContext.Database.IsSqlite())
    {
        var dbFilePath = GetSqliteDataSourcePath(dbContext.Database.GetDbConnection().ConnectionString, contentRootPath);
        if (!string.IsNullOrWhiteSpace(dbFilePath) && !File.Exists(dbFilePath))
        {
            var directory = Path.GetDirectoryName(dbFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        ResetSqliteMigrationLock(dbContext);
    }

    dbContext.Database.Migrate();

    if (dbContext.Database.IsSqlite())
    {
        // WAL lets concurrent readers/writers (SmsQueueWorker, SmsInboxWorker, web requests,
        // and the log sink) access the same SQLite file without "database is locked" errors
        // from the default rollback journal — this is what previously caused a successful
        // modem send to be recorded as a failed SmsRecord, which made the caller resend and
        // duplicate the SMS.
        dbContext.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
    }
}

static string? GetSqliteDataSourcePath(string? connectionString, string contentRootPath)
{
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        return null;
    }

    var builder = new SqliteConnectionStringBuilder(connectionString);
    if (string.IsNullOrWhiteSpace(builder.DataSource))
    {
        return null;
    }

    return Path.IsPathRooted(builder.DataSource)
        ? builder.DataSource
        : Path.Combine(contentRootPath, builder.DataSource);
}

static void ResetSqliteMigrationLock(DbContext dbContext)
{
    try
    {
        var lockTableExists = dbContext.Database
            .SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE name = '__EFMigrationsLock' AND type = 'table'")
            .AsEnumerable()
            .FirstOrDefault() > 0;

        if (lockTableExists)
        {
            dbContext.Database.ExecuteSqlRaw("DELETE FROM \"__EFMigrationsLock\" WHERE \"Id\" = 1;");
        }
    }
    catch
    {
    }
}
