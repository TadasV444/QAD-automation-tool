namespace QadAutomation.Core.Configuration;

/// <summary>
/// The validated root of the tool's configuration.
/// </summary>
/// <param name="WorkingFolder">
/// The local "QAD Tasks" folder that ticket sub-folders live under.
/// </param>
/// <param name="Clients">All configured clients.</param>
public sealed record ToolConfiguration(
    string WorkingFolder,
    IReadOnlyList<ClientProfile> Clients)
{
    /// <summary>
    /// Finds a client by its <c>id</c>, case-insensitively.
    /// </summary>
    /// <exception cref="ConfigurationException">
    /// Thrown when no such client exists, listing the configured ids.
    /// </exception>
    public ClientProfile RequireClient(string id)
    {
        var match = Clients.FirstOrDefault(
            c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            var available = string.Join(", ", Clients.Select(c => c.Id));
            throw new ConfigurationException(
                $"No client with id '{id}' is configured. Available: {available}.");
        }

        return match;
    }
}
