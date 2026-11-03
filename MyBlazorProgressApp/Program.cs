using MyBlazorProgressApp.Data;
using MyBlazorProgressApp.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
builder.Configuration
    .SetBasePath(AppContext.BaseDirectory) // Ensure it finds the file
    .AddJsonFile(configPath, optional: false, reloadOnChange: true)
    .AddJsonFile(Path.Combine(AppContext.BaseDirectory, $"appsettings.{builder.Environment.EnvironmentName}.json"), optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddSingleton<WeatherForecastService>();
builder.Services.AddOptions<MyshareOptions>()
    .Bind(builder.Configuration.GetSection(MyshareOptions.SectionName))
    .Validate(o => o.IsValid, "The Myshare section is incomplete. See appsettings.json.")
    .ValidateOnStart();
builder.Services.AddHttpClient<MyshareService>();
var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
