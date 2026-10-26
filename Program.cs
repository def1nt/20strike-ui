using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Authorization;
using _20strike_ui.Data;
using _20strike_ui.Data.ComputersV2;
using _20strike_ui.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddSingleton<SimulatedDataProviderService>();
builder.Services.AddScoped<WebsiteAuthenticator>();
// The same scoped instance serves both the page-facing service and the Blazor auth state
// provider, so login/logout notifications always reach AuthorizeView.
builder.Services.AddScoped<IAuthenticationService>(s => s.GetRequiredService<WebsiteAuthenticator>());
builder.Services.AddScoped<AuthenticationStateProvider>(s => s.GetRequiredService<WebsiteAuthenticator>());
builder.Services.AddHttpClient();
var computersV2Options = builder.Configuration
    .GetSection(ComputersV2Options.SectionName)
    .Get<ComputersV2Options>() ?? new ComputersV2Options();
builder.Services.AddScoped<IComputersV2Client>(s =>
{
    var http = s.GetRequiredService<HttpClient>();
    return new ComputersV2Client(http, computersV2Options.BaseUrl);
});
builder.Services.AddScoped<ComputersV2Cache>();
builder.Services.AddScoped<ComputerPingMonitor>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    // app.UseHsts();
}
// app.Urls.Add("https://*:7156");
app.Urls.Add("http://*:5247");
// app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
