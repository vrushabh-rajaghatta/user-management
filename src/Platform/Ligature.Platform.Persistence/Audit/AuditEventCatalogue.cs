using System.Text.Json.Serialization;

namespace Ligature.Platform.Persistence.Audit;

/// <summary>
/// The V1 event catalogue: the release-controlled seed for
/// <c>audit.audit_event_type</c> and <c>audit.audit_event_origin</c>.
///
/// The single source of these rows in code, in the same way
/// PlatformProvisioner.GetPermissionSeeds is the single source of the
/// permission catalogue. AuditCatalogueSeeder writes them at provisioning
/// (AUD-C4 step 3), and AuditCatalogueDriftTests asserts a provisioned
/// database still matches them. The Audit Entity Workbook's Event Catalogue
/// sheet is the normative origin; this list was generated from it, and the
/// normalisations below are recorded in the AUD-C4 story:
///
///   - "UserManagement (AM-01)" is the UserManagement context.
///   - "BeforeAfter (After only)" is shape BeforeAfter: creations carry no
///     Before, which is content, not shape (AR17).
///   - "Target entity if known (no)" is a NULL primary type, permitted by
///     ET3 only because the primary is not required.
///   - Refs marked (optional) or (if resolved) are not required; every
///     other declared ref is.
///   - PII paths carry the `describes` attribute ET5 v0.3 requires and the
///     sheet omits. RefRole:x paths were checked against a REQUIRED ref x,
///     and PrimarySubject paths against a user-subject primary type
///     (IMPL-10), when this list was generated.
///   - AuditInspected declares no PII paths: its sheet entry names an
///     identifier and says so, and identifiers are never listed (AUD-D29).
///   - The four Agent events are seeded inactive (AU11), origins included.
///   - Name is derived from the code; the workbook carries none.
///   - PayloadSchemaRef is null: no payload schemas exist yet (IMPL-09).
///
/// 49 rows: 42 UserManagement, 7 Audit. The seeding sheet's "47" predates
/// TokenRejected and AuditInspected, both added in v0.3.
/// </summary>
internal static class AuditEventCatalogue
{
    /// <summary>
    /// Recorded in TenantProvisioned's payload as auditCatalogueVersion, and
    /// the value AUD-C3 will compare against on a later release.
    /// </summary>
    public const int Version = 1;

    /// <summary>The two event types permitted an Anonymous origin (EO5).</summary>
    internal static readonly IReadOnlySet<string> AnonymousOriginCodes =
        new HashSet<string>(StringComparer.Ordinal) { "SignInFailed", "TokenRejected" };

