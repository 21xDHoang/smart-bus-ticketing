using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Entities;
using SmartBus.Api.Services;
using Route = SmartBus.Api.Entities.Route;

namespace SmartBus.Api.Seed;

/// <summary>
/// Bộ dữ liệu nền cho CSDL dùng chung của nhóm (chốt 01/10/2026, mục 3): 4 tài khoản — mỗi vai
/// trò một cái — cộng bộ trạm/tuyến/giá vé/xe/chuyến tối thiểu để mở app lên là có việc để làm.
///
/// Logic nằm trong project API (chứ không nằm trong project console) để bộ test chạm tới được:
/// <c>backend/SmartBus.Seed</c> chỉ là vỏ mỏng đọc chuỗi kết nối rồi gọi vào đây. Nhờ vậy mọi
/// hành vi của seed — tạo tài khoản, bỏ qua dữ liệu đã có, reset — đều có test InMemory như phần
/// còn lại của backend.
///
/// Ba tính chất cố ý, đừng phá khi sửa:
///   - <b>Idempotent</b>: chạy lại không nhân bản dữ liệu. Tài khoản khớp theo số điện thoại,
///     trạm theo tên, tuyến theo mã, xe theo biển số, chuyến theo "tuyến đã có chuyến nào chưa".
///   - <b>Mật khẩu không nằm trong repo</b> — repo public, nên mật khẩu seed truyền vào lúc chạy
///     và lấy từ chat nhóm. Băm bằng đúng <see cref="PasswordService"/> của API để tài khoản seed
///     đăng nhập được y hệt tài khoản đăng ký qua API.
///   - <b>Reset là một lệnh, không phải xếp tay thứ tự xoá</b>: danh sách bảng lấy từ chính model
///     EF, sprint sau thêm bảng thì bảng mới tự nằm trong lượt reset — không ai phải nhớ sửa file
///     này (xem <see cref="ResetAsync"/>).
/// </summary>
public static class SampleDataSeeder
{
    /// <summary>Độ dài tối thiểu của mật khẩu seed — chặn giá trị quá ngắn do gõ nhầm.</summary>
    public const int MinPasswordLength = 8;

    /// <summary>Múi giờ Việt Nam — cùng hằng số và cùng lý do với <see cref="TripGenerationService"/>.</summary>
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    /// <summary>Số ngày sinh chuyến mẫu tính từ hôm nay (giờ Việt Nam) — xem <see cref="SeedTripsAsync"/>.</summary>
    private const int SoNgaySinhChuyen = 3;

    /// <summary>Các khung giờ chạy (giờ Việt Nam) của chuyến mẫu trong một ngày.</summary>
    private static readonly int[] KhungGioChay = [5, 7, 9, 11, 13, 15, 17];

    /// <summary>Tốc độ trung bình để ước lượng giờ tới bến — chỉ để hiển thị, không phải số liệu vận hành.</summary>
    private const double TocDoTrungBinhKmH = 25;

    /// <summary>
    /// Bốn tài khoản mẫu, mỗi vai trò một cái. Số điện thoại cố định để tài liệu và chat nhóm
    /// chỉ đúng "đăng nhập bằng số này" mà không cần tra CSDL. Mật khẩu dùng chung một giá trị
    /// truyền vào lúc chạy — đây là tài khoản dev của CSDL dev, không phải tài khoản thật.
    /// </summary>
    private static readonly (string PhoneNumber, string FullName, Guid RoleId)[] TaiKhoanMau =
    [
        ("0900000001", "Quản trị viên", RoleIds.Admin),
        ("0900000002", "Quản lý mẫu", RoleIds.Manager),
        ("0900000003", "Tài xế mẫu", RoleIds.Driver),
        ("0900000004", "Hành khách mẫu", RoleIds.Passenger),
    ];

    /// <summary>
    /// Trạm mẫu quanh TP.HCM — đủ để dựng hai tuyến có trạm chung (Bến Thành) như thực tế.
    /// Toạ độ chỉ cần đúng cỡ để bản đồ hiển thị hợp lý, không phải dữ liệu trắc địa.
    /// </summary>
    private static readonly (string Name, string Address, double Latitude, double Longitude)[] TramMau =
    [
        ("Bến Thành", "Quận 1, TP.HCM", 10.7719, 106.6980),
        ("Công viên 23/9", "Quận 1, TP.HCM", 10.7676, 106.6930),
        ("Đại học Y Dược", "Quận 5, TP.HCM", 10.7553, 106.6870),
        ("Chợ Lớn", "Quận 5, TP.HCM", 10.7529, 106.6515),
        ("Ngã tư Hàng Xanh", "Bình Thạnh, TP.HCM", 10.8014, 106.7116),
        ("Bến xe Miền Đông", "Bình Thạnh, TP.HCM", 10.8146, 106.7120),
        ("Suối Tiên", "Thủ Đức, TP.HCM", 10.8670, 106.8025),
    ];

