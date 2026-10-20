using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuShi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class M3c_DropSnoozeUntil : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 旧数据修正：取值 2 曾为 Snoozed（「稍后」旧实现）。M3c 起「稍后」已改为「回退 Pending + 叠顺延量」，
            // 不再有 Snoozed 状态。把残留的 Snoozed 行归位成合法的 Pending（清 FiredAt、顺延量归零），
            // 避免出现「Pending 却带 FiredAt」的僵尸行（新口径下 Pending 恒要求 FiredAt 为 null）。
            migrationBuilder.Sql("UPDATE Occurrences SET Status = 0, FiredAt = NULL, ShiftMinutes = 0 WHERE Status = 2;");

            migrationBuilder.DropColumn(
                name: "SnoozeUntil",
                table: "Occurrences");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SnoozeUntil",
                table: "Occurrences",
                type: "TEXT",
                nullable: true);
        }
    }
}
