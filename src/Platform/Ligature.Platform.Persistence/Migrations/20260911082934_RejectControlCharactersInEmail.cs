using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ligature.Platform.Persistence.Migrations
{
    /// <summary>
    /// Makes "an email address contains no control character" a property of the
    /// database rather than of the code paths that happen to write one.
    ///
    /// EmailAddress rejects the same characters, and the two are mirrored
    /// deliberately: char.IsControl covers U+0000-U+001F and U+007F-U+009F, and
    /// so does the expression below. [[:cntrl:]] would have been equivalent on
    /// the server this was written against, and was rejected for being
    /// locale-dependent — an invariant should not mean different things in
    /// different environments.
    ///
    /// WHY IT MATTERS. The address is interpolated into a mail header when an
    /// activation link is sent; an embedded CR or LF there injects headers or
    /// terminates the header block and replaces the body. It also reaches the
    /// audit trail and the caller context. The mail transport defends itself
    /// independently, but a value that cannot exist needs no defending.
    ///
    /// NO DATA CLEANUP STEP, deliberately. PostgreSQL validates every existing
    /// row as this constraint is added, so an environment holding a bad address
    /// fails the migration — which is the wanted outcome. Silently transforming
    /// stored personal data would be worse than stopping.
    ///
    /// U+0000 appears in the range for completeness only: PostgreSQL refuses it
    /// in a varchar at the text encoding boundary (22021), so it never reaches
    /// this constraint. Two mechanisms, not one.
    /// </summary>
    public partial class RejectControlCharactersInEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "ck_app_user_email_no_control_characters",
                table: "app_user",
                sql: "\"email\" IS NULL OR \"email\" !~ '[\\x00-\\x1F\\x7F-\\x9F]'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_app_user_email_no_control_characters",
                table: "app_user");
        }
    }
}
