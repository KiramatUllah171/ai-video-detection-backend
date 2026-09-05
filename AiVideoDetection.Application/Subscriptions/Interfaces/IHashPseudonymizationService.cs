namespace AiVideoDetection.Application.Subscriptions.Interfaces;

public interface IHashPseudonymizationService
{
    string HashVersion { get; }

    string HashValue(string purpose, string value);
}
