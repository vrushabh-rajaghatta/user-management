using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Application.Audit;
using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Abstractions;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Application.Users.Commands.ResetPassword;

/// <summary>
/// CRD-C3 — ResetPassword.
///
/// CHANGES an existing credential; never creates one. Absence of a credential
/// is the pending-activation state (inv. 15), and activation — with its own
/// AccountActivated event — is the only way out of it. CRD-C2 refuses to issue
/// a reset token to an identity with no credential, so that path is closed at
/// the source; this handler still refuses it defensively, because a token
/// issued before that rule landed can still be live.
///
/// A TOKEN IS BURNED ONLY BY A RESET THAT COMMITS. Consumption runs inside the
/// transaction, so every refusal after it — an unusable password, a reused one,
/// a missing credential — rolls the consumption back and leaves the link
/// working. Burning a reset token because the user chose a password the policy
/// rejects would force them to request another mail for no security benefit.
///
/// CHEAPEST FIRST. The reuse check verifies the new password against up to N
/// stored hashes, each at its full adaptive work factor, so it runs only after
/// the token has proven itself and the free checks have passed. A request
/// without a live token never reaches a derivation.
///
/// Rate limited per client address by behaviour 11, before this handler runs,
/// which bounds the derivations a stream of requests can buy. See
/// docs/requirements.md, "Behaviour 11".
/// </summary>
public sealed class ResetPasswordCommandHandler
    : ICommandHandler<ResetPasswordCommand, ResetPasswordResult>
{
    /// <summary>
    /// One message for every token rejection, as CRD-C1 does: unknown id, wrong
    /// secret, wrong type, used, invalidated, expired, inactive subject, and a
    /// token whose identity has no credential are indistinguishable.
    /// </summary>
    private const string InvalidToken = "The password reset token is not valid.";

    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserTokenRepository _userTokenRepository;
    private readonly IUserTokenService _userTokenService;
    private readonly ISecurityPolicyResolver _securityPolicyResolver;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ICredentialRepository _credentialRepository;
    private readonly IPasswordHistoryRepository _passwordHistoryRepository;
    private readonly IBearerActorEstablisher _bearerActorEstablisher;
    private readonly IExecutionContext _executionContext;
    private readonly IAuditEvents _auditEvents;

    public ResetPasswordCommandHandler(
        IClock clock,
        IUnitOfWork unitOfWork,
        IUserTokenRepository userTokenRepository,
        IUserTokenService userTokenService,
        ISecurityPolicyResolver securityPolicyResolver,
        IPasswordHasher passwordHasher,
        ICredentialRepository credentialRepository,
        IPasswordHistoryRepository passwordHistoryRepository,
        IBearerActorEstablisher bearerActorEstablisher,
        IExecutionContext executionContext,
        IAuditEvents auditEvents)
    {
        _clock = clock;
        _unitOfWork = unitOfWork;
        _userTokenRepository = userTokenRepository;
        _userTokenService = userTokenService;
        _securityPolicyResolver = securityPolicyResolver;
        _passwordHasher = passwordHasher;
        _credentialRepository = credentialRepository;
        _passwordHistoryRepository = passwordHistoryRepository;
        _bearerActorEstablisher = bearerActorEstablisher;
        _executionContext = executionContext;
        _auditEvents = auditEvents;
    }

    public async Task<ResetPasswordResult> Handle(
        ResetPasswordCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = _clock.UtcNow;

        var presented = _userTokenService.Parse(command.TokenPlainText);

        if (presented is null)
        {
            DeclareRejection(tokenId: null, "Malformed");

            throw new BusinessRuleViolationException(InvalidToken);
        }

        return await _unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                // Step 1. Consume, typed and subject-checked, in one statement.
                // TokenType.PasswordReset is what stops an activation token
                // being used here; the subject predicate is what stops a
                // deactivated account being reset. Neither burns the token on
                // refusal, because both are predicates of the same UPDATE.
                var identityId = await _userTokenRepository.TryConsumeAsync(
                    presented.TokenId,
                    _userTokenService.Hash(presented.Secret),
                    TokenType.PasswordReset,
                    now,
                    ct);

                if (identityId is null)
                {
                    // No second read to classify the refusal: the statement
                    // that decides reports only whether it consumed.
                    DeclareRejection(presented.TokenId, "NotUsable");

                    throw new BusinessRuleViolationException(InvalidToken);
                }

                // Step 2. The consumption is the authentication (AUD-D28), so
                // the identity it returned is the sole authority for who is
                // acting. PasswordReset and TokenConsumed both require an
                // Authenticated origin.
                if (!await _bearerActorEstablisher.EstablishAsync(identityId, ct))
                    throw new BusinessRuleViolationException(InvalidToken);

                var subject = _executionContext.UserId;

                // Step 3. The credential to change. Null is reachable only for
                // a token issued before CRD-C2 required a credential. It is
                // refused with the invalid-token message and rolled back — and
                // NEVER turned into a new credential, which would make this a
                // second activation path.
                //
                // No TokenRejected here: the bearer is now established, so the
                // record would carry an Authenticated origin, and the catalogue
                // permits TokenRejected only as Anonymous.
                var credential = await _credentialRepository
                    .FindByIdentityAsync(identityId, ct);

                if (credential is null)
                    throw new BusinessRuleViolationException(InvalidToken);

                // Step 4. The policy floor, free to evaluate. Same surface as
                // CRD-C1: PasswordMinLength is max(tenant, baseline), resolved
                // now so a tightened baseline applies immediately.
                var policy = await _securityPolicyResolver
                    .GetEffectiveSettingsAsync(now, ct);

                if (command.NewPassword is null
                    || command.NewPassword.Length < policy.PasswordMinLength)
                {
                    throw new BusinessRuleViolationException(
                        "The password must be at least "
                        + $"{policy.PasswordMinLength} characters.");
                }

                // Step 5. Reuse — the expensive check, last before hashing.
                //
                // Each row is verified with ITS OWN stored hash and algorithm,
                // never re-derived with current parameters. The stored hash is
                // self-describing — scheme, work factor and salt are all in it
                // — so a password set years ago at a lower work factor is still
                // recognised. Hashing the new password afresh and comparing
                // strings would never match anything: every hash has its own
                // random salt.
                //
                // A corrupt row throws out of Verify and is NOT caught. Skipping
                // it would quietly shorten the reuse window and hide corruption
                // in our own data; the hasher's contract chose loudness.
                //
                // The wording below is not a frozen contract. What is required
                // is the refusal, its surface, and that the token survives it.
                var recent = await _passwordHistoryRepository.FindRecentAsync(
                    identityId, policy.PasswordHistoryDepth, ct);

                foreach (var previous in recent)
                {
                    var verification = _passwordHasher.Verify(
                        command.NewPassword,
                        previous.PasswordHash,
                        previous.PasswordAlgorithm);

                    if (verification.IsValid)
                    {
                        throw new BusinessRuleViolationException(
                            "The new password must not match a recently used password.");
                    }
                }

                // Step 6. Hash, then change. ChangePassword also clears
                // FailedAttemptCount and LockedUntil — a successful reset is the
                // frozen way out of a lockout. MustChangePassword is false: the
                // user chose this password themselves (CRD-C5 will set true).
                var hashed = _passwordHasher.Hash(command.NewPassword);

                credential.ChangePassword(
                    hashed.Hash,
                    hashed.Algorithm,
                    now,
                    mustChangePassword: false);

                // Step 7. CR5 — history in the SAME transaction, otherwise the
                // next reuse check has a permanent hole.
                await _passwordHistoryRepository.AddAsync(
                    PasswordHistory.Create(
                        PasswordHistoryId.New(),
                        identityId,
                        hashed.Hash,
                        hashed.Algorithm,
                        createdAt: now),
                    ct);

                _auditEvents.Emit("TokenConsumed", version: 1)
                    .Primary("Token", presented.TokenId.Value)
                    .Ref("Identity", identityId.Value, role: "Target")
                    .Ref("User", subject.Value, role: "Subject")
                    .WithPayload(new
                    {
                        consumedBy = "PasswordReset",
                        consumedAt = now,
                    });

                // Never the hash. The algorithm marker is a version label, and
                // whether a change is forced is a fact a reviewer needs.
                _auditEvents.Emit("PasswordReset", version: 1)
                    .Primary("Credential", credential.Id.Value)
                    .Ref("Identity", identityId.Value, role: "Target")
                    .Ref("User", subject.Value, role: "Subject")
                    .WithPayload(new
                    {
                        algorithm = hashed.Algorithm,
                        mustChangePassword = false,
                    });

                return new ResetPasswordResult(identityId);
            },
            cancellationToken);
    }

    /// <summary>
    /// The record of a refused token, before any bearer is established.
    /// Autonomous and Anonymous for CRD-C1's reasons: the command rolls back,
    /// so a transactional record would vanish with it.
    /// </summary>
    private void DeclareRejection(UserTokenId? tokenId, string reason)
    {
        var declaration = _auditEvents.Emit("TokenRejected", version: 1)
            .WithPayload(new { Reason = reason });

        if (tokenId is null)
            declaration.Primary("Token");
        else
            declaration.Primary("Token", tokenId.Value);
    }
}
