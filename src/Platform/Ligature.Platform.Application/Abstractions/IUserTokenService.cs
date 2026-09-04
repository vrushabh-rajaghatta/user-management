namespace Ligature.Platform.Application.Abstractions;

public interface IUserTokenService
{
    TokenMaterial Generate();
}

public sealed record TokenMaterial(
    string PlainText,
    string Hash);