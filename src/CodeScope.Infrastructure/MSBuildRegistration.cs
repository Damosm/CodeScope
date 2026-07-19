using Microsoft.Build.Locator;

namespace CodeScope.Infrastructure;

internal static class MSBuildRegistration
{
    private static readonly object Gate = new();

    public static void EnsureRegistered()
    {
        if (MSBuildLocator.IsRegistered) return;

        lock (Gate)
        {
            if (MSBuildLocator.IsRegistered) return;

            var instance = MSBuildLocator.QueryVisualStudioInstances()
                .OrderByDescending(candidate => candidate.Version)
                .FirstOrDefault();

            if (instance is null)
                MSBuildLocator.RegisterDefaults();
            else
                MSBuildLocator.RegisterInstance(instance);
        }
    }
}
