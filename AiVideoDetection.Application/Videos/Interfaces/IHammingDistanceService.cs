namespace AiVideoDetection.Application.Videos.Interfaces;

public interface IHammingDistanceService
{
    int Calculate(string? hashA, string? hashB);

    decimal ToSimilarity(int distance, int hashLength);
}
