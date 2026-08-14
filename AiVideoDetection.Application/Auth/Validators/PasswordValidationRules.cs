using FluentValidation;

namespace AiVideoDetection.Application.Auth.Validators;

internal static class PasswordValidationRules
{
    public const string StrongPasswordMessage =
        "Password must be at least 8 characters and include at least one uppercase letter, one number, and one special character.";

    public static IRuleBuilderOptions<T, string> StrongPassword<T>(this IRuleBuilder<T, string> ruleBuilder)
    {
        return ruleBuilder
            .NotEmpty()
            .MinimumLength(8)
            .WithMessage(StrongPasswordMessage)
            .Matches("[A-Z]")
            .WithMessage(StrongPasswordMessage)
            .Matches("[0-9]")
            .WithMessage(StrongPasswordMessage)
            .Matches("[^a-zA-Z0-9]")
            .WithMessage(StrongPasswordMessage);
    }
}
