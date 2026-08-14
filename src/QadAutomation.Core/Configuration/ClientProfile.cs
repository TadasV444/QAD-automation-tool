namespace QadAutomation.Core.Configuration;

/// <summary>
/// A single client and all of its environments, fully resolved.
/// </summary>
/// <remarks>
/// This is the "client profile" the readme calls for: every difference between
/// clients - VPN type, connection details, SRC/QRF remote paths, compile
/// strategy - is a value in here rather than a branch in code. Adding the fifth
/// client should mean editing configuration, not editing this class.
/// </remarks>
public sealed record ClientProfile(
    string Id,
    string DisplayName,
    VpnSettings Vpn,
    IReadOnlyList<QadEnvironment> Environments)
{
    /// <summary>
    /// Finds an environment by name, case-insensitively (<c>prod</c> == <c>PROD</c>).
    /// </summary>
    /// <exception cref="ConfigurationException">
    /// Thrown when no such environment exists. The message lists what <i>is</i>
    /// available, because the most likely cause is a typo at the command line.
    /// </exception>
    public QadEnvironment RequireEnvironment(string name)
    {
        var match = Environments.FirstOrDefault(
            e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            var available = string.Join(", ", Environments.Select(e => e.Name));
            throw new ConfigurationException(
                $"Client '{Id}' has no environment '{name}'. Available: {available}.");
        }

        return match;
    }
}
