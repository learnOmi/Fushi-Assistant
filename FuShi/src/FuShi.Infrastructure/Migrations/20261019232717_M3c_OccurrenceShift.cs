using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuShi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class M3c_OccurrenceShift : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ShiftBatchId",
                table: "Occurrences",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ShiftMinutes",
                table: "Occurrences",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShiftBatchId",
                table: "Occurrences");

            migrationBuilder.DropColumn(
                name: "ShiftMinutes",
                table: "Occurrences");
        }
    }
}
