using Ligature.Platform.Domain.Users;

namespace Ligature.Platform.Application.Abstractions;

public interface IExecutionContext
{
    UserId UserId { get; }

    ActorType ActorType { get; }

    bool IsAuthenticated { get; }
}