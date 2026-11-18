using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using SmartStudyPlanner.Components;
using SmartStudyPlanner.Services;

// Runs entirely in the browser (Blazor WebAssembly): the published output is static files
// that any static host (Vercel, GitHub Pages, Azure, Netlify…) can serve.
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped<PlannerStore>();

await builder.Build().RunAsync();
