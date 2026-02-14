namespace CodexBar.Core.Models;

public enum ProviderId
{
    Codex,
    Claude,
    Cursor,
    Gemini,
    Antigravity,
    Droid,
    Copilot,
    Zai,
    Kiro,
    VertexAi,
    Augment,
    Amp,
    JetBrainsAi,
    ContinueDev,
    SourcegraphCody,
    Replit,
    Aider
}

public static class ProviderIdExtensions
{
    public static string ToDisplayName(this ProviderId id) => id switch
    {
        ProviderId.Codex => "Codex",
        ProviderId.Claude => "Claude Code",
        ProviderId.Cursor => "Cursor",
        ProviderId.Gemini => "Gemini",
        ProviderId.Antigravity => "Antigravity",
        ProviderId.Droid => "Droid",
        ProviderId.Copilot => "GitHub Copilot",
        ProviderId.Zai => "z.ai",
        ProviderId.Kiro => "Kiro",
        ProviderId.VertexAi => "Vertex AI",
        ProviderId.Augment => "Augment",
        ProviderId.Amp => "Amp",
        ProviderId.JetBrainsAi => "JetBrains AI",
        ProviderId.ContinueDev => "Continue",
        ProviderId.SourcegraphCody => "Cody",
        ProviderId.Replit => "Replit",
        ProviderId.Aider => "Aider",
        _ => id.ToString()
    };
}
