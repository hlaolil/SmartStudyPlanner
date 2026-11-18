using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using SmartStudyPlanner.Models;
using SmartStudyPlanner.Services;

namespace SmartStudyPlanner.Components.Shared;

/// <summary>Base for components that read the planner store and re-render when it changes.</summary>
public abstract class StoreComponentBase : ComponentBase, IDisposable
{
    private bool _subscribed;

    [Inject] protected PlannerStore Store { get; set; } = default!;
    [Inject] protected IJSRuntime JS { get; set; } = default!;
    [Inject] protected NavigationManager Nav { get; set; } = default!;

    protected AppData Data => Store.Data;
    protected DateOnly Today => Store.Today;

    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender && !_subscribed)
        {
            Store.Changed += OnStoreChanged;
            _subscribed = true;
        }
    }

    private void OnStoreChanged() => InvokeAsync(StateHasChanged);

    protected async Task<bool> ConfirmAsync(string message)
    {
        try { return await JS.InvokeAsync<bool>("ssp.confirm", message); }
        catch (JSException) { return false; }
    }

    public virtual void Dispose()
    {
        if (_subscribed) Store.Changed -= OnStoreChanged;
        GC.SuppressFinalize(this);
    }
}
