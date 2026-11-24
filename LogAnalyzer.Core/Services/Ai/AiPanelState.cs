namespace LogAnalyzer.Services.Ai;

/// <summary>
/// The state of one AI panel on a page: the provider, the previewed request, the answer or the error.
/// Built once the user asks for an analysis, and sent only when the user presses Send on the preview.
/// </summary>
public sealed class AiPanelState
{
    public AiSettings? Settings { get; private init; }
    public AiPreview? Preview { get; private init; }
    public string? Result { get; set; }
    public string? Error { get; set; }

    /// <summary>The last answer as the provider returned it, with its token counts. Null until an answer arrives.</summary>
    public AiResponse? Response { get; private set; }
    public bool Busy { get; private set; }

    /// <summary>
    /// The state for a new analysis. Nothing is built or sent when the provider is missing or invalid; the error
    /// says why. <paramref name="build"/> runs only when there is a usable provider.
    /// </summary>
    /// <param name="settings">The active provider, or null when none is set up.</param>
    /// <param name="notConfigured">The message to show when <paramref name="settings"/> is null.</param>
    /// <param name="build">Builds the request the user will read before anything is sent.</param>
    public static AiPanelState Prepare(AiSettings? settings, string notConfigured, Func<AiPreview> build)
    {
        if (settings is null) return new AiPanelState { Error = notConfigured };
        if (settings.Validate() is { } problem) return new AiPanelState { Settings = settings, Error = problem };
        return new AiPanelState { Settings = settings, Preview = build() };
    }

    /// <summary>Sends the previewed request. Errors are kept on the state, never thrown.</summary>
    /// <param name="usage">Where the answer's token counts are added, against the provider it was sent through.</param>
    /// <param name="text">The text to send, when the user reviewed and changed it. Null sends the preview as it is.</param>
    public async Task<string?> SendAsync(AiUsageStore? usage = null, string? text = null)
    {
        if (Settings is null || Preview is null || Busy) return null;

        Busy = true;
        Error = null;
        try
        {
            // The reviewed text, when the user changed it, replaces the request text; the rest of the preview stays.
            var preview = text is null ? Preview! : Preview! with { User = text };
            var response = await AiAssistant.SendAsync(Settings, preview);
            Result = response.Text;
            Response = response;
            if (usage is not null) await usage.RecordAsync(Settings.ProviderId, response);
            return Result;
        }
        catch (AiException e)
        {
            Error = e.Message;
            return null;
        }
        finally
        {
            Busy = false;
        }
    }
}
