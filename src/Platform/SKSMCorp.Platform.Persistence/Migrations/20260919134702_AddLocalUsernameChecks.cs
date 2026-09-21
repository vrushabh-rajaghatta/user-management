using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SKSMCorp.Platform.Persistence.Migrations
{
    /// <summary>
    /// "Local usernames refuse surrounding whitespace" (docs/requirements.md,
    /// WS3 and WS4): the database backstops for the local username rule.
    ///
    /// ck_user_identity_local_username_no_surrounding_whitespace refuses a
    /// local username that begins or ends with one of char.IsWhiteSpace's 25
    /// characters, named explicitly rather than by a locale-dependent class.
    /// UserIdentity.ValidateUsernameBoundary is the rule; this is what holds
    /// when something bypasses it, and a test proves the two agree for every
    /// UTF-16 code unit.
    ///
    /// ck_user_identity_local_username_required closes UI6 from the entities
    /// workbook: a local identity has a username, and it is not empty.
    ///
    /// Deliberately NO data step. The dev and shared test databases were
    /// checked and hold no violating row; a database that does refuses this
    /// migration, which is the wanted outcome. Silently trimming stored
    /// identifiers would be exactly what the rule forbids.
    /// </summary>
    public partial class AddLocalUsernameChecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "ck_user_identity_local_username_no_surrounding_whitespace",
                table: "user_identity",
                sql: "\"identity_type\" <> 'Local' OR \"username\" IS NULL OR \"username\" !~ '^[\\u0009-\\u000D\\u0020\\u0085\\u00A0\\u1680\\u2000-\\u200A\\u2028\\u2029\\u202F\\u205F\\u3000]|[\\u0009-\\u000D\\u0020\\u0085\\u00A0\\u1680\\u2000-\\u200A\\u2028\\u2029\\u202F\\u205F\\u3000]$'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_user_identity_local_username_required",
                table: "user_identity",
                sql: "\"identity_type\" <> 'Local' OR (\"username\" IS NOT NULL AND \"username\" <> '')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_user_identity_local_username_no_surrounding_whitespace",
                table: "user_identity");

            migrationBuilder.DropCheckConstraint(
                name: "ck_user_identity_local_username_required",
                table: "user_identity");
        }
    }
}
