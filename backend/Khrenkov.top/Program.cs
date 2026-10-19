using Microsoft.AspNetCore.Authentication.Cookies;
using Khrenkov.top.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Khrenkov.top.Data;
using Microsoft.EntityFrameworkCore;

DatabaseCommand? databaseCommand;
try
{
    databaseCommand = DatabaseCommand.Parse(args);
}
catch (ArgumentException exception)
{
    Console.Error.WriteLine(exception.Message);
    Environment.ExitCode = 2;
    return;
}
var builder = WebApplication.CreateBuilder(databaseCommand?.ConfigurationArguments ?? args);

// Add services to the container.
builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Khrenkov.top API", Version = "v1" });
});

builder.Services.AddSingleton<StoragePaths>();
builder.Services.AddSingleton<FileStorage>();
builder.Services.AddDbContext<ApplicationDbContext>((services, options) =>
    options.UseSqlite(services.GetRequiredService<StoragePaths>().ConnectionString));
builder.Services.AddScoped<DatabaseMigrator>();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 512 * 1024 * 1024; // 512 MB
});
builder.WebHost.ConfigureKestrel(k =>
{
    k.Limits.MaxRequestBodySize = 512 * 1024 * 1024; // 512 MB
});

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });

await using var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var migrator = scope.ServiceProvider.GetRequiredService<DatabaseMigrator>();
    var paths = scope.ServiceProvider.GetRequiredService<StoragePaths>();
    if (databaseCommand is not null)
    {
        try
        {
            switch (databaseCommand.Action)
            {
                case "migrate": await migrator.ApplyAsync(paths.UploadsPath); break;
                case "rollback": await migrator.RollbackAsync(databaseCommand.Target!); break;
                case "status": Console.WriteLine(await migrator.StatusAsync()); break;
            }
        }
        catch (Exception exception)
        {
            app.Logger.LogError(exception, "Database command failed");
            Environment.ExitCode = 1;
        }
        return;
    }
    await migrator.RequireCurrentAsync();
    Directory.CreateDirectory(paths.UploadsPath);
    await FileCatalogSynchronization.ApplyAsync(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(), paths.UploadsPath);
}

app.UseForwardedHeaders();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Khrenkov.top API v1");
    c.ConfigObject.AdditionalItems["withCredentials"] = true;
});

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

await app.RunAsync();
