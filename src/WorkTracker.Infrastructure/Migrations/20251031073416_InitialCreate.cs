using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkTracker.Infrastructure.Migrations
{
	/// <inheritdoc />
	public partial class InitialCreate : Migration
	{
		/// <inheritdoc />
		protected override void Up(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.CreateTable(
				name: "WorkEntries",
				columns: table => new
				{
					Id = table.Column<int>(type: "INTEGER", nullable: false)
						.Annotation("Sqlite:Autoincrement", true),
					TicketId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
					StartTime = table.Column<DateTime>(type: "TEXT", nullable: false),
					EndTime = table.Column<DateTime>(type: "TEXT", nullable: true),
					Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
					IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
					CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
					UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
				},
				constraints: table =>
				{
					table.PrimaryKey("PK_WorkEntries", x => x.Id);
				});

			migrationBuilder.CreateIndex(
				name: "IX_WorkEntries_IsActive",
				table: "WorkEntries",
				column: "IsActive");

			migrationBuilder.CreateIndex(
				name: "IX_WorkEntries_StartTime",
				table: "WorkEntries",
				column: "StartTime");
		}

		/// <inheritdoc />
		protected override void Down(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.DropTable(
				name: "WorkEntries");
		}
	}
}
