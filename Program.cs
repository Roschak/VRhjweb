using HajjVR.Api;
using HajjVR.Components;
using HajjVR.Data;
using HajjVR.Services;
using HajjVR.Services.Ai;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ---------- Blazor ----------
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// ---------- Database: multi-provider (SQLite default, SQLServer, MySQL, PostgreSQL) ----------
var dbProvider = builder.Configuration["Database:Provider"] ?? "SQLite";
builder.Services.AddDbContextFactory<AppDbContext>(options =>
{
    var cs = builder.Configuration.GetConnectionString(dbProvider) ?? "Data Source=hajjvr.db";
    switch (dbProvider.ToLowerInvariant())
    {
        case "sqlserver":
            options.UseSqlServer(cs);
            break;
        case "mysql":
            options.UseMySql(cs, ServerVersion.AutoDetect(cs));
            break;
        case "postgresql" or "postgres":
            options.UseNpgsql(cs);
            break;
        default:
            options.UseSqlite(cs);
            break;
    }
});

// ---------- Autentikasi cookie + otorisasi role ----------
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/login";
        o.AccessDeniedPath = "/login";
        o.Cookie.Name = "HajjVR.Auth";
        o.ExpireTimeSpan = TimeSpan.FromDays(7);
        o.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();

// ---------- Services aplikasi ----------
builder.Services.AddSingleton<SettingsService>();
builder.Services.AddScoped<LocalizationService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddSingleton<IStorageService, StorageRouter>();
builder.Services.AddSingleton<AnalyticsService>();
builder.Services.AddSingleton<GamificationService>();
builder.Services.AddSingleton<ExportService>();
builder.Services.AddSingleton<SemanticSearchService>();
builder.Services.AddSingleton<NavigationManagerAccessor>();
builder.Services.AddSingleton<KernelFactory>();
builder.Services.AddSingleton<TimePlugin>();
builder.Services.AddSingleton<MathPlugin>();
builder.Services.AddSingleton<WebPlugin>();
builder.Services.AddSingleton<DataPlugin>();
builder.Services.AddSingleton<ChatAiService>();
builder.Services.AddHttpClient("ai", c =>
{
    c.Timeout = TimeSpan.FromSeconds(60);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("HajjVR/1.0");
});
builder.Services.AddHostedService<CrowdSimulatorService>();

// ---------- REST API + Swagger ----------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new Microsoft.OpenApi.OpenApiInfo
    {
        Title = "HajjVR API",
        Version = "v1",
        Description = "REST API HajjVR untuk integrasi dengan aplikasi lain. Gunakan header X-Api-Key."
    });
});

// ---------- Performa ----------
builder.Services.AddResponseCompression(o => o.EnableForHttps = true);

var app = builder.Build();

// ---------- Inisialisasi database + seed ----------
using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    await using var db = await factory.CreateDbContextAsync();
    await db.Database.EnsureCreatedAsync();
    await DbSeeder.SeedAsync(db);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseResponseCompression();
app.UseStaticFiles(); // file upload di wwwroot/uploads (storage FileSystem)
app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();

// ---------- Swagger UI ----------
app.UseSwagger();
app.UseSwaggerUI(o =>
{
    o.SwaggerEndpoint("/swagger/v1/swagger.json", "HajjVR API v1");
    o.DocumentTitle = "HajjVR API";
});

// ---------- Endpoint auth (form post → cookie sign-in) ----------
app.MapAuthEndpoints();

// ---------- REST API data ----------
app.MapHajjApi();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
