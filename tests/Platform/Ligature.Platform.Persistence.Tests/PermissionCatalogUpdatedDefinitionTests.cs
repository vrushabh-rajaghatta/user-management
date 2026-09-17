using Ligature.Platform.Persistence.Audit;

namespace Ligature.Platform.Persistence.Tests;

/// <summary>
/// PRV-C2's audit identity, pinned (docs/requirements.md, PRV-C2 — Audit).
///
/// PermissionCatalogUpdated was defined in the deployed catalogue ahead of its
/// emitter, for a narrower conception of PRV-C2, and was corrected in place to
/// match the frozen contract. These tests hold that correction.
///
/// They pass the moment they are written, and that is the point: the correction
/// has already landed, and what it needs is something that fails if it is
/// reverted. Each of the three properties below was wrong before, and each has
/// a reason a later editor might plausibly "fix" it back.
/// </summary>
public sealed class PermissionCatalogUpdatedDefinitionTests
{
    private static EventTypeSeed Definition
        => AuditEventCatalogue.GetEventTypeSeeds()
            .Single(x => x.Code == "PermissionCatalogUpdated");

    /// <summary>
    /// THE CORRECTION THAT MATTERS. A refused synchronisation rolls back every
    /// catalogue mutation, and a Transactional record would be destroyed by the
    /// very outcome it exists to evidence.
    /// </summary>
    [Fact]
    public void It_is_written_autonomously_so_a_refusal_keeps_its_evidence()
    {
        Assert.Equal("Autonomous", Definition.WritePath);
    }

    /// <summary>
    /// Both references were Required. A run that changed nothing and a run that
    /// refused can satisfy neither, and the contract emits an event for both.
    /// </summary>
    [Fact]
    public void Its_permission_references_are_optional()
    {
        Assert.All(
            Definition.EntityRefRoles.Where(x => x.EntityType == "Permission"),
            role => Assert.False(
                role.Required,
                $"Permission/{role.RefRole} is required, which no no-change or refused run could satisfy."));
    }

    /// <summary>
    /// The event is about the synchronisation OPERATION. Adding Role or
    /// RolePermission references would turn one event into a row-by-row change
    /// log, which the contract rules out: breadth belongs in the payload counts.
    /// </summary>
    [Fact]
    public void It_references_no_role_or_grant_entities()
    {
        Assert.Equal(
            ["Permission"],
            Definition.EntityRefRoles.Select(x => x.EntityType).Distinct());

        Assert.Equal("PermissionCatalog", Definition.PrimaryEntityType);
        Assert.False(Definition.PrimaryEntityRequired);
    }

    /// <summary>
    /// The identity itself. Renaming the code would be catalogue churn and
    /// would strand the definition established for this requirement.
    /// </summary>
    [Fact]
    public void Its_code_is_the_release_controlled_identity_and_its_name_carries_the_semantics()
    {
        Assert.Equal("Catalogue synchronised", Definition.Name);
        Assert.True(Definition.IsActive);
        Assert.Equal(["System"], Definition.Origins);
    }
}
