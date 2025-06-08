using Microsoft.EntityFrameworkCore;
using Forms_app.Data;
using DotNetEnv;

var builder = WebApplication.CreateBuilder(args);

// Configure connection string from appsettings.json
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<FormManagementContext>(options =>
{
    options.UseSqlServer(connectionString, sqlServerOptions =>
    {
        sqlServerOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(30),
            errorNumbersToAdd: null);
        
        // Set command timeout to 60 seconds
        sqlServerOptions.CommandTimeout(60);
        
        // Configure connection pooling and query splitting
        sqlServerOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
        sqlServerOptions.MigrationsHistoryTable("__EFMigrationsHistory");
        
        // Enable connection resiliency
        sqlServerOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(30),
            errorNumbersToAdd: null);
    });
});

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddTransient<EmailHelper>();
builder.Services.Configure<SmtpSettings>(builder.Configuration.GetSection("SmtpSettings"));
builder.Host.UseWindowsService();

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.MaxRequestBodySize = 2147483648; // 2GB in bytes
    serverOptions.ListenAnyIP(5350);
});

DotNetEnv.Env.Load();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

// Configure routing for MVC controllers
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Forms}/{action=Index}/{id?}");

app.Run();