    internal static IReadOnlyList<EventTypeSeed> GetEventTypeSeeds()
    {
        return
        [
            // PRV-C1
            new(
                "TenantProvisioned",
                "UserManagement",
                "Tenant provisioned",
                "Provisioning",
                ReasonRequired: false,
                "Transactional",
                "Payload",
                PrimaryEntityType: "Tenant",
                PrimaryEntityRequired: false,
                EntityRefRoles:
                [
                    new("SecurityPolicy", "InitialVersion", Required: true),
                ],
                PiiPaths: [],
                IsActive: true,
                Origins: ["System"]),

            // PRV-C2
            new(
                "PermissionCatalogUpdated",
                "UserManagement",
                "Permission catalog updated",
                "ConfigurationChange",
                ReasonRequired: false,
                "Transactional",
                "Payload",
                PrimaryEntityType: "PermissionCatalog",
                PrimaryEntityRequired: false,
                EntityRefRoles:
                [
                    new("Permission", "Added", Required: true),
                    new("Permission", "Changed", Required: true),
                ],
                PiiPaths: [],
                IsActive: true,
                Origins: ["System"]),

            // OPR-C1
            new(
                "PlatformAccessOpened",
                "UserManagement",
                "Platform access opened",
                "Provisioning",
                ReasonRequired: true,
                "Transactional",
                "Payload",
                PrimaryEntityType: "User",
                PrimaryEntityRequired: true,
                EntityRefRoles:
                [
                    new("Identity", "OperatorIdentity", Required: true),
                    new("UserRoleAssignment", "Assignment", Required: true),
                    new("Role", "GrantedRole", Required: true),
                ],
                PiiPaths: [],
                IsActive: true,
                Origins: ["System"]),

            // OPR-C2
            new(
                "PlatformAccessClosed",
                "UserManagement",
                "Platform access closed",
                "Provisioning",
                ReasonRequired: true,
                "Transactional",
                "Payload",
                PrimaryEntityType: "User",
                PrimaryEntityRequired: true,
                EntityRefRoles: [],
                PiiPaths: [],
                IsActive: true,
                Origins: ["System"]),

            // USR-C1, OPR-C1
            new(
                "UserCreated",
                "UserManagement",
                "User created",
                "IdentityLifecycle",
                ReasonRequired: false,
                "Transactional",
                "BeforeAfter",
                PrimaryEntityType: "User",
                PrimaryEntityRequired: true,
                EntityRefRoles: [],
                PiiPaths:
                [
                    new("After.FirstName", "PrimarySubject"),
                    new("After.LastName", "PrimarySubject"),
                    new("After.DisplayName", "PrimarySubject"),
                    new("After.Email", "PrimarySubject"),
                ],
                IsActive: true,
                Origins: ["Authenticated", "System"]),

            // USR-C2
            new(
                "UserProfileChanged",
                "UserManagement",
                "User profile changed",
                "IdentityLifecycle",
                ReasonRequired: false,
                "Transactional",
                "BeforeAfter",
                PrimaryEntityType: "User",
                PrimaryEntityRequired: true,
                EntityRefRoles: [],
                PiiPaths:
                [
                    new("Before.FirstName", "PrimarySubject"),
                    new("Before.LastName", "PrimarySubject"),
                    new("Before.DisplayName", "PrimarySubject"),
                    new("After.FirstName", "PrimarySubject"),
                    new("After.LastName", "PrimarySubject"),
                    new("After.DisplayName", "PrimarySubject"),
                ],
                IsActive: true,
                Origins: ["Authenticated"]),

            // USR-C3
            new(
                "UserEmailChanged",
                "UserManagement",
                "User email changed",
                "IdentityLifecycle",
                ReasonRequired: false,
                "Transactional",
                "BeforeAfter",
                PrimaryEntityType: "User",
                PrimaryEntityRequired: true,
                EntityRefRoles: [],
                PiiPaths:
                [
                    new("Before.Email", "PrimarySubject"),
                    new("After.Email", "PrimarySubject"),
                ],
                IsActive: true,
                Origins: ["Authenticated"]),

            // USR-C4, OPR-C2
            new(
                "UserDeactivated",
                "UserManagement",
                "User deactivated",
                "IdentityLifecycle",
                ReasonRequired: true,
                "Transactional",
                "BeforeAfter",
                PrimaryEntityType: "User",
                PrimaryEntityRequired: true,
                EntityRefRoles:
                [
                    new("User", "NewAgentOwner", Required: false),
                ],
                PiiPaths: [],
                IsActive: true,
                Origins: ["Authenticated", "System"]),

            // USR-C5, OPR-C1
            new(
                "UserReactivated",
                "UserManagement",
                "User reactivated",
                "IdentityLifecycle",
                ReasonRequired: true,
                "Transactional",
                "BeforeAfter",
                PrimaryEntityType: "User",
                PrimaryEntityRequired: true,
                EntityRefRoles: [],
                PiiPaths: [],
                IsActive: true,
                Origins: ["Authenticated", "System"]),

            // USR-C1, IDN-C1, OPR-C1
            new(
                "IdentityCreated",
                "UserManagement",
                "Identity created",
                "IdentityLifecycle",
                ReasonRequired: false,
                "Transactional",
                "BeforeAfter",
                PrimaryEntityType: "Identity",
                PrimaryEntityRequired: true,
                EntityRefRoles:
                [
                    new("User", "Subject", Required: true),
                ],
                PiiPaths:
                [
                    new("After.Username", "RefRole:Subject"),
                ],
                IsActive: true,
                Origins: ["Authenticated", "System"]),

            // IDN-C2
            new(
                "IdentityUsernameChanged",
                "UserManagement",
                "Identity username changed",
                "IdentityLifecycle",
                ReasonRequired: false,
                "Transactional",
                "BeforeAfter",
                PrimaryEntityType: "Identity",
                PrimaryEntityRequired: true,
                EntityRefRoles:
                [
                    new("User", "Subject", Required: true),
                ],
                PiiPaths:
                [
                    new("Before.Username", "RefRole:Subject"),
                    new("After.Username", "RefRole:Subject"),
                ],
                IsActive: true,
                Origins: ["Authenticated"]),

            // IDN-C3, USR-C4, OPR-C2
            new(
                "IdentityDeactivated",
                "UserManagement",
                "Identity deactivated",
                "IdentityLifecycle",
                ReasonRequired: true,
                "Transactional",
                "BeforeAfter",
                PrimaryEntityType: "Identity",
                PrimaryEntityRequired: true,
                EntityRefRoles:
                [
                    new("User", "Subject", Required: true),
                ],
                PiiPaths: [],
                IsActive: true,
                Origins: ["Authenticated", "System"]),

            // IDN-C4, OPR-C1
            new(
                "IdentityReactivated",
                "UserManagement",
                "Identity reactivated",
                "IdentityLifecycle",
                ReasonRequired: true,
                "Transactional",
                "BeforeAfter",
                PrimaryEntityType: "Identity",
                PrimaryEntityRequired: true,
                EntityRefRoles:
                [
                    new("User", "Subject", Required: true),
                ],
                PiiPaths: [],
                IsActive: true,
                Origins: ["Authenticated", "System"]),

            // USR-C1, CRD-C5
            new(
                "TokenIssued",
                "UserManagement",
                "Token issued",
                "SecurityEvent",
                ReasonRequired: false,
                "Transactional",
                "Payload",
                PrimaryEntityType: "Token",
                PrimaryEntityRequired: true,
                EntityRefRoles:
                [
                    new("Identity", "Target", Required: true),
                    new("User", "Subject", Required: true),
                ],
                PiiPaths: [],
                IsActive: true,
                Origins: ["Authenticated"]),

            // CRD-C2
            new(
                "PasswordResetRequested",
                "UserManagement",
                "Password reset requested",
                "SecurityEvent",
                ReasonRequired: false,
                "Transactional",
                "Payload",
                PrimaryEntityType: "Token",
                PrimaryEntityRequired: true,
                EntityRefRoles:
                [
                    new("Identity", "Target", Required: true),
                    new("User", "Subject", Required: true),
                ],
                PiiPaths:
                [
                    new("Payload.RequestIp", "RefRole:Subject"),
                ],
                IsActive: true,
                Origins: ["System"]),

            // USR-C1, CRD-C2, CRD-C5
            new(
                "TokenInvalidated",
                "UserManagement",
                "Token invalidated",
                "SecurityEvent",
                ReasonRequired: false,
                "Transactional",
                "Payload",
                PrimaryEntityType: "Token",
                PrimaryEntityRequired: true,
                EntityRefRoles:
                [
                    new("Identity", "Target", Required: true),
                    new("Token", "SupersededBy", Required: true),
                ],
                PiiPaths: [],
                IsActive: true,
                Origins: ["Authenticated", "System"]),

            // CRD-C1, CRD-C3
            new(
                "TokenConsumed",
                "UserManagement",
                "Token consumed",
                "SecurityEvent",
                ReasonRequired: false,
                "Transactional",
                "Payload",
                PrimaryEntityType: "Token",
                PrimaryEntityRequired: true,
                EntityRefRoles:
                [
                    new("Identity", "Target", Required: true),
                    new("User", "Subject", Required: true),
                ],
                PiiPaths: [],
                IsActive: true,
                Origins: ["Authenticated"]),

            // CRD-C1, CRD-C3 (failure branch)
            new(
                "TokenRejected",
                "UserManagement",
                "Token rejected",
                "SecurityEvent",
                ReasonRequired: false,
                "Autonomous",
                "Payload",
                PrimaryEntityType: "Token",
                PrimaryEntityRequired: false,
                EntityRefRoles: [],
                PiiPaths:
                [
                    new("Payload.Ip", "PrimarySubject"),
                ],
                IsActive: true,
                Origins: ["Anonymous"]),

            // CRD-C1
            new(
                "PasswordSet",
                "UserManagement",
                "Password set",
                "SecurityEvent",
                ReasonRequired: false,
                "Transactional",
                "Payload",
                PrimaryEntityType: "Credential",
                PrimaryEntityRequired: true,
                EntityRefRoles:
                [
                    new("Identity", "Target", Required: true),
                    new("User", "Subject", Required: true),
                ],
                PiiPaths: [],
                IsActive: true,
                Origins: ["Authenticated"]),

            // CRD-C1
            new(
                "AccountActivated",
                "UserManagement",
                "Account activated",
                "IdentityLifecycle",
                ReasonRequired: false,
                "Transactional",
                "None",
                PrimaryEntityType: "Identity",
                PrimaryEntityRequired: true,
                EntityRefRoles:
                [
                    new("User", "Subject", Required: true),
                ],
                PiiPaths: [],
                IsActive: true,
                Origins: ["Authenticated"]),

            // CRD-C3
            new(
                "PasswordReset",
                "UserManagement",
                "Password reset",
                "SecurityEvent",
                ReasonRequired: false,
                "Transactional",
                "Payload",
                PrimaryEntityType: "Credential",
                PrimaryEntityRequired: true,
                EntityRefRoles:
                [
                    new("Identity", "Target", Required: true),
                    new("User", "Subject", Required: true),
                ],
                PiiPaths: [],
                IsActive: true,
                Origins: ["Authenticated"]),

            // CRD-C4
            new(
                "PasswordChanged",
                "UserManagement",
                "Password changed",
                "SecurityEvent",
                ReasonRequired: false,
                "Transactional",
                "Payload",
                PrimaryEntityType: "Credential",
                PrimaryEntityRequired: true,
                EntityRefRoles:
                [
                    new("Identity", "Target", Required: true),
                ],
                PiiPaths: [],
                IsActive: true,
                Origins: ["Authenticated"]),

            // CRD-C5
            new(
                "AdminPasswordResetIssued",
                "UserManagement",
                "Admin password reset issued",
                "SecurityEvent",
                ReasonRequired: true,
                "Transactional",
                "Payload",
                PrimaryEntityType: "Credential",
                PrimaryEntityRequired: true,
                EntityRefRoles:
                [
                    new("Identity", "Target", Required: true),
                    new("User", "Subject", Required: true),
                    new("Token", "Issued", Required: true),
                ],
                PiiPaths: [],
                IsActive: true,
                Origins: ["Authenticated"]),

            // CRD-C6
            new(
                "AccountUnlocked",
                "UserManagement",
                "Account unlocked",
                "SecurityEvent",
                ReasonRequired: true,
                "Transactional",
                "BeforeAfter",
                PrimaryEntityType: "Credential",
                PrimaryEntityRequired: true,
                EntityRefRoles:
                [
                    new("Identity", "Target", Required: true),
                    new("User", "Subject", Required: true),
                ],
                PiiPaths: [],
                IsActive: true,
                Origins: ["Authenticated"]),

            // SES-C1
            new(
                "SignInSucceeded",
                "UserManagement",
                "Sign in succeeded",
                "SecurityEvent",
                ReasonRequired: false,
                "Transactional",
                "Payload",
                PrimaryEntityType: "Session",
                PrimaryEntityRequired: true,
                EntityRefRoles:
                [
                    new("Identity", "Target", Required: true),
                    new("User", "Subject", Required: true),
                ],
                PiiPaths:
                [
                    new("Payload.IpAddress", "RefRole:Subject"),
                ],
                IsActive: true,
                Origins: ["Authenticated"]),

            // SES-C1
            new(
                "SignInFailed",
                "UserManagement",
                "Sign in failed",
                "SecurityEvent",
                ReasonRequired: false,
                "Autonomous",
                "Payload",
                PrimaryEntityType: "Identity",
                PrimaryEntityRequired: false,
                EntityRefRoles:
                [
                    new("Identity", "Target", Required: false),
                ],
                PiiPaths:
                [
                    new("Payload.AttemptedIdentifier", "PrimarySubject"),
                    new("Payload.IpAddress", "PrimarySubject"),
                ],
                IsActive: true,
                Origins: ["Anonymous"]),

            // SES-C1
            new(
                "AccountLocked",
                "UserManagement",
                "Account locked",
                "SecurityEvent",
                ReasonRequired: false,
                "Transactional",
                "BeforeAfter",
                PrimaryEntityType: "Credential",
                PrimaryEntityRequired: true,
                EntityRefRoles:
                [
                    new("Identity", "Target", Required: true),
                    new("User", "Subject", Required: true),
                ],
                PiiPaths: [],
                IsActive: true,
                Origins: ["System"]),

            // SES-C2
            new(
                "SignedOut",
                "UserManagement",
                "Signed out",
                "SecurityEvent",
                ReasonRequired: false,
                "Transactional",
                "BeforeAfter",
                PrimaryEntityType: "Session",
                PrimaryEntityRequired: true,
                EntityRefRoles: [],
                PiiPaths: [],
                IsActive: true,
                Origins: ["Authenticated"]),

            // SES-C3, SES-C4, USR-C4, IDN-C3, OPR-C2
            new(
                "SessionRevoked",
                "UserManagement",
                "Session revoked",
                "SecurityEvent",
                ReasonRequired: true,
                "Transactional",
                "BeforeAfter",
                PrimaryEntityType: "Session",
                PrimaryEntityRequired: true,
                EntityRefRoles:
                [
                    new("Identity", "Target", Required: true),
                    new("User", "Subject", Required: true),
                ],
                PiiPaths: [],
                IsActive: true,
                Origins: ["Authenticated", "System"]),

            // AUT-C1, OPR-C1
            new(
                "RoleGranted",
                "UserManagement",
                "Role granted",
                "AuthorisationChange",
                ReasonRequired: true,
                "Transactional",
                "BeforeAfter",
                PrimaryEntityType: "UserRoleAssignment",
                PrimaryEntityRequired: true,
                EntityRefRoles:
                [
                    new("User", "Subject", Required: true),
                    new("Role", "GrantedRole", Required: true),
                ],
                PiiPaths: [],
                IsActive: true,
                Origins: ["Authenticated", "System"]),

            // AUT-C2, USR-C4, OPR-C2
            new(
                "RoleRevoked",
                "UserManagement",
                "Role revoked",
                "AuthorisationChange",
                ReasonRequired: true,
                "Transactional",
                "BeforeAfter",
                PrimaryEntityType: "UserRoleAssignment",
                PrimaryEntityRequired: true,
                EntityRefRoles:
                [
                    new("User", "Subject", Required: true),
                    new("Role", "RevokedRole", Required: true),
                ],
                PiiPaths: [],
                IsActive: true,
                Origins: ["Authenticated", "System"]),

            // AUT-C3
            new(
                "RoleCreated",
                "UserManagement",
                "Role created",
                "AuthorisationChange",
                ReasonRequired: false,
                "Transactional",
                "BeforeAfter",
                PrimaryEntityType: "Role",
                PrimaryEntityRequired: true,
                EntityRefRoles: [],
                PiiPaths: [],
                IsActive: true,
                Origins: ["Authenticated"]),

            // AUT-C4
            new(
                "RoleUpdated",
                "UserManagement",
                "Role updated",
                "AuthorisationChange",
                ReasonRequired: false,
                "Transactional",
                "BeforeAfter",
                PrimaryEntityType: "Role",
                PrimaryEntityRequired: true,
                EntityRefRoles: [],
                PiiPaths: [],
                IsActive: true,
                Origins: ["Authenticated"]),

            // AUT-C5
            new(
                "RoleDeactivated",
                "UserManagement",
                "Role deactivated",
                "AuthorisationChange",
                ReasonRequired: true,
                "Transactional",
                "BeforeAfter",
                PrimaryEntityType: "Role",
                PrimaryEntityRequired: true,
                EntityRefRoles: [],
                PiiPaths: [],
                IsActive: true,
                Origins: ["Authenticated"]),

            // AUT-C6
            new(
                "RoleReactivated",
                "UserManagement",
                "Role reactivated",
                "AuthorisationChange",
                ReasonRequired: false,
                "Transactional",
                "BeforeAfter",
                PrimaryEntityType: "Role",
                PrimaryEntityRequired: true,
                EntityRefRoles: [],
                PiiPaths: [],
                IsActive: true,
                Origins: ["Authenticated"]),

            // AUT-C7
            new(
                "PermissionGrantedToRole",
                "UserManagement",
                "Permission granted to role",
                "AuthorisationChange",
                ReasonRequired: false,
                "Transactional",
                "BeforeAfter",
                PrimaryEntityType: "RolePermissionGrant",
                PrimaryEntityRequired: true,
                EntityRefRoles:
                [
                    new("Role", "Target", Required: true),
                    new("Permission", "Granted", Required: true),
                ],
                PiiPaths: [],
                IsActive: true,
                Origins: ["Authenticated"]),

            // AUT-C8
            new(
                "PermissionRevokedFromRole",
                "UserManagement",
                "Permission revoked from role",
                "AuthorisationChange",
                ReasonRequired: true,
                "Transactional",
                "BeforeAfter",
                PrimaryEntityType: "RolePermissionGrant",
                PrimaryEntityRequired: true,
                EntityRefRoles:
                [
                    new("Role", "Target", Required: true),
                    new("Permission", "Revoked", Required: true),
                ],
                PiiPaths: [],
                IsActive: true,
                Origins: ["Authenticated"]),

            // POL-C1
            new(
                "SecurityPolicyVersionCreated",
                "UserManagement",
                "Security policy version created",
                "ConfigurationChange",
                ReasonRequired: true,
                "Transactional",
                "BeforeAfter",
                PrimaryEntityType: "SecurityPolicy",
                PrimaryEntityRequired: true,
                EntityRefRoles:
                [
                    new("SecurityPolicy", "PreviousVersion", Required: true),
                ],
                PiiPaths: [],
                IsActive: true,
                Origins: ["Authenticated"]),

            // AGT-C1
            new(
                "AgentRegistered",
                "UserManagement",
                "Agent registered",
                "IdentityLifecycle",
                ReasonRequired: true,
                "Transactional",
                "BeforeAfter",
                PrimaryEntityType: "User",
                PrimaryEntityRequired: true,
                EntityRefRoles:
                [
                    new("User", "Owner", Required: true),
                ],
                PiiPaths: [],
                IsActive: false,
                Origins: ["Authenticated"]),

            // AGT-C2, USR-C4
            new(
                "AgentOwnershipTransferred",
                "UserManagement",
                "Agent ownership transferred",
                "AuthorisationChange",
                ReasonRequired: true,
                "Transactional",
                "BeforeAfter",
                PrimaryEntityType: "User",
                PrimaryEntityRequired: true,
                EntityRefRoles:
                [
                    new("User", "PreviousOwner", Required: true),
                    new("User", "NewOwner", Required: true),
                ],
                PiiPaths: [],
                IsActive: false,
                Origins: ["Authenticated"]),

            // AGT-C3
            new(
                "AgentCredentialIssued",
                "UserManagement",
                "Agent credential issued",
                "SecurityEvent",
                ReasonRequired: false,
                "Transactional",
                "Payload",
                PrimaryEntityType: "AgentCredential",
                PrimaryEntityRequired: true,
                EntityRefRoles:
                [
                    new("Identity", "Target", Required: true),
                    new("User", "Agent", Required: true),
                ],
                PiiPaths: [],
                IsActive: false,
                Origins: ["Authenticated"]),

            // AGT-C4
            new(
                "AgentVersionActivated",
                "UserManagement",
                "Agent version activated",
                "ConfigurationChange",
                ReasonRequired: true,
                "Transactional",
                "BeforeAfter",
                PrimaryEntityType: "AgentVersion",
                PrimaryEntityRequired: true,
                EntityRefRoles:
                [
                    new("User", "Agent", Required: true),
                ],
                PiiPaths: [],
                IsActive: false,
                Origins: ["Authenticated"]),

            // Pipeline behaviours 3, 4; UR10, UR16
            new(
                "AuthorisationDenied",
                "Audit",
                "Authorisation denied",
                "Refusal",
                ReasonRequired: false,
                "Autonomous",
                "Payload",
                PrimaryEntityType: null,
                PrimaryEntityRequired: false,
                EntityRefRoles: [],
                PiiPaths: [],
                IsActive: true,
                Origins: ["Authenticated"]),

            // Pipeline behaviour 8
            new(
                "CommandRejected",
                "Audit",
                "Command rejected",
                "Refusal",
                ReasonRequired: false,
                "Autonomous",
                "Payload",
                PrimaryEntityType: null,
                PrimaryEntityRequired: false,
                EntityRefRoles: [],
                PiiPaths: [],
                IsActive: true,
                Origins: ["Authenticated"]),

            // AUD-Q5
            new(
                "AuditExported",
                "Audit",
                "Audit exported",
                "AuditAdministration",
                ReasonRequired: true,
                "Transactional",
                "Payload",
                PrimaryEntityType: "AuditTrail",
                PrimaryEntityRequired: false,
                EntityRefRoles: [],
                PiiPaths: [],
                IsActive: true,
                Origins: ["Authenticated"]),

            // Query pipeline (behaviour 19)
            new(
                "AuditInspected",
                "Audit",
                "Audit inspected",
                "AuditAdministration",
                ReasonRequired: false,
                "Autonomous",
                "Payload",
                PrimaryEntityType: "AuditTrail",
                PrimaryEntityRequired: false,
                EntityRefRoles: [],
                PiiPaths: [],
                IsActive: true,
                Origins: ["Authenticated"]),

            // AUD-C1
            new(
                "AuditAnonymised",
                "Audit",
                "Audit anonymised",
                "AuditAdministration",
                ReasonRequired: true,
                "Transactional",
                "Payload",
                PrimaryEntityType: "User",
                PrimaryEntityRequired: true,
                EntityRefRoles:
                [
                    new("User", "DataSubject", Required: true),
                ],
                PiiPaths: [],
                IsActive: true,
                Origins: ["Authenticated"]),

            // AUD-C2
            new(
                "AuditRetentionPolicyVersionCreated",
                "Audit",
                "Audit retention policy version created",
                "ConfigurationChange",
                ReasonRequired: true,
                "Transactional",
                "BeforeAfter",
                PrimaryEntityType: "AuditRetentionPolicy",
                PrimaryEntityRequired: true,
                EntityRefRoles:
                [
                    new("AuditRetentionPolicy", "PreviousVersion", Required: true),
                ],
                PiiPaths: [],
                IsActive: true,
                Origins: ["Authenticated"]),

            // Release migration (AUD-C3)
            new(
                "AuditEventCatalogueUpdated",
                "Audit",
                "Audit event catalogue updated",
                "ConfigurationChange",
                ReasonRequired: false,
                "Transactional",
                "Payload",
                PrimaryEntityType: "AuditEventCatalogue",
                PrimaryEntityRequired: false,
                EntityRefRoles: [],
                PiiPaths: [],
                IsActive: true,
                Origins: ["System"]),
        ];
    }
}

