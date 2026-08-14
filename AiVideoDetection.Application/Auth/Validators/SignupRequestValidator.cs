using AiVideoDetection.Application.Auth.DTOs;
using FluentValidation;

namespace AiVideoDetection.Application.Auth.Validators;

public class SignupRequestValidator : AbstractValidator<SignupRequest>
{
    public SignupRequestValidator()
    {
        RuleFor(request => request.Name)
            .NotEmpty()
            .MaximumLength(200);

        RuleFor(request => request.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(320);

        RuleFor(request => request.Password)
            .StrongPassword();

        RuleFor(request => request.ConfirmPassword)
            .Equal(request => request.Password)
            .WithMessage("Confirm password must match password.");
    }
}
