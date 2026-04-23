using Microsoft.AspNetCore.Components;

namespace Fabulis.Server.Components;

public static class NavigationManagerExtensions
{
    // Forces a full page load so cross-island navigation re-renders the target route.
    // The static Router does not react to pushState from an InteractiveServer island.
    public static void NavigateSafe(this NavigationManager nav, string uri)
        => nav.NavigateTo(uri, forceLoad: true);
}
