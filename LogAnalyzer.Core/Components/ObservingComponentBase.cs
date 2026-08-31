using Microsoft.AspNetCore.Components;

namespace LogAnalyzer.Components;

/// <summary>
/// Base for the pages that redraw in response to <see cref="Services.LogStore"/> and
/// <see cref="Services.LogWatcher"/> events.
/// <para>
/// Those events are raised from background threads — a load runs on the thread pool, the tail on
/// its own poll loop. The handlers used to be <c>async void</c>, so an exception raised while
/// dispatching to a renderer that had already gone away (a circuit closing just as a load
/// finishes) had no synchronization context to surface on, and took the process down instead of
/// the one component.
/// </para>
/// </summary>
public abstract class ObservingComponentBase : ComponentBase
{
    /// <summary>
    /// Marshals <paramref name="update"/> onto the renderer and redraws. Fire-and-forget, and safe
    /// to call from any thread.
    /// </summary>
    protected void RequestRefresh(Action? update = null) => _ = RefreshAsync(update);

    private async Task RefreshAsync(Action? update)
    {
        try
        {
            await InvokeAsync(() =>
            {
                update?.Invoke();
                StateHasChanged();
            });
        }
        catch (ObjectDisposedException)
        {
            // The renderer is gone; there is nothing left to redraw.
        }
        catch (InvalidOperationException)
        {
            // The dispatcher shut down between the event being raised and this call.
        }
    }
}
