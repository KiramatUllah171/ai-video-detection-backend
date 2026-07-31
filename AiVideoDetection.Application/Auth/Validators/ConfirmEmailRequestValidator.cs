using AiVideoDetection.Application.Auth.DTOs;
using FluentValidation;

namespace AiVideoDetection.Application.Auth.Validators;

public class ConfirmEmailRequestValidator : AbstractValidator<ConfirmEmailRequest>
{
    public ConfirmEmailRequestValidator()
    {
        RuleFor(request => request.Token)
            .NotEmpty()
            .MaximumLength(512);
    }
}