    /// <summary>
    /// Hai tuyến mẫu kèm thứ tự trạm và bảng giá. Khoảng cách trong <see cref="TuyenMau.Stops"/>
    /// là từ trạm liền trước tới trạm này (trạm đầu = 0) — đúng ngữ nghĩa cột RouteStops.DistanceKm.
    /// Giá ưu đãi đặt 50% giá phổ thông cho dễ kiểm tra bằng mắt khi demo US 17.
    /// </summary>
    private static readonly TuyenMau[] TuyenDuongMau =
    [
        new(
            Code: "01",
            Name: "Bến Thành — Chợ Lớn",
            Origin: "Bến Thành",
            Destination: "Chợ Lớn",
            Stops:
            [
                ("Bến Thành", 0m),
                ("Công viên 23/9", 1.5m),
                ("Đại học Y Dược", 2.5m),
                ("Chợ Lớn", 3.0m),
            ],
            Fares:
            [
                (PassengerType.Standard, 7000m),
                (PassengerType.Student, 3500m),
                (PassengerType.Senior, 3500m),
            ]),
        new(
            Code: "02",
            Name: "Bến Thành — Suối Tiên",
            Origin: "Bến Thành",
            Destination: "Suối Tiên",
            Stops:
            [
                ("Bến Thành", 0m),
                ("Ngã tư Hàng Xanh", 3.5m),
                ("Bến xe Miền Đông", 4.0m),
                ("Suối Tiên", 8.5m),
            ],
            Fares:
            [
                (PassengerType.Standard, 10000m),
                (PassengerType.Student, 5000m),
                (PassengerType.Senior, 5000m),
            ]),
    ];

    /// <summary>
    /// Ba xe mẫu: hai xe đang khai thác (đủ để xoay vòng gán chuyến) và một xe bảo dưỡng — có sẵn
    /// một trạng thái khác Active để màn hình đội xe và bộ lọc có dữ liệu mà kiểm tra bằng mắt.
    ///
    /// Cố ý KHÔNG sinh ghế (Seats): chưa có luồng nào sinh ghế trong API (Sprint 3 mới có chọn ghế),
    /// và bịa trước một quy cách mã ghế ở đây sẽ chọi với quy cách thật của Sprint 3.
    /// </summary>
    private static readonly (string LicensePlate, string BusType, int Capacity, BusStatus Status)[] XeMau =
    [
        ("51B-123.45", "Xe buýt 45 chỗ", 45, BusStatus.Active),
        ("51B-234.56", "Xe buýt 45 chỗ", 45, BusStatus.Active),
        ("51B-345.67", "Xe buýt 29 chỗ", 29, BusStatus.Maintenance),
    ];

    /// <summary>
    /// Chạy trọn một lượt seed. <paramref name="reset"/> = true thì xoá toàn bộ dữ liệu nghiệp vụ
    /// trước khi seed (giữ nguyên 4 vai trò và lịch sử migration).
    /// </summary>
    public static async Task<SeedResult> RunAsync(
        AppDbContext db,
        bool reset,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        if (password.Length < MinPasswordLength)
        {
            throw new ArgumentException(
                $"Mật khẩu seed phải có ít nhất {MinPasswordLength} ký tự.", nameof(password));
        }

        if (reset)
        {
            await ResetAsync(db, cancellationToken);
        }

        // Thứ tự có nghĩa: vai trò trước tài khoản, trạm trước tuyến (gán trạm tra theo tên),
        // xe trước chuyến (chuyến cần xe đang khai thác).
        await EnsureRolesAsync(db, cancellationToken);

        var (taiKhoanTao, taiKhoanDaCo) = await SeedAccountsAsync(db, password, cancellationToken);
        var tramTao = await SeedStopsAsync(db, cancellationToken);
        var tuyenTao = await SeedRoutesAsync(db, cancellationToken);
        var xeTao = await SeedBusesAsync(db, cancellationToken);
        var chuyenTao = await SeedTripsAsync(db, cancellationToken);

        return new SeedResult(reset, taiKhoanTao, taiKhoanDaCo, tramTao, tuyenTao, xeTao, chuyenTao);
    }

