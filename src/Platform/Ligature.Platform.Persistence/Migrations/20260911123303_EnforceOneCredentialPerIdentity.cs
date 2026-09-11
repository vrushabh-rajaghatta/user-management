using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ligature.Platform.Persistence.Migrations
{
    /// <summary>
    /// CR1 — one credential per identity, 1:0..1, made structural.
    ///
    /// EF already created this index to support the composite foreign key on
    /// (user_identity_id, identity_type); it simply was not unique, so two
    /// credential rows for one identity were possible. The composite key
    /// pinned identity_type to the identity and constrained cardinality not at
    /// all.
    ///
    /// Unique on the PAIR, not on user_identity_id alone. The two are
    /// equivalent here — the foreign key forces credential.identity_type to
    /// equal the identity's own, and an identity has exactly one — so this
    /// states CR1 exactly while adding no second index over overlapping
    /// columns.
    ///
    /// WHY IT MATTERS, given nothing produces a second row today. Activation
    /// inserts a credential unconditionally, with a fresh id and no check for
    /// an existing one, and CredentialRepository resolves with
    /// FirstOrDefaultAsync. A second row would therefore not be an error
    /// anybody sees: it would be authentication against whichever of two
    /// password hashes the query happened to return. The consumption contract
    /// in this same story closes the one path that could produce it; this
    /// closes the shape of the failure, whatever path is invented later.
    ///
    /// NO DATA-CLEANUP STEP, deliberately. PostgreSQL validates every existing
    /// row as it builds a unique index, so an environment holding duplicate
    /// credentials fails this migration — which is the wanted outcome.
    /// Silently deleting one of two password hashes would be worse than
    /// stopping, because there is no way to know which one its owner uses.
    ///
    /// A probe of the development database found 5 credentials over 5 distinct
    /// identities and no duplicates, which establishes that the migration is
    /// safe THERE. The index build is the authority everywhere else.
    /// </summary>
    public partial class EnforceOneCredentialPerIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_credential_user_identity_id_identity_type",
                table: "credential");

            migrationBuilder.CreateIndex(
                name: "IX_credential_user_identity_id_identity_type",
                table: "credential",
                columns: new[] { "user_identity_id", "identity_type" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_credential_user_identity_id_identity_type",
                table: "credential");

            migrationBuilder.CreateIndex(
                name: "IX_credential_user_identity_id_identity_type",
                table: "credential",
                columns: new[] { "user_identity_id", "identity_type" });
        }
    }
}
