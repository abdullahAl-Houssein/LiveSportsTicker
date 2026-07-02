using LiveSportsTicker.Services;

var builder = WebApplication.CreateBuilder(args);

// MVC (controllers + Razor views)
builder.Services.AddControllersWithViews();

// Singleton service: holds current match state + manages SSE subscribers
builder.Services.AddSingleton<MatchBroadcastService>();

// Background service: simulates a live match and pushes events forever
builder.Services.AddHostedService<MatchSimulatorService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
