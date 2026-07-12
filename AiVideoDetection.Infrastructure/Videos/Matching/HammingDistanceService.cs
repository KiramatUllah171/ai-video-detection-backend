using AiVideoDetection.Application.Videos.Interfaces;

namespace AiVideoDetection.Infrastructure.Videos.Matching;

public class HammingDistanceService : IHammingDistanceService
{
    public int Calculate(string? hashA, string? hashB)
    {
        if (string.IsNullOrWhiteSpace(hashA) || string.IsNullOrWhiteSpace(hashB))
        {
            return int.MaxValue;
        }

        var normalizedA = hashA.Trim();
        var normalizedB = hashB.Trim();
        if (normalizedA.Length != normalizedB.Length || !IsHex(normalizedA) || !IsHex(normalizedB))
        {
            return int.MaxValue;
        }

        var distance = 0;
        for (var i = 0; i < normalizedA.Length; i++)
        {
            var left = Convert.ToInt32(normalizedA[i].ToString(), 16);
            var right = Convert.ToInt32(normalizedB[i].ToString(), 16);
            distance += CountBits(left ^ right);
        }

        return distance;
    }

    public decimal ToSimilarity(int distance, int hashLength)
    {
        if (distance == int.MaxValue || hashLength <= 0)
        {
            return 0;
        }

        var totalBits = hashLength * 4m;
        return Math.Clamp(1m - (distance / totalBits), 0m, 1m);
    }

    private static bool IsHex(string value)
    {
        return value.All(character =>
            character is >= '0' and <= '9'
            || character is >= 'a' and <= 'f'
            || character is >= 'A' and <= 'F');
    }

    private static int CountBits(int value)
    {
        var count = 0;
        while (value != 0)
        {
            count += value & 1;
            value >>= 1;
        }

        return count;
    }
}
