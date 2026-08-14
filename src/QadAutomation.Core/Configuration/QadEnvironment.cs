namespace QadAutomation.Core.Configuration;

/// <summary>
/// One fully-resolved deployment target, e.g. <c>pilot</c> / <c>PROD</c>.
/// </summary>
/// <remarks>
/// Every property here is already merged with the client's defaults and
/// validated, so consumers never have to ask "was this overridden?" or handle a
/// missing host. All the ambiguity lives in the raw config layer and is burned
/// off by <see cref="ConfigurationResolver"/> before this record is constructed.
/// </remarks>
public sealed record QadEnvironment(
    string Name,
    bool IsProduction,
    SshEndpoint Ssh,
    RemotePaths Paths,
    CompileSettings Compile);
