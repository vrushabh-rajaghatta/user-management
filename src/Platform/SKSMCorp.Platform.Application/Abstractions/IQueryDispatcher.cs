using SKSMCorp.SharedKernel.Abstractions;

namespace SKSMCorp.Platform.Application.Abstractions;

/// <summary>
/// The seam between the Host and the application for reads
/// (docs/architecture.md section 11, decided in B6-A).
///
/// It exists for the same reason ICommandDispatcher does: an endpoint
/// resolving a handler directly would be a new architectural pattern, and the
/// application boundary stays consistent with the one commands established. It
/// is NOT justified by a future need for behaviours.
/// </summary>
public interface IQueryDispatcher
{
    Task<TResult> SendAsync<TQuery, TResult>(
        TQuery query,
        CancellationToken cancellationToken)
        where TQuery : IQuery<TResult>;
}
