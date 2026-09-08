using Ligature.SharedKernel.Abstractions;

namespace Ligature.Platform.Application.Abstractions;

/// <summary>
/// Declares that a command runs without an authenticated caller (CRD-C1).
///
/// Authentication is required by default; this is the only way out. The
/// direction matters: forgetting the marker makes a command demand
/// authentication it does not need, which fails loudly in a test. The opposite
/// convention — a marker meaning "authenticated" — would make forgetting it
/// expose a command to anonymous callers, and the failure would be silent.
///
/// Deliberately empty. It declares a fact about the command; the decision of
/// what to do with that fact belongs to AuthenticationBehavior.
///
/// Permissionless is NOT anonymous. CRD-C4 ChangePassword is an authenticated
/// self-service command that declares no catalogue permission, so inferring
/// anonymity from the absence of IAuthorizableCommand would silently open it.
/// </summary>
public interface IAnonymousCommand<TResult> : ICommand<TResult>
{
}
