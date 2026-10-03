namespace SmartBus.Api.Data;

/// <summary>
/// Kiểm tra chuỗi kết nối CSDL chung trước khi giao cho Npgsql.
///
/// Vì sao cần: máy mới kéo repo về chưa có appsettings.Development.json — file chứa chuỗi bị
/// .gitignore chặn vì repo public. Thiếu chuỗi mà đi thẳng vào Npgsql thì lỗi hiện ra là
/// ArgumentNullException hoặc lỗi parse khó hiểu, người mới không biết sửa ở đâu. Ở đây đổi
/// thành một thông báo nêu đúng các bước sửa (script scripts/setup-csdl.sh hoặc dán tay).
///
/// Lời gọi phải nằm trong lambda của AddDbContext — lambda chỉ chạy khi app thật dựng DbContext,
/// còn host test (TestAppFactory) gỡ đăng ký đó rồi thay bằng InMemory nên không đi qua đây.
/// </summary>
public static class ChuoiKetNoiCsdl
{
    /// <summary>Chỗ dán mẫu trong appsettings.Development.json.example — còn nguyên nghĩa là chưa cấu hình.</summary>
    public const string ChoDan = "DAN_CHUOI_KET_NOI_CSDL_CHUNG_VAO_DAY";

    /// <summary>Chuỗi đã được cấu hình thật hay chưa — dùng chung với SmartBus.Seed.</summary>
    public static bool DaCauHinh(string? chuoi)
        => !string.IsNullOrWhiteSpace(chuoi)
           && !chuoi.Contains(ChoDan, StringComparison.Ordinal);

    /// <summary>Trả lại nguyên chuỗi nếu đã cấu hình; ném hướng dẫn nếu còn thiếu.</summary>
    public static string KiemTra(string? chuoi)
    {
        if (DaCauHinh(chuoi))
        {
            return chuoi!;
        }

        throw new InvalidOperationException(
            """
            Chưa cấu hình chuỗi kết nối CSDL chung (ConnectionStrings:Default).

              1. Copy chuỗi kết nối trong tin nhắn GHIM ở chat nhóm — chuỗi không nằm trong repo
                 (repo public, xem luật "Không commit bí mật" trong docs/03-quy-uoc.md).
              2. Chạy script tự điền:  bash scripts/setup-csdl.sh
                 — hoặc tự dán chuỗi vào ConnectionStrings:Default của
                 backend/SmartBus.Api/appsettings.Development.json

            Hướng dẫn đầy đủ: docs/25-huong-dan-csdl-chung.md
            """);
    }
}
