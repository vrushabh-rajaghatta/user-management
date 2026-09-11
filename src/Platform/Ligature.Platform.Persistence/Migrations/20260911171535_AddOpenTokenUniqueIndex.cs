using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ligature.Platform.Persistence.Migrations
{
    /// <summary>
    /// UT4 — at most one OPEN token per (UserIdentityId, TokenType).
    ///
    /// "Open" means unused and uninvalidated. Note what the predicate
    /// deliberately does NOT say: expired. An expired-but-unused token still
    /// occupies the slot, because expiry is derived from a timestamp rather
    /// than stored as state (UM §6.5) and a partial index cannot depend on
    /// now() — it must be immutable.
    ///
    /// That is not a limitation being worked around; it is what makes UT4 and
    /// UT5 one mechanism. UT5 requires issuance to invalidate all prior unused
    /// tokens of that type "including already-expired ones", and this index is
    /// what makes forgetting that step impossible: without the expired clause
    /// in UT5, a reissue after expiry would collide here rather than silently
    /// producing two open tokens.
    ///
    /// Raw SQL rather than EF's HasIndex, matching the three partial unique
    /// indexes already in AddUserManagementPostgresConstraints — EF cannot
    /// express this filter portably, and the ux_ prefix marks the family.
    /// The EF model is unchanged, so the snapshot is untouched.
    ///
    /// NO DATA-CLEANUP STEP. PostgreSQL validates every existing row as it
    /// builds the index, so an environment already holding two open tokens for
    /// one identity and type fails this migration — the wanted outcome. Both
    /// are live credentials; silently invalidating one would decide, without
    /// evidence, which of two people holding a link gets to use it.
    ///
    /// A probe of the development database found 142 tokens, 137 of them open,
    /// and no violations. That establishes safety THERE; the index build is
    /// the authority everywhere else.
    /// </summary>
    public partial class AddOpenTokenUniqueIndex : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX "ux_user_token_open_per_type"
                ON "user_token" ("user_identity_id", "token_type")
                WHERE "used_at" IS NULL
                  AND "invalidated_at" IS NULL;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS "ux_user_token_open_per_type";
                """);
        }
    }
}
