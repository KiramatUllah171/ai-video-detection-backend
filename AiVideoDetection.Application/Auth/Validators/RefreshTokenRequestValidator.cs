using AiVideoDetection.Application.Auth.DTOs;
using FluentValidation;

namespace AiVideoDetection.Application.Auth.Validators;

public class RefreshTokenRequestValidator : AbstractValidator<RefreshTokenRequest>
{
    public RefreshTokenRequestValidator()
    {
        RuleFor(request => request.RefreshToken)
            .NotEmpty();
    }
}
