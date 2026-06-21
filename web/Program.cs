using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using web.Data;
using web.Services.SmsService;
using web.Services.SmsService.Interfaces;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services
    .AddIdentity<IdentityUser<Guid>, IdentityRole<Guid>>(options =>
    {
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredLength = 8;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/Login";
});

builder.Services.AddControllersWithViews();

builder.Services.AddHttpClient<ISmsService, SmsService>((serviceProvider, client) =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var baseUrl = configuration["SmsService:Url"];
    if (!string.IsNullOrWhiteSpace(baseUrl))
    {
        client.BaseAddress = new Uri(baseUrl);
    }
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStatusCodePagesWithReExecute("/Error/{0}");

if (app.Environment.IsDevelopment())
{
    // Production runs HTTP-only behind the container's single :8080 listener
    // (no TLS endpoint configured) — see README.md. UseHsts()/UseHttpsRedirection()
    // would just be no-ops there and log noise on every request.
    app.UseHttpsRedirection();
}

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "App_dbs"));

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    if (dbContext.Database.IsSqlite())
    {
        var dbFilePath = GetSqliteDataSourcePath(dbContext.Database.GetDbConnection().ConnectionString, app.Environment.ContentRootPath);
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
}

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();

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

static void ResetSqliteMigrationLock(AppDbContext dbContext)
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

