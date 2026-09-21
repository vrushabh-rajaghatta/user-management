namespace SKSMCorp.Platform.Application.Abstractions;

/// <summary>
/// Declares an anonymous command that authenticates its own bearer credential
/// — a password, an emailed token — and establishes the caller from it
/// (AUD-D28): SES-C1 SignIn, CRD-C1 ActivateAccount, CRD-C3 ResetPassword.
///
/// A bearer-authenticated identity-establishing command may execute only when
/// no caller is already established in the execution context.
/// AuthenticationBehavior enforces that before the command starts.
///
/// Why. The execution context's identity cannot be rebound once established
/// (docs/architecture.md section 11, E2a), and every record a command writes
/// takes its origin from the scope's caller. Under an established caller, such
/// a command could verify or consume another account's credential and then be
/// unable to act as that account's owner, and its failure records — which the
/// catalogue permits only an Anonymous origin (EO5) — could not be written.
/// Before this marker existed, that made a correct password for another account
/// answer differently from a wrong one.
///
/// Narrower than IAnonymousCommand on purpose. A plain anonymous command that
/// establishes nobody — CRD-C2 RequestPasswordReset — still runs with or without
/// a caller.
/// </summary>
public interface IBearerAuthenticatedCommand<TResult> : IAnonymousCommand<TResult>
{
}
