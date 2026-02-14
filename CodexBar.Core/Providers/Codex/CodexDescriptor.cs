using CodexBar.Core.Models;

namespace CodexBar.Core.Providers.Codex;

public sealed class CodexDescriptor : ProviderDescriptor
{
    public CodexDescriptor()
    {
        Id = ProviderId.Codex;
        DisplayName = "Codex";
        SessionLabel = "5-hour limit";
        SecondaryLabel = "Weekly limit";
        SupportsCredits = true;
        SupportsTokenCost = true;
        SupportsStatusPolling = true;
        SupportsLogin = true;
        IconResourceName = "codex";
        PrimaryColor = "#10A37F";
        ToggleTitle = "Show Codex usage";
        DefaultEnabled = true;
        FetchStrategies = CodexStrategies.Build();
    }
}
