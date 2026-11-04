using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkTracker.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EnforceSingleActiveEntry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The unique index below can't be created over rows that break it, and nothing kept those
            // from being written: a start from the GUI and the CLI at the same time could leave two
            // running entries. No row gets deleted here and only the end of a row that had no valid
            // one changes, so the time a user tracked stays where it was.

            // The end decides: it is what the overlap checks and the worklogs always went by.
            migrationBuilder.Sql("""
                UPDATE "WorkEntries"
                SET "IsActive" = 0
                WHERE "IsActive" <> 0 AND "EndTime" IS NOT NULL;
                """);

            // Marked as finished without an end. Nothing was ever shown as running here, and treating
            // it as running would stop the entry that really is. Not a guess at how long it took.
            migrationBuilder.Sql("""
                UPDATE "WorkEntries"
                SET "EndTime" = "StartTime"
                WHERE "IsActive" = 0 AND "EndTime" IS NULL;
                """);

            // Several running: the newest stays, the same one the application has been showing. The
            // others end where the next entry starts, as they would have if starting it had stopped
            // them. Two with the same start have no time between them to keep.
            migrationBuilder.Sql("""
                UPDATE "WorkEntries"
                SET "EndTime" = COALESCE(
                        (SELECT MIN("Later"."StartTime")
                         FROM "WorkEntries" AS "Later"
                         WHERE "Later"."StartTime" > "WorkEntries"."StartTime"),
                        "StartTime"),
                    "IsActive" = 0
                WHERE "IsActive" <> 0
                  AND "Id" <> (SELECT "Newest"."Id"
                               FROM "WorkEntries" AS "Newest"
                               WHERE "Newest"."IsActive" <> 0
                               ORDER BY "Newest"."StartTime" DESC, "Newest"."Id" DESC
                               LIMIT 1);
                """);

            migrationBuilder.DropIndex(
                name: "IX_WorkEntries_IsActive",
                table: "WorkEntries");

            migrationBuilder.CreateIndex(
                name: "IX_WorkEntries_IsActive",
                table: "WorkEntries",
                column: "IsActive",
                unique: true,
                filter: "\"IsActive\" = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkEntries_IsActive",
                table: "WorkEntries");

            migrationBuilder.CreateIndex(
                name: "IX_WorkEntries_IsActive",
                table: "WorkEntries",
                column: "IsActive");
        }
    }
}
