using Ligature.Platform.Application.Abstractions;
using Ligature.Platform.Domain.Users;
using Ligature.SharedKernel.Abstractions;
using Ligature.SharedKernel.Exceptions;

namespace Ligature.Platform.Application.Users.Commands.CreateUser;

public sealed class CreateUserCommandHandler
    : ICommandHandler<CreateUserCommand, CreateUserResult>
{
    private readonly IExecutionContext _executionContext;
    private readonly IClock _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserRepository _userRepository;
    private readonly IUserIdentityRepository _userIdentityRepository;
    private readonly IUserTokenRepository _userTokenRepository;
    private readonly ISecurityPolicyResolver _securityPolicyResolver;
    private readonly IUserTokenService _userTokenService;

    public CreateUserCommandHandler(
        IExecutionContext executionContext,
        IClock clock,
        IUnitOfWork unitOfWork,
        IUserRepository userRepository,
        IUserIdentityRepository userIdentityRepository,
        IUserTokenRepository userTokenRepository,
        ISecurityPolicyResolver securityPolicyResolver,
        IUserTokenService userTokenService)
    {
        _executionContext = executionContext;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _userRepository = userRepository;
        _userIdentityRepository = userIdentityRepository;
        _userTokenRepository = userTokenRepository;
        _securityPolicyResolver = securityPolicyResolver;
        _userTokenService = userTokenService;
    }

    public async Task<CreateUserResult> Handle(
    CreateUserCommand command,
    CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        var email =
            EmailAddress.Create(command.Email);

        if (await _userRepository
                .ExistsActiveHumanWithEmailAsync(
                    email,
                    cancellationToken))
        {
            throw new BusinessRuleViolationException(
                "A user with this email address already exists.");
        }

        if (await _userIdentityRepository
                .ExistsWithUsernameAsync(
                    command.InitialUsername,
                    cancellationToken))
        {
            throw new BusinessRuleViolationException(
                "A user identity with this username already exists.");
        }

        var userId = UserId.New();
        var identityId = UserIdentityId.New();

        var user = User.CreateHuman(
            userId,
            command.FirstName,
            command.LastName,
            command.DisplayName,
            command.Email,
            now,
            _executionContext.UserId
            );

        var identity = UserIdentity.CreateLocal(
            identityId,
            userId,
            ActorType.Human,
            command.InitialUsername,
            now,
            _executionContext.UserId
            );

        var result =
            await _unitOfWork.ExecuteInTransactionAsync(
                async ct =>
                {
                    var policy =
                        await _securityPolicyResolver
                            .GetEffectiveSettingsAsync(
                                now,
                                ct);

                    // The id is minted first because the delivered token
                    // embeds it: CRD-C1 consumes by primary key.
                    var tokenId = UserTokenId.New();

                    var tokenMaterial =
                        _userTokenService.Generate(tokenId);

                    // TODO:
                    // Pass tokenMaterial.PlainText to the post-commit
                    // activation-email/outbox workflow.
                    // Never persist the plaintext token.

                    var activationToken =
                        UserToken.Create(
                            tokenId,
                            identityId,
                            TokenType.Activation,
                            tokenMaterial.Hash,
                            now,
                            now + policy.ActivationTokenLifetime,
                            _executionContext.UserId);

                    await _userRepository.AddAsync(
                        user,
                        ct);

                    await _userIdentityRepository.AddAsync(
                        identity,
                        ct);

                    await _userTokenRepository.AddAsync(
                        activationToken,
                        ct);

                    // TODO:
                    // Persist audit event(s) in this transaction.

                    // TODO:
                    // Queue activation email after successful commit.
                    // Never persist the plaintext activation token.

                    return new CreateUserResult(
                        userId,
                        identityId);
                },
                cancellationToken);

        return result;
    }
}