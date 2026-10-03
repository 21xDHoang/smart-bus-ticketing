using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Seed;

// Nạp dữ liệu nền cho CSDL dùng chung của nhóm (Supabase) hoặc PostgreSQL local:
//
//   dotnet run --project backend/SmartBus.Seed -- --password "<mật khẩu seed>"
//   dotnet run --project backend/SmartBus.Seed -- --password "<mật khẩu seed>" --reset
//
// Mật khẩu seed KHÔNG nằm trong repo (repo public) — lấy ở tin nhắn ghim trong chat nhóm.
// Chuỗi kết nối cũng vậy; tool này đọc lại chính file cấu hình mà API đang dùng
// (backend/SmartBus.Api/appsettings.Development.json) nên không phải khai hai nơi.
//
// Hướng dẫn đầy đủ: docs/25-huong-dan-csdl-chung.md.

var (reset, password, showHelp, loiThamSo) = ParseArgs(args);

if (loiThamSo is not null)
{
    Console.Error.WriteLine(loiThamSo);
    Console.Error.WriteLine("Xem hướng dẫn: dotnet run --project backend/SmartBus.Seed -- --help");
    return 2;
}

if (showHelp)
{
    PrintHelp();
    return 0;
}

if (password is null)
{
    Console.Error.WriteLine("Thiếu --password (mật khẩu seed lấy ở chat nhóm).");
    Console.Error.WriteLine("Xem hướng dẫn: dotnet run --project backend/SmartBus.Seed -- --help");
    return 2;
}

var (connectionString, nguon) = ResolveConnectionString();

if (connectionString is null)
{
    Console.Error.WriteLine(
        """
        Không tìm thấy chuỗi kết nối CSDL (hoặc file cấu hình còn nguyên chỗ dán mẫu).
        Nhanh nhất — copy chuỗi kết nối trong tin nhắn ghim ở chat nhóm rồi chạy:
          bash scripts/setup-csdl.sh
        Hoặc làm tay:
          1. Tạo backend/SmartBus.Api/appsettings.Development.json từ file .example (xem README),
             rồi dán chuỗi kết nối CSDL chung — lấy ở chat nhóm — vào ConnectionStrings:Default.
          2. Hoặc đặt biến môi trường ConnectionStrings__Default.
        """);
    return 1;
}

try
{
    var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(connectionString)
        .Options;

    await using var db = new AppDbContext(dbOptions);

    // In nguồn (không in chuỗi — chuỗi có mật khẩu CSDL) để biết seed đang chạy vào CSDL nào.
    Console.WriteLine($"Chuỗi kết nối đọc từ: {nguon}");

    var ketQua = await SampleDataSeeder.RunAsync(db, reset, password);

    if (ketQua.ResetPerformed)
    {
        Console.WriteLine("--reset: đã xoá toàn bộ dữ liệu nghiệp vụ trước khi seed.");
    }

    Console.WriteLine($"Tài khoản: tạo mới {ketQua.AccountsCreated}, đã có sẵn bỏ qua {ketQua.AccountsSkipped}");
    Console.WriteLine(
        $"Trạm: {ketQua.StopsCreated} · Tuyến: {ketQua.RoutesCreated} · " +
        $"Xe: {ketQua.BusesCreated} · Chuyến: {ketQua.TripsCreated} · " +
        $"Phản ánh: {ketQua.FeedbacksCreated}");
    Console.WriteLine();
    Console.WriteLine("Đăng nhập bằng 4 số điện thoại 0900000001–0900000004 (xem bảng vai trò ở docs/25).");
    Console.WriteLine("Mật khẩu: mật khẩu seed trong chat nhóm.");

    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"Seed thất bại: {ex.Message}");
    Console.Error.WriteLine(
        "Gợi ý: kiểm tra chuỗi kết nối trong appsettings.Development.json và CSDL đã chạy " +
        "`dotnet ef database update` chưa (xem docs/25).");
    return 1;
}

