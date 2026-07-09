using AiVideoDetection.Application.Videos.Ai;

namespace AiVideoDetection.Application.Videos.Interfaces;

public interface IFinalScoringService
{
    FinalScoringResult Calculate(FinalScoringInput input);
}
