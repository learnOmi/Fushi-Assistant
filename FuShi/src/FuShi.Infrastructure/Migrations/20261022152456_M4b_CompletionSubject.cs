using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuShi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class M4b_CompletionSubject : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SubjectId",
                table: "CompletionLogs",
                type: "INTEGER",
                nullable: true);

            // 旧数据回填（M4b）：历史记录只有 EntryId，按仍存在的条目补上科目快照。
            // 条目已被删除或来源缺失的行保持 NULL，统计里归入「未分类」——
            // 回填失败不影响启动，也不能用 DELETE 重来，历史宁可少分类也不丢。
            migrationBuilder.Sql(
                "UPDATE CompletionLogs " +
                "SET SubjectId = (SELECT e.SubjectId FROM ScheduleEntries e WHERE e.Id = CompletionLogs.EntryId) " +
                "WHERE SubjectId IS NULL " +
                "AND EXISTS (SELECT 1 FROM ScheduleEntries e WHERE e.Id = CompletionLogs.EntryId);");

            migrationBuilder.CreateIndex(
                name: "IX_CompletionLogs_Date",
                table: "CompletionLogs",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_CompletionLogs_SubjectId",
                table: "CompletionLogs",
                column: "SubjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CompletionLogs_Date",
                table: "CompletionLogs");

            migrationBuilder.DropIndex(
                name: "IX_CompletionLogs_SubjectId",
                table: "CompletionLogs");

            migrationBuilder.DropColumn(
                name: "SubjectId",
                table: "CompletionLogs");
        }
    }
}