static (bool Reset, string? Password, bool ShowHelp, string? LoiThamSo) ParseArgs(string[] args)
{
    var reset = false;
    string? password = null;
    var showHelp = false;

    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--reset":
                reset = true;
                break;

            case "--password":
                if (i + 1 >= args.Length)
                {
                    return (reset, null, showHelp, "Tham số --password thiếu giá trị.");
                }

                password = args[++i];
                break;

            case "--help" or "-h":
                showHelp = true;
                break;

            default:
                return (reset, password, showHelp, $"Tham số không nhận diện được: {args[i]}");
        }
    }

    return (reset, password, showHelp, null);
}

/// <summary>
/// Tìm chuỗi kết nối: biến môi trường trước (cùng thứ tự ưu tiên env &gt; json của ASP.NET Core),
/// rồi tới appsettings.Development.json của API. Đi ngược từ thư mục hiện tại lên gốc ổ đĩa để
/// chạy được từ gốc repo, từ backend/ lẫn từ SmartBus.Seed/ — dotnet run đổi thư mục làm việc
/// theo project nên không đoán được người dùng đang đứng ở đâu.
/// </summary>
static (string? ConnectionString, string Nguon) ResolveConnectionString()
{
    var tuBienMoiTruong = Environment.GetEnvironmentVariable("ConnectionStrings__Default");
    if (!string.IsNullOrWhiteSpace(tuBienMoiTruong))
    {
        return (tuBienMoiTruong, "biến môi trường ConnectionStrings__Default");
    }

    for (var thuMuc = new DirectoryInfo(Directory.GetCurrentDirectory());
         thuMuc is not null;
         thuMuc = thuMuc.Parent)
    {
        foreach (var duongDan in new[]
                 {
                     Path.Combine(thuMuc.FullName, "backend", "SmartBus.Api", "appsettings.Development.json"),
                     Path.Combine(thuMuc.FullName, "SmartBus.Api", "appsettings.Development.json"),
                 })
        {
            if (!File.Exists(duongDan))
            {
                continue;
            }

            // Đọc bằng System.Text.Json — chỉ cần đúng một giá trị, không kéo thêm gói
            // Microsoft.Extensions.Configuration vào project console này. Skip comment + cho phép
            // dấu phẩy cuối để file local lỡ có sửa tay vẫn đọc được thay vì đổ.
            using var json = JsonDocument.Parse(
                File.ReadAllText(duongDan),
                new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                });

            if (json.RootElement.TryGetProperty("ConnectionStrings", out var connectionStrings)
                && connectionStrings.TryGetProperty("Default", out var macDinh)
                && ChuoiKetNoiCsdl.DaCauHinh(macDinh.GetString()))
            {
                return (macDinh.GetString(), duongDan);
            }
        }
    }

    return (null, string.Empty);
}

static void PrintHelp()
{
    Console.WriteLine(
        """
        Nạp dữ liệu nền cho CSDL dùng chung của nhóm — hướng dẫn đầy đủ ở
        docs/25-huong-dan-csdl-chung.md.

        Cách dùng:
          dotnet run --project backend/SmartBus.Seed -- --password "<mật khẩu seed>"
          dotnet run --project backend/SmartBus.Seed -- --password "<mật khẩu seed>" --reset

        Tham số:
          --password <chuỗi>  Mật khẩu cho 4 tài khoản mẫu (lấy ở chat nhóm, tối thiểu 8 ký tự).
          --reset             Xoá TOÀN BỘ dữ liệu nghiệp vụ rồi seed lại — báo nhóm trước khi chạy.
          --help, -h          In hướng dẫn này.

        Chuỗi kết nối đọc theo thứ tự: biến môi trường ConnectionStrings__Default, rồi
        backend/SmartBus.Api/appsettings.Development.json (đúng file API đang dùng).
        """);
}
