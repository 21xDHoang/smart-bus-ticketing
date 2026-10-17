using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartBus.Api.Migrations
{
    /// <inheritdoc />
    public partial class Sprint5_Payments_RevenueIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Hai chỉ mục phục vụ báo cáo doanh thu (US 19 — Sprint 5 dòng 38).
            //
            // RevenueReportService lọc Status = 'Success' và khoảng thời gian theo biểu thức
            // COALESCE("PaidAt", "CreatedAt"). EF Core không biểu diễn được chỉ mục biểu thức
            // bằng HasIndex nên phải viết SQL tay; model snapshot không đổi (hai chỉ mục này
            // không nằm trong model EF), xem docs/47 §2–§3.
            //
            // INCLUDE ba cột service đọc (PaidAt, CreatedAt, Amount) để SUM/COUNT và truy vấn
            // lấy dòng thô có thể chạy index-only scan. WHERE Status = 'Success' thu nhỏ chỉ
            // mục — báo cáo chỉ đếm giao dịch thành công.
            migrationBuilder.Sql(
                """
                CREATE INDEX "IX_Payments_Success_RevenueAt"
                    ON "Payments" ((COALESCE("PaidAt", "CreatedAt")))
                    INCLUDE ("PaidAt", "CreatedAt", "Amount")
                    WHERE "Status" = 'Success';
                """);

            // Nhánh lọc tuyến dùng p."TripId" = ANY(@tripIds); TripId đứng đầu để mỗi trip là
            // một lần seek, rồi khoảng thời gian là cột thứ hai.
            migrationBuilder.Sql(
                """
                CREATE INDEX "IX_Payments_Success_TripId_RevenueAt"
                    ON "Payments" ("TripId", (COALESCE("PaidAt", "CreatedAt")))
                    INCLUDE ("PaidAt", "CreatedAt", "Amount")
                    WHERE "Status" = 'Success';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS "IX_Payments_Success_TripId_RevenueAt";
                """);

            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS "IX_Payments_Success_RevenueAt";
                """);
        }
    }
}