    /// <summary>
    /// Xoá toàn bộ dữ liệu nghiệp vụ. Trên PostgreSQL dùng một câu <c>TRUNCATE ... CASCADE</c>:
    /// danh sách bảng lấy từ chính model EF nên bảng của sprint sau tự động được xoá theo, và
    /// CASCADE để Postgres tự lo thứ tự khoá ngoại — xếp tay thứ tự 17 bảng là cách chắc chắn
    /// hỏng khi ai đó thêm bảng mới.
    ///
    /// Giữ lại hai bảng, mỗi bảng một lý do:
    ///   - <c>Roles</c> — danh mục chuẩn do migration seed sẵn, xoá đi thì mọi tài khoản mất vai
    ///     trò và phải chờ migration seed lại (migration đã chạy rồi thì không chạy lại).
    ///   - <c>__EFMigrationsHistory</c> — xoá là mất lịch sử migration; lượt <c>database update</c>
    ///     sau sẽ cố chạy lại từ migration đầu tiên trên CSDL đã có bảng và đổ vỡ.
    /// </summary>
    private static async Task ResetAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        // Bỏ hết thực thể đang bị theo dõi: context còn giữ chúng thì SaveChanges kế tiếp sẽ
        // ghi lại đúng những dòng vừa bị xoá.
        db.ChangeTracker.Clear();

        if (db.Database.IsRelational())
        {
            var bang = db.Model.GetEntityTypes()
                .Select(e => e.GetTableName())
                .Where(ten => ten is not null && ten != "Roles" && ten != "__EFMigrationsHistory")
                .Distinct()
                .Select(ten => $"\"{ten}\"")
                .ToList();

            // Ghép SQL vào biến trước khi gọi: tên bảng là định danh nên KHÔNG thể tham số hoá
            // được (TRUNCATE TABLE @ten là SQL sai), nhưng nguồn duy nhất của chúng là model EF —
            // không có đầu vào người dùng nào lọt vào đây, nên không phải đường SQL injection.
            var cauTruncate = $"TRUNCATE TABLE {string.Join(", ", bang)} CASCADE";
            await db.Database.ExecuteSqlRawAsync(cauTruncate, cancellationToken);
            return;
        }

