using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="ITripGenerationService"/> — phần ruột của job sinh chuyến tự động
/// (US 13 "Lập lịch trình", task của Nguyễn Duy Kiên).
///
/// ── Vì sao NHÂN BẢN lịch trình thay vì đọc một bảng mẫu ──
/// Quy ước A8.3 chốt KHÔNG có bảng <c>Schedules</c>: lịch trình định kỳ không lưu thành bảng mẫu,
/// chỉ có <c>Trips</c> + API sinh chuyến hàng loạt (<see cref="RouteTripsService.GenerateAsync"/>).
/// Hệ quả là "tuyến này chạy 05:00–21:00, tần suất 20 phút" chỉ là ba tham số của một lời gọi API:
/// sinh xong N dòng Trips rồi tham số biến mất, CSDL không giữ lại chỗ nào để job đọc.
///
/// Nên nguồn sự thật duy nhất còn lại của job là chính những dòng <c>Trips</c> đã sinh: NGÀY GẦN
/// NHẤT đã có chuyến của một tuyến chính là lịch trình đang chạy của tuyến đó. Job lấy ngày ấy làm
/// MẪU rồi nhân bản sang những ngày còn trống phía trước. Đúng tinh thần A8.3, không mở lại một
/// quyết định đã chốt và không cần migrate thêm bảng nào.
///
/// ── Hai giới hạn của thiết kế này, nói thẳng để không ai tưởng job làm được hơn thực tế ──
/// 1. Tuyến CHƯA TỪNG có chuyến nào thì job không có gì để nhân bản và đứng im. Quản lý phải gọi
///    <c>POST /routes/{routeId}/trips/generate</c> một lần để "gieo" lịch trình đầu tiên; từ đó
///    job tự lo những ngày sau. Đây là cái giá đã được chấp nhận khi A8.3 bỏ bảng mẫu. Cùng lý do,
///    nếu lần gieo đầu tiên chỉ phủ những ngày TƯƠNG LAI (hôm nay chưa có chuyến nào) thì job chờ
///    tới khi ngày đầu tiên đó trôi qua mới lấy được nó làm mẫu.
/// 2. Tuyến muốn NGỪNG chạy phải đặt <c>Routes.Status = Inactive</c> — đó là công tắc duy nhất,
///    và job tôn trọng nó. Không có công tắc ấy thì job sẽ mãi dựng lại một tuyến đã bỏ. Nghỉ TẠM
///    vài ngày (nghỉ lễ) thì không cần đụng vào tuyến: huỷ chuyến của đúng những ngày đó, vì ngày
///    đã có chuyến — kể cả chuyến đã huỷ — là ngày job không đụng tới.
///
/// ── Một lượt chạy làm gì ──
/// Với mỗi tuyến đang khai thác có lịch sử chuyến: lấy ngày gần nhất (theo giờ Việt Nam) đã có
/// chuyến chưa huỷ làm mẫu, rồi với từng ngày TRỐNG trong tầm <see cref="HorizonDays"/> ngày tới
/// thì chép các chuyến mẫu sang, giữ nguyên giờ địa phương.
/// </summary>
public class TripGenerationService : ITripGenerationService
{
    /// <summary>
    /// Múi giờ Việt Nam. Cố định UTC+7 quanh năm — Việt Nam không có giờ mùa hè — nên một hằng số
    /// là đủ và đúng; không cần <c>TimeZoneInfo</c> (máy chủ deploy chạy UTC, hỏi múi giờ hệ thống
    /// là hỏi nhầm người) cũng không cần khoá cấu hình.
    ///
    /// Vì sao job phải biết múi giờ trong khi phần còn lại của backend chỉ nói UTC: "ngày" của một
    /// thời gian biểu xe buýt là ngày trên lịch của hành khách, không phải ngày UTC. Một chuyến
    /// 05:00 giờ Việt Nam nằm ở 22:00 UTC NGÀY HÔM TRƯỚC. Gộp theo ngày UTC sẽ cắt đôi một ngày
    /// khai thác — nhóm chuyến sáng sớm rơi sang "ngày" UTC trước đó — và job sẽ nhân bản sai nhóm.
    /// Đầu vào của hợp đồng API cũng nghĩ theo giờ Việt Nam: màn hình gửi from/to kèm +07:00.
    /// </summary>
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);

    /// <summary>
    /// Số ngày trống tối đa phía trước mà mỗi lượt chạy lấp cho đầy. 7 ngày là mức dung hoà: đủ để
    /// app có chết vài ngày rồi khởi động lại thì hành khách vẫn thấy chuyến để đặt vé, mà không
    /// sinh sẵn quá nhiều dòng Trips tương lai cho những ngày còn có thể đổi lịch.
    /// </summary>
    private const int HorizonDays = 7;

    private readonly AppDbContext _db;

    public TripGenerationService(AppDbContext db) => _db = db;

    public async Task<TripGenerationResult> GenerateUpcomingAsync(
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        var homNay = LocalDay(nowUtc);
        var ngayDau = homNay.AddDays(1);
        var ngayCuoi = homNay.AddDays(HorizonDays);

        // ── Tuyến đang khai thác. Đọc ID thành danh sách rồi lọc trong bộ nhớ thay vì lọc
        //    t.Route.Status ngay trong câu LINQ: lọc theo navigation là một phép JOIN, mà provider
        //    InMemory của bộ test không dịch được phép JOIN đó (navigation chỉ được fix-up khi có
        //    Include, còn truy vấn này cố ý chạy AsNoTracking). ──
        var tuyenDangChay = await _db.Routes.AsNoTracking()
            .Where(r => r.Status == RouteStatus.Active)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        if (tuyenDangChay.Count == 0)
        {
            return TripGenerationResult.Empty;
        }

        // ── Chuyến cuối cùng đã tới giờ chạy của mỗi tuyến, bỏ qua chuyến đã huỷ: ngày chứa nó là
        //    ngày gần nhất tuyến này thật sự có lịch, tức là mẫu để nhân bản. Một truy vấn gộp cho
        //    mọi tuyến (mỗi tuyến một dòng) chứ không phải mỗi tuyến một lượt hỏi CSDL.
        //    Cố ý KHÔNG chặn trên "mẫu phải mới trong vòng N ngày": một tuyến vắng chuyến dài ngày
        //    vẫn là tuyến đang khai thác, và công tắc để ngừng hẳn là Routes.Status (giới hạn (2)). ──
        var chuyenCuoi = await _db.Trips.AsNoTracking()
            .Where(t => t.Status != TripStatus.Cancelled && t.DepartureTime <= nowUtc)
            .GroupBy(t => t.RouteId)
            .Select(g => new { RouteId = g.Key, DepartureTime = g.Max(t => t.DepartureTime) })
            .ToListAsync(cancellationToken);

        var tuyenCoMau = chuyenCuoi
            .Where(x => tuyenDangChay.Contains(x.RouteId))
            .Select(x => new { x.RouteId, NgayMau = LocalDay(x.DepartureTime) })
            .ToList();

        if (tuyenCoMau.Count == 0)
        {
            // Chưa tuyến nào từng chạy chuyến nào — không có gì để nhân bản (giới hạn (1)).
            return TripGenerationResult.Empty;
        }

        // ── Chuyến mẫu chỉ dùng được nếu XE của nó còn khai thác. Cùng luật với API sinh chuyến
        //    (RouteTripsService từ chối xe không Active): job không được tạo ra những chuyến mà
        //    chính API của dự án sẽ từ chối. ──
        var xeDangKhaiThac = await _db.Buses.AsNoTracking()
            .Where(b => b.Status == BusStatus.Active)
            .Select(b => b.Id)
            .ToListAsync(cancellationToken);

        // ── Ngày nào đã có chuyến — bất kể trạng thái — thì job KHÔNG đụng vào. Vừa là tấm chắn
        //    chống sinh trùng khi job chạy lặp lại, vừa là cách tôn trọng thao tác tay của quản lý:
        //    một ngày đã được lập trình là ngày đã có chủ. Kể cả ngày bị huỷ HẾT chuyến để nghỉ lễ
        //    cũng tính là có chủ — nếu bỏ qua chuyến đã huỷ ở đây thì job sẽ dựng lại đúng cái ngày
        //    mà quản lý vừa cho nghỉ.
        //    Hỏi theo từng ngày (7 truy vấn, mỗi truy vấn chỉ trả về danh sách RouteId) chứ không
        //    kéo cả tuần chuyến về đếm — mỗi ngày tối đa 200 chuyến/tuyến, kéo hết là hàng nghìn
        //    dòng mỗi lượt chạy trong khi thứ cần biết chỉ là "ngày này có ai chưa". ──
        var ngayDaCoChuyen = new HashSet<(Guid RouteId, DateTime Ngay)>();
        var routeIdsCoMau = tuyenCoMau.Select(x => x.RouteId).ToList();

        for (var ngay = ngayDau; ngay <= ngayCuoi; ngay = ngay.AddDays(1))
        {
            var dauNgay = LocalDayStartUtc(ngay);
            var cuoiNgay = dauNgay.AddDays(1);

            // Lọc kèm RouteId để câu này đi đúng chỉ mục (RouteId, DepartureTime) của bảng Trips
            // (A6) — chỉ lọc theo DepartureTime là quét cả bảng, mà đây là 7 truy vấn mỗi giờ,
            // chạy mãi. Cũng chỉ hỏi những tuyến thật sự có mẫu: tuyến không có mẫu thì có chuyến
            // hay không cũng đằng nào chẳng sinh được gì.
            var tuyenDaCoChuyen = await _db.Trips.AsNoTracking()
                .Where(t => routeIdsCoMau.Contains(t.RouteId)
                    && t.DepartureTime >= dauNgay && t.DepartureTime < cuoiNgay)
                .Select(t => t.RouteId)
                .Distinct()
                .ToListAsync(cancellationToken);

            foreach (var routeId in tuyenDaCoChuyen)
            {
                ngayDaCoChuyen.Add((routeId, ngay));
            }
        }

        var soChuyenSinh = 0;
        var soTuyenSinh = 0;
        var soChuyenBoQua = 0;

        // Gom tuyến theo NGÀY MẪU của chúng để mỗi ngày mẫu chỉ phải đọc chuyến một lần. Thực tế
        // gần như mọi tuyến dùng chung một ngày mẫu (hôm nay), nên đây thường chỉ là một truy vấn.
        foreach (var nhomTheoNgayMau in tuyenCoMau.GroupBy(x => x.NgayMau))
        {
            var ngayMau = nhomTheoNgayMau.Key;
            var routeIds = nhomTheoNgayMau.Select(x => x.RouteId).ToList();

            var dauNgayMau = LocalDayStartUtc(ngayMau);
            var cuoiNgayMau = dauNgayMau.AddDays(1);

            var chuyenMau = await _db.Trips.AsNoTracking()
                .Where(t => t.Status != TripStatus.Cancelled
                    && t.DepartureTime >= dauNgayMau && t.DepartureTime < cuoiNgayMau
                    && routeIds.Contains(t.RouteId))
                .OrderBy(t => t.DepartureTime)
                .ToListAsync(cancellationToken);

            foreach (var routeId in routeIds)
            {
                var mauCuaNgay = chuyenMau.Where(t => t.RouteId == routeId).ToList();
                var mauDungDuoc = mauCuaNgay.Where(t => xeDangKhaiThac.Contains(t.BusId)).ToList();
                var soBoQua = mauCuaNgay.Count - mauDungDuoc.Count;

                if (mauDungDuoc.Count == 0)
                {
                    // Tuyến chỉ có chuyến của xe đã rút khỏi đội (hoặc ngày mẫu rỗng) — không còn
                    // gì để nhân bản. Vẫn báo số chuyến bị bỏ để log lên tiếng, vì đây là tuyến
                    // đứng im chứ không phải tuyến không có việc.
                    soChuyenBoQua += soBoQua;
                    continue;
                }

                var soNgayDaLap = 0;

                for (var ngay = ngayDau; ngay <= ngayCuoi; ngay = ngay.AddDays(1))
                {
                    if (ngayDaCoChuyen.Contains((routeId, ngay)))
                    {
                        continue;
                    }

                    // Việt Nam không có giờ mùa hè nên cộng đúng số NGÀY LỊCH vào mốc UTC là giữ
                    // nguyên giờ địa phương của chuyến: chuyến 05:00 giờ Việt Nam của ngày mẫu
                    // thành 05:00 giờ Việt Nam của ngày đích, không cần đụng tới phép đổi múi giờ.
                    var lechNgay = (ngay - ngayMau).Days;

                    foreach (var chuyen in mauDungDuoc)
                    {
                        _db.Trips.Add(new Trip
                        {
                            RouteId = routeId,
                            BusId = chuyen.BusId,
                            DepartureTime = chuyen.DepartureTime.AddDays(lechNgay),
                            ArrivalTime = chuyen.ArrivalTime?.AddDays(lechNgay),
                            // Ghi lại chính mốc đã dùng để xét "hôm nay là ngày nào" chứ không gọi
                            // DateTime.UtcNow lần nữa — cùng lý do MonthlyPassExpiryService.
                            CreatedAt = nowUtc,
                            // Status để mặc định Scheduled, DriverId để trống: chuyến sinh ra
                            // TRƯỚC khi điều xe, đó chính là nghiệp vụ của US 14 (xem chú thích
                            // DriverId ở Trip). Các cột vị trí cũng để trống — chuyến tương lai
                            // chưa ở đâu cả.
                        });
                    }

                    soChuyenSinh += mauDungDuoc.Count;
                    soNgayDaLap++;
                }

                if (soNgayDaLap > 0)
                {
                    soTuyenSinh++;

                    // Chỉ cộng số chuyến bị bỏ khi lượt này CÓ sinh chuyến cho tuyến đó, để log
                    // không nhắc lại cùng một xe hỏng ở mọi lượt chạy rảnh — cùng tinh thần "chỉ
                    // ghi log khi có việc" của MonthlyPassExpiryBackgroundService.
                    soChuyenBoQua += soBoQua;
                }
            }
        }

        // Không có gì mới thì không mở giao dịch — lượt chạy rảnh (phần lớn các lượt) không được
        // ghi gì xuống CSDL.
        if (soChuyenSinh > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return new TripGenerationResult(soChuyenSinh, soTuyenSinh, soChuyenBoQua);
    }

    /// <summary>Ngày trên lịch Việt Nam (nửa đêm địa phương) chứa mốc UTC truyền vào.</summary>
    private static DateTime LocalDay(DateTime utc) => utc.Add(VietnamOffset).Date;

    /// <summary>
    /// Mốc UTC của nửa đêm giờ Việt Nam bắt đầu một ngày trên lịch. Ghi Kind tường minh vì
    /// Npgsql từ chối ghi một <c>DateTime</c> Kind Unspecified vào cột <c>timestamptz</c>.
    /// </summary>
    private static DateTime LocalDayStartUtc(DateTime localDay)
        => DateTime.SpecifyKind(localDay - VietnamOffset, DateTimeKind.Utc);
}
