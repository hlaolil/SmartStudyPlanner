using SmartStudyPlanner.Components;
using SmartStudyPlanner.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    // Saved data is sent from the browser to the server on load; allow up to 8 MB.
    .AddHubOptions(o => o.MaximumReceiveMessageSize = 8 * 1024 * 1024);

builder.Services.AddScoped<PlannerStore>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