        // Provider InMemory của bộ test không hiểu SQL — dựng lại kho rỗng thay cho TRUNCATE.
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
    }

    /// <summary>
    /// Bảo đảm 4 vai trò có mặt. Trên CSDL thật việc này thường là no-op vì migration đã seed
    /// (HasData ở AppDbContext.UserRoles.cs) — nhưng seeder phải tự đứng được một mình: CSDL
    /// dựng bằng EnsureCreated (bộ test) không có HasData, và bảng Roles bị giữ lại qua reset nên
    /// nhánh này cũng là lưới an toàn nếu ai đó xoá nhầm.
    /// Tên vai trò chép đúng từng chữ từ HasData để hai nguồn không bao giờ lệch nhau.
    /// </summary>
    private static async Task EnsureRolesAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var idDaCo = await db.Roles.Select(r => r.Id).ToListAsync(cancellationToken);

        var conThieu = VaiTroMacDinh().Where(r => !idDaCo.Contains(r.Id)).ToList();
        if (conThieu.Count == 0)
        {
            return;
        }

        db.Roles.AddRange(conThieu);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Tạo 4 tài khoản mẫu còn thiếu (khớp theo số điện thoại). Tài khoản tạo ra được gán vai trò
    /// ở CẢ HAI chỗ mà RBAC đọc: cột <see cref="User.RoleId"/> (vai trò chính phát trong JWT) và
    /// bảng nối <see cref="UserRole"/> — cùng cách <c>AuthService</c> đăng ký hành khách.
    /// </summary>
    private static async Task<(int Tao, int DaCo)> SeedAccountsAsync(
        AppDbContext db, string password, CancellationToken cancellationToken)
    {
        var soDienThoaiDaCo = await db.Users.Select(u => u.PhoneNumber).ToListAsync(cancellationToken);

        var tao = 0;
        var daCo = 0;

        foreach (var tk in TaiKhoanMau)
        {
            if (soDienThoaiDaCo.Contains(tk.PhoneNumber))
            {
                daCo++;
                continue;
            }

            var user = new User
            {
                PhoneNumber = tk.PhoneNumber,
                FullName = tk.FullName,
                // Băm bằng đúng PasswordService của API — hai luồng đăng ký và seed không bao giờ
                // lệch cấu hình băm (workFactor, thư viện).
                PasswordHash = PasswordService.HashPassword(password),
                IsActive = true,
                RoleId = tk.RoleId,
            };
            user.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = tk.RoleId });

            db.Users.Add(user);
            tao++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return (tao, daCo);
    }

    /// <summary>Tạo các trạm mẫu còn thiếu, khớp theo tên trạm.</summary>
    private static async Task<int> SeedStopsAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var tenDaCo = await db.Stops.Select(s => s.Name).ToListAsync(cancellationToken);

        var moi = TramMau
            .Where(t => !tenDaCo.Contains(t.Name))
            .Select(t => new Stop
            {
                Name = t.Name,
                Address = t.Address,
                Latitude = t.Latitude,
                Longitude = t.Longitude,
            })
            .ToList();

        db.Stops.AddRange(moi);
        await db.SaveChangesAsync(cancellationToken);
        return moi.Count;
    }

    /// <summary>
    /// Tạo các tuyến mẫu còn thiếu, khớp theo mã tuyến, kèm gán trạm (RouteStops) và giá vé
    /// (Fares). Tuyến đã tồn tại thì bỏ qua CẢ CỤM — kể cả gán trạm và giá — để người đã sửa
    /// dữ liệu qua giao diện không bị seeder chép đè.
    /// </summary>
    private static async Task<int> SeedRoutesAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var maDaCo = await db.Routes.Select(r => r.Code).ToListAsync(cancellationToken);

        // Trạm đã được SeedStopsAsync bảo đảm tồn tại trước khi hàm này chạy.
        var tramTheoTen = await db.Stops.ToDictionaryAsync(s => s.Name, s => s.Id, cancellationToken);

        var tao = 0;

        foreach (var tuyen in TuyenDuongMau)
        {
            if (maDaCo.Contains(tuyen.Code))
            {
                continue;
            }

            var route = new Route
            {
                Code = tuyen.Code,
                Name = tuyen.Name,
                Origin = tuyen.Origin,
                Destination = tuyen.Destination,
                // Tổng chiều dài = cộng dồn khoảng cách các chặng, đúng định nghĩa cột (xem Route.DistanceKm).
                DistanceKm = tuyen.Stops.Sum(s => s.DistanceKm),
                Status = RouteStatus.Active,
            };
            db.Routes.Add(route);

            for (var i = 0; i < tuyen.Stops.Length; i++)
            {
                var (tenTram, distanceKm) = tuyen.Stops[i];
                db.RouteStops.Add(new RouteStop
                {
                    RouteId = route.Id,
                    StopId = tramTheoTen[tenTram],
                    StopOrder = i + 1,
                    DistanceKm = distanceKm,
                });
            }

            foreach (var (passengerType, price) in tuyen.Fares)
            {
                db.Fares.Add(new Fare
                {
                    RouteId = route.Id,
                    PassengerType = passengerType,
                    Price = price,
                });
            }

            tao++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return tao;
    }

    /// <summary>Tạo các xe mẫu còn thiếu, khớp theo biển số.</summary>
    private static async Task<int> SeedBusesAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var bienSoDaCo = await db.Buses.Select(b => b.LicensePlate).ToListAsync(cancellationToken);

        var moi = XeMau
            .Where(x => !bienSoDaCo.Contains(x.LicensePlate))
            .Select(x => new Bus
            {
                LicensePlate = x.LicensePlate,
                BusType = x.BusType,
                Capacity = x.Capacity,
                Status = x.Status,
            })
            .ToList();

        db.Buses.AddRange(moi);
        await db.SaveChangesAsync(cancellationToken);
        return moi.Count;
    }

    /// <summary>
    /// Sinh chuyến mẫu cho từng tuyến mẫu CHƯA có chuyến nào: các khung giờ cố định của hôm nay
    /// và 2 ngày tới (giờ Việt Nam).
    ///
    /// Vì sao bắt đầu từ HÔM NAY chứ không phải ngày mai: mở app lên là màn hình tra cứu có kết
    /// quả ngay trong ngày, không phải chờ tới mai. Vài khung giờ đã trôi qua trong ngày cũng
    /// không sao — chúng vẫn là "ngày gần nhất có chuyến", tức mẫu để job sinh chuyến
    /// (<see cref="TripGenerationBackgroundService"/>) nhân bản sang 7 ngày kế tiếp khi API chạy.
    ///
    /// Vì sao chỉ sinh khi tuyến CHƯA có chuyến: chạy lại seeder không được nhân bản chuyến, và
    /// càng không được chép thêm đè lên lịch trình mà quản lý đã lập qua giao diện (US 13).
    /// Tuyến đã có chuyến — dù chỉ một — là tuyến đã có chủ.
    /// </summary>
    private static async Task<int> SeedTripsAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var maTuyen = TuyenDuongMau.Select(t => t.Code).ToList();
        var tuyenMau = await db.Routes.Where(r => maTuyen.Contains(r.Code)).ToListAsync(cancellationToken);

        // Cùng luật với job sinh chuyến: chỉ gán xe đang khai thác.
        var xeDangChay = await db.Buses
            .Where(b => b.Status == BusStatus.Active)
            .OrderBy(b => b.LicensePlate)
            .ToListAsync(cancellationToken);

        if (tuyenMau.Count == 0 || xeDangChay.Count == 0)
        {
            return 0;
        }

        var homNay = DateTime.UtcNow.Add(VietnamOffset).Date;
        var soChuyen = 0;
        var viTriXe = 0;

        foreach (var route in tuyenMau)
        {
            var daCoChuyen = await db.Trips.AnyAsync(t => t.RouteId == route.Id, cancellationToken);
            if (daCoChuyen)
            {
                continue;
            }

            for (var ngay = 0; ngay < SoNgaySinhChuyen; ngay++)
            {
                var ngayLocal = homNay.AddDays(ngay);

                foreach (var gio in KhungGioChay)
                {
                    // Nửa đêm giờ Việt Nam nằm ở 17:00 UTC ngày hôm trước — SpecifyKind tường minh
                    // vì Npgsql từ chối ghi DateTime Kind Unspecified vào cột timestamptz.
                    var khoiHanh = DateTime.SpecifyKind(
                        ngayLocal.AddHours(gio) - VietnamOffset, DateTimeKind.Utc);

                    db.Trips.Add(new Trip
                    {
                        RouteId = route.Id,
                        BusId = xeDangChay[viTriXe++ % xeDangChay.Count].Id,
                        DepartureTime = khoiHanh,
                        ArrivalTime = khoiHanh + ThoiGianChayUocLuong(route.DistanceKm),
                        // Chuyến sinh trước khi điều xe — DriverId để trống, đúng nghiệp vụ US 14.
                        Status = TripStatus.Scheduled,
                    });
                    soChuyen++;
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return soChuyen;
    }

    /// <summary>
    /// Ước lượng thời gian chạy hết tuyến: tốc độ trung bình 25 km/h, làm tròn LÊN bội số 5 phút,
    /// tối thiểu 15 phút. Chỉ để giờ tới bến hiển thị hợp lý — số liệu vận hành thật không nằm ở đây.
    /// </summary>
    private static TimeSpan ThoiGianChayUocLuong(decimal distanceKm)
    {
        var phut = (int)Math.Ceiling((double)distanceKm / TocDoTrungBinhKmH * 60 / 5) * 5;
        return TimeSpan.FromMinutes(Math.Max(15, phut));
    }

    private static Role[] VaiTroMacDinh() =>
    [
        new Role { Id = RoleIds.Admin, Code = RoleCodes.Admin, Name = "Admin" },
        new Role { Id = RoleIds.Manager, Code = RoleCodes.Manager, Name = "Quản lý" },
        new Role { Id = RoleIds.Driver, Code = RoleCodes.Driver, Name = "Tài xế" },
        new Role { Id = RoleIds.Passenger, Code = RoleCodes.Passenger, Name = "Hành khách" },
    ];

    /// <summary>Một tuyến mẫu cùng thứ tự trạm và bảng giá — chỉ dùng bên trong seeder.</summary>
    private sealed record TuyenMau(
        string Code,
        string Name,
        string Origin,
        string Destination,
        (string StopName, decimal DistanceKm)[] Stops,
        (PassengerType PassengerType, decimal Price)[] Fares);
}

/// <summary>Kết quả một lượt seed — console in ra cho người chạy, test khẳng định theo.</summary>
public sealed record SeedResult(
    bool ResetPerformed,
    int AccountsCreated,
    int AccountsSkipped,
    int StopsCreated,
    int RoutesCreated,
    int BusesCreated,
    int TripsCreated);