/// <summary>One row of audit_event_type plus its audit_event_origin rows.</summary>
internal sealed record EventTypeSeed(
    string Code,
    string OwningContext,
    string Name,
    string DefaultClassification,
    bool ReasonRequired,
    string WritePath,
    string Shape,
    string? PrimaryEntityType,
    bool PrimaryEntityRequired,
    IReadOnlyList<EntityRefRoleSeed> EntityRefRoles,
    IReadOnlyList<PiiPathSeed> PiiPaths,
    bool IsActive,
    IReadOnlyList<string> Origins)
{
    /// <summary>Every seed is Version 1; the column exists for AUD-C3 (ET8).</summary>
    public int Version => AuditEventCatalogue.Version;
}

/// <summary>
/// ET4 — a (EntityType, RefRole) pair a record of this type may carry, and
/// whether it must. Serialised with the workbook's own key casing, so the
/// deployed JSON reads exactly as the normative text does.
/// </summary>
internal sealed record EntityRefRoleSeed(
    [property: JsonPropertyName("EntityType")] string EntityType,
    [property: JsonPropertyName("RefRole")] string RefRole,
    [property: JsonPropertyName("Required")] bool Required);

/// <summary>
/// ET5 — a transformable path and whom it describes: Actor, PrimarySubject or
/// RefRole:&lt;role&gt;. The workbook writes this entry as {path, describes},
/// lower case, and so does the column.
/// </summary>
internal sealed record PiiPathSeed(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("describes")] string Describes);
