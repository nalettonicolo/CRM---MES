using System.Globalization;
using CrmMes.Web;
using CrmMes.Web.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Dates as 30/09/2026 and amounts as 1.234,56 whatever the browser's language.
CultureInfo.DefaultThreadCurrentCulture = Labels.Italian;
CultureInfo.DefaultThreadCurrentUICulture = Labels.Italian;

// The app is served by the API under /app/: API calls go to the site root, same origin, no CORS.
var siteRoot = new Uri(new Uri(builder.HostEnvironment.BaseAddress), "/");
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = siteRoot });
builder.Services.AddScoped<Session>();
builder.Services.AddScoped<Api>();

await builder.Build().RunAsync();
