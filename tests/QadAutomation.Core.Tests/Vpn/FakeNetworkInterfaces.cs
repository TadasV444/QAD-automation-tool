using QadAutomation.Core.Vpn;

namespace QadAutomation.Core.Tests.Vpn;

/// <summary>
/// The adapters a machine reports as up.
/// </summary>
/// <remarks>
/// Models the counterparty rather than recording expectations, like the other
/// fakes here. The list is what a real machine returns - tunnel adapters mixed in
/// with Ethernet, Wi-Fi and loopback - because a matcher that only ever sees the
/// tunnel it is looking for is not being tested at all.
/// </remarks>
internal sealed class FakeNetworkInterfaces : INetworkInterfaces
{
    /// <summary>What a Windows laptop with no tunnel up typically reports.</summary>
    public static readonly string[] Ordinary =
    [
        "Ethernet",
        "Intel(R) Ethernet Connection I219-LM",
        "Wi-Fi",
        "Intel(R) Wi-Fi 6 AX201 160MHz",
        "Loopback Pseudo-Interface 1"
    ];

    private readonly List<string> _adapters = [.. Ordinary];

    public FakeNetworkInterfaces With(params string[] adapters)
    {
        _adapters.AddRange(adapters);
        return this;
    }

    public IReadOnlyList<string> Active() => _adapters;
}
