namespace AiVideoDetection.Api.Options;

public sealed class ForwardedHeadersSecurityOptions
{
    public const string SectionName = "ForwardedHeaders";

    public bool Enabled { get; init; }

    public int ForwardLimit { get; init; } = 1;

    public string[] KnownProxies { get; init; } = [];

    public string[] KnownNetworks { get; init; } = [];
}
