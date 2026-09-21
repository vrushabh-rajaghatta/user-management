using SKSMCorp.Platform.Application.Abstractions;
using SKSMCorp.SharedKernel.Abstractions;

namespace SKSMCorp.Platform.Application.Roles.Queries.WhoCanDo;

/// <summary>
/// AUT-Q7 (docs/requirements.md, "AUT-Q7 WhoCanDo"). A query has no pipeline,
/// so the handler establishes, in order: authenticate, authorise EVERY
/// declared permission, refuse a scope that cannot exist, then ask the shared
/// resolver.
///
/// It performs NO authorisation evaluation of its own (RW2): the answer comes
/// from IAuthorizationService, which is the same evaluation the pipeline uses.
/// </summary>
public sealed class WhoCanDoQueryHandler
    : IQueryHandler<WhoCanDoQuery, WhoCanDoResult>
{
    private readonly IExecutionContext _executionContext;
    private readonly IAuthorizationService _authorizationService;
    private readonly IPermissionCatalogueEntryReader _permissions;
    private readonly IClock _clock;

    public WhoCanDoQueryHandler(
        IExecutionContext executionContext,
        IAuthorizationService authorizationService,
        IPermissionCatalogueEntryReader permissions,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(executionContext);
        ArgumentNullException.ThrowIfNull(authorizationService);
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(clock);

        _executionContext = executionContext;
        _authorizationService = authorizationService;
        _permissions = permissions;
        _clock = clock;
    }

    public Task<WhoCanDoResult> Handle(
        WhoCanDoQuery query,
        CancellationToken cancellationToken)
        => throw new NotImplementedException("AUT-Q7 is not implemented yet.");
}
