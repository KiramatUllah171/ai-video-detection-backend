using AiVideoDetection.Application.Auth.DTOs;
using FluentValidation;

namespace AiVideoDetection.Application.Auth.Validators;

public class GoogleLoginRequestValidator : AbstractValidator<GoogleLoginRequest>
{
    public GoogleLoginRequestValidator()
    {
        RuleFor(request => request.Code)
            .NotEmpty()
            .MaximumLength(4096);

        RuleFor(request => request.RedirectUri)
            .NotEmpty()
            .MaximumLength(2048)
            .Must(value => Uri.TryCreate(value, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            .WithMessage("Redirect URI must be an absolute HTTP or HTTPS URL.");
    }
}
