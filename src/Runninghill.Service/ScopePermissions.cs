using System.Security.Claims;

namespace Runninghill.Service;

/// <summary>Checks whether a user's token gives permission to perform an operation.</summary>
public static class ScopePermissions
{
    /// <summary>
    /// Checks every scope claim for an exact, case-sensitive permission, scanning space-separated
    /// values without allocating split strings.
    /// </summary>
    public static bool HasScope(ClaimsPrincipal user, string requiredScope)
    {
        foreach (var claim in user.FindAll("scope"))
        {
            // A scope claim is a space-separated list, like "status.read words.write".
            // These slices look at the original text without creating an array or new strings
            // for every request. Compare whole words: "status.read.extra" is not "status.read".
            foreach (var part in claim.Value.AsSpan().Split(' '))
            {
                if (claim.Value.AsSpan(part).SequenceEqual(requiredScope))
                    return true;
            }
        }

        return false;
    }
}
