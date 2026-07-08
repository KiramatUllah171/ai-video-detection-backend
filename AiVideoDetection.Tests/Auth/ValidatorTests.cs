using AiVideoDetection.Application.Auth.DTOs;
using AiVideoDetection.Application.Auth.Validators;

namespace AiVideoDetection.Tests.Auth;

public class ValidatorTests
{
    [Fact]
    public void SignupValidator_RejectsInvalidEmail()
    {
        var validator = new SignupRequestValidator();
        var request = ValidSignupRequest();
        request.Email = "invalid-email";

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void SignupValidator_RejectsShortPassword()
    {
        var validator = new SignupRequestValidator();
        var request = ValidSignupRequest();
        request.Password = "short";
        request.ConfirmPassword = "short";

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void SignupValidator_RejectsMismatchedConfirmPassword()
    {
        var validator = new SignupRequestValidator();
        var request = ValidSignupRequest();
        request.ConfirmPassword = "DifferentPassword1";

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void LoginValidator_RejectsEmptyEmailAndPassword()
    {
        var validator = new LoginRequestValidator();
        var request = new LoginRequest();

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(LoginRequest.Email));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(LoginRequest.Password));
    }

    private static SignupRequest ValidSignupRequest()
    {
        return new SignupRequest
        {
            Name = "Test User",
            Email = "test@example.com",
            Password = "Password123",
            ConfirmPassword = "Password123"
        };
    }
}
