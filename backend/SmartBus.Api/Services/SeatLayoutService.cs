using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.SeatLayouts;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// CRUD sơ đồ ghế theo loại xe + sinh ghế thật cho xe — story 2
/// "Cấu hình sơ đồ ghế theo loại xe (số tầng, số ghế, ghế VIP)", Phùng Duy Hoàng.
/// Hợp đồng đầy đủ ở mục "Sơ đồ ghế — /seat-layouts" của docs/api-contract.md.
///
/// Sơ đồ là hình học của một LOẠI xe, không phải của từng xe: SeatLayout.BusType khớp
/// Buses.BusType bằng so chuỗi (không có khoá ngoại — xem docs/26-csdl-so-do-ghe.md), còn ghế
/// thật thuộc TỪNG xe (bảng Seats). Vì vậy lưu sơ đồ kéo theo sinh ghế:
///
/// - Xe cùng loại đang có 0 ghế thì được sinh đủ lưới ngay trong lượt lưu này.
/// - Xe đã có ghế thì KHÔNG đụng tới — ghế của nó có thể đã bị giữ chỗ hoặc bán.
/// - Sửa hình học mà ghế của các xe đó đã bị SeatHolds/Tickets tham chiếu thì từ chối (409);
///   chưa ai đụng thì xoá ghế cũ của các xe đó rồi sinh lại theo lưới mới, tất cả trong MỘT
///   SaveChanges — EF Core sắp lệnh theo chỉ mục unique (BusId, SeatNumber) nên xoá chạy trước
///   chèn, và cả lượt lưu nằm trong một transaction.
///
/// Ba hàm đánh mã ghế dưới cuối file chép từ SampleDataSeeder — dự án chọn lối chép bản thay vì
/// tách dùng chung (cùng lý do đã ghi ở FaresController.ValidationError), sửa một bên thì nhớ
/// sửa bên kia kẻo xe sinh bằng seeder và xe sinh qua API lệch mã nhau.
/// </summary>
public class SeatLayoutService : ISeatLayoutService
{
    private const string NotFoundMessage = "Không tìm thấy sơ đồ ghế";
    private const string BusTypeExistsMessage = "Loại xe này đã có sơ đồ ghế";
    private const string BusTypeLengthMessage = "Loại xe phải từ 2 đến 50 ký tự";
    private const string FloorsInvalidMessage = "Số tầng phải là 1 hoặc 2";
    private const string RowsInvalidMessage = "Số hàng mỗi tầng phải từ 1 đến 26";
    private const string ColumnsInvalidMessage = "Số ghế mỗi hàng phải từ 1 đến 10";
    private const string TooManySeatsMessage = "Lưới ghế vượt quá 200 ghế — quá trần sức chứa của xe";
    private const string LayoutInUseMessage =
        "Sơ đồ đang được dùng để giữ chỗ hoặc bán vé — không sửa hình học được nữa.";

    // Biên lưới ghế. Trần màn hình chặt hơn (13 hàng, 6 cột) nhưng hợp đồng API rộng hơn để
    // không phải sửa hợp đồng khi màn hình nới giới hạn; 26 hàng = 26 chữ cái A–Z dùng đánh mã ghế.
    private const int MaxFloors = 2;
    private const int MaxRowsPerFloor = 26;
    private const int MaxColumnsPerRow = 10;
    private const int MaxTotalSeats = 200;

    private readonly AppDbContext _db;

    public SeatLayoutService(AppDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<ServiceResult<IReadOnlyList<SeatLayoutResponse>>> ListAsync(
        ListSeatLayoutsRequest request,
        CancellationToken cancellationToken = default)
    {
        // Chỉ đọc nên AsNoTracking; lọc theo loại xe không phân biệt hoa thường, cùng lối
        // lọc biển số ở BusService. Không phân trang: số sơ đồ bằng số loại xe trong đội.
        var query = _db.SeatLayouts.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var keyword = request.Search.Trim().ToLower();
            query = query.Where(l => l.BusType.ToLower().Contains(keyword));
        }

        var danhSach = await query.OrderBy(l => l.BusType).ToListAsync(cancellationToken);

        return ServiceResult<IReadOnlyList<SeatLayoutResponse>>.Ok(
            danhSach.Select(l => ToResponse(l)).ToList());
    }

    /// <inheritdoc />
    public async Task<ServiceResult<SeatLayoutResponse>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var soDo = await _db.SeatLayouts
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == id, cancellationToken);

        return soDo is null
            ? ServiceResult<SeatLayoutResponse>.NotFound(NotFoundMessage)
            : ServiceResult<SeatLayoutResponse>.Ok(ToResponse(soDo));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<SeatLayoutResponse>> CreateAsync(
        CreateSeatLayoutRequest request,
        CancellationToken cancellationToken = default)
    {
        var busType = request.BusType?.Trim() ?? string.Empty;

        // Phòng khi service bị gọi thẳng (test, job nền) — Controller chặn bằng ModelState rồi.
        if (busType.Length is < 2 or > 50)
        {
            return InvalidField("busType", BusTypeLengthMessage);
        }

        if (await _db.SeatLayouts.AnyAsync(l => l.BusType == busType, cancellationToken))
        {
            return InvalidField("busType", BusTypeExistsMessage);
        }

        if (KiemTraHinhHoc(request.NumberOfFloors, request.RowsPerFloor, request.ColumnsPerRow) is { } loiHinhHoc)
        {
            return loiHinhHoc.Field is null
                ? InvalidMessage(loiHinhHoc.Message)
                : InvalidField(loiHinhHoc.Field, loiHinhHoc.Message);
        }

        if (ThuThapViTriVip(
                request.VipSeatPositions,
                request.NumberOfFloors,
                request.RowsPerFloor,
                request.ColumnsPerRow,
                out var vipChuoi) is { } loiVip)
        {
            return InvalidField("vipSeatPositions", loiVip);
        }

        var tongSoGhe = request.NumberOfFloors * request.RowsPerFloor * request.ColumnsPerRow;

        if (await KiemTraSucChuaAsync(busType, tongSoGhe, cancellationToken) is { } loiSucChua)
        {
            return InvalidMessage(loiSucChua);
        }

        var soDo = new SeatLayout
        {
            BusType = busType,
            NumberOfFloors = request.NumberOfFloors,
            RowsPerFloor = request.RowsPerFloor,
            ColumnsPerRow = request.ColumnsPerRow,
            TotalSeats = tongSoGhe,
            VipSeatPositions = vipChuoi,
        };
        _db.SeatLayouts.Add(soDo);

        var (xeCungLoai, gheHienCo) = await XeVaGheCuaLoaiAsync(busType, cancellationToken);
        var soXeSinhGhe = SinhGheChoTatCaXe(soDo, xeCungLoai, gheHienCo.Select(s => s.BusId).ToHashSet());

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Ca đua: hai quản lý cùng lưu một loại xe. Chỉ mục unique là chốt chặn cuối —
            // trả đúng lỗi gắn ô nhập như pre-check ở trên, cùng lối biển số xe ở /buses
            // (BusService cũng trả lỗi ô nhập ở lớp bắt DbUpdateException chứ không phải 409).
            return InvalidField("busType", BusTypeExistsMessage);
        }

        return ServiceResult<SeatLayoutResponse>.Ok(ToResponse(soDo, soXeSinhGhe));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<SeatLayoutResponse>> UpdateAsync(
        Guid id,
        UpdateSeatLayoutRequest request,
        CancellationToken cancellationToken = default)
    {
        var soDo = await _db.SeatLayouts.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);

        if (soDo is null)
        {
            return ServiceResult<SeatLayoutResponse>.NotFound(NotFoundMessage);
        }

        if (KiemTraHinhHoc(request.NumberOfFloors, request.RowsPerFloor, request.ColumnsPerRow) is { } loiHinhHoc)
        {
            return loiHinhHoc.Field is null
                ? InvalidMessage(loiHinhHoc.Message)
                : InvalidField(loiHinhHoc.Field, loiHinhHoc.Message);
        }

        if (ThuThapViTriVip(
                request.VipSeatPositions,
                request.NumberOfFloors,
                request.RowsPerFloor,
                request.ColumnsPerRow,
                out var vipChuoi) is { } loiVip)
        {
            return InvalidField("vipSeatPositions", loiVip);
        }

        var tongSoGhe = request.NumberOfFloors * request.RowsPerFloor * request.ColumnsPerRow;

        if (await KiemTraSucChuaAsync(soDo.BusType, tongSoGhe, cancellationToken) is { } loiSucChua)
        {
            return InvalidMessage(loiSucChua);
        }

        var (xeCungLoai, gheHienCo) = await XeVaGheCuaLoaiAsync(soDo.BusType, cancellationToken);

        var doiHinhHoc = soDo.NumberOfFloors != request.NumberOfFloors
            || soDo.RowsPerFloor != request.RowsPerFloor
            || soDo.ColumnsPerRow != request.ColumnsPerRow
            || !CungTapVip(soDo.VipSeatPositions, vipChuoi);

        if (doiHinhHoc)
        {
            // Ghế cũ của các xe ĐANG theo sơ đồ này. Xe cùng loại nhưng mang ghế từ sơ đồ khác
            // (đổi loại xe ở PUT /buses/{id} không xoá ghế cũ — cảnh báo ở api-contract.md) thì
            // để nguyên, không xoá hộ.
            var gheCuaSoDo = gheHienCo.Where(s => s.SeatLayoutId == soDo.Id).ToList();

            if (gheCuaSoDo.Count > 0 &&
                await CoGheDangBiDungAsync(gheCuaSoDo.Select(s => s.Id).ToList(), cancellationToken))
            {
                return ServiceResult<SeatLayoutResponse>.Conflict(LayoutInUseMessage);
            }

            // Xoá và chèn mới nằm chung một SaveChanges: EF Core sắp xếp lệnh theo giá trị của
            // chỉ mục unique nên lệnh xoá (BusId, SeatNumber) cũ chạy trước lệnh chèn mã trùng,
            // và mọi lệnh nằm trong một transaction — không có trạng thái nửa vời.
            _db.Seats.RemoveRange(gheCuaSoDo);

            // Bỏ khỏi danh sách "đang có ghế": ghế sắp bị xoá trong chính lượt lưu này, còn
            // hỏi lại CSDL thì chúng vẫn nằm đó nên xe sẽ bị bỏ qua nhầm.
            gheHienCo = gheHienCo.Except(gheCuaSoDo).ToList();
        }

        soDo.NumberOfFloors = request.NumberOfFloors;
        soDo.RowsPerFloor = request.RowsPerFloor;
        soDo.ColumnsPerRow = request.ColumnsPerRow;
        soDo.TotalSeats = tongSoGhe;
        soDo.VipSeatPositions = vipChuoi;
        soDo.UpdatedAt = DateTime.UtcNow;

        var soXeSinhGhe = SinhGheChoTatCaXe(soDo, xeCungLoai, gheHienCo.Select(s => s.BusId).ToHashSet());

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Ca đua với một lượt giữ chỗ/bán vé vừa chèn vào giữa: khoá ngoại Restrict của
            // SeatHolds/Tickets chặn ở tầng CSDL — báo đúng câu 409 như pre-check ở trên.
            return ServiceResult<SeatLayoutResponse>.Conflict(LayoutInUseMessage);
        }

        return ServiceResult<SeatLayoutResponse>.Ok(ToResponse(soDo, soXeSinhGhe));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<bool>> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var soDo = await _db.SeatLayouts.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);

        if (soDo is null)
        {
            return ServiceResult<bool>.NotFound(NotFoundMessage);
        }

        // Ghế là dữ liệu nghiệp vụ (có thể đã gắn với vé) — còn ghế thì không xoá sơ đồ, cùng
        // tinh thần khoá ngoại Restrict của quy ước A5. Chặn trước để báo được số lượng cụ thể.
        var soGhe = await _db.Seats.CountAsync(s => s.SeatLayoutId == id, cancellationToken);

        if (soGhe > 0)
        {
            return ServiceResult<bool>.Conflict(
                $"Sơ đồ còn {soGhe} ghế của các xe đang dùng — chuyển các xe đó sang loại xe khác trước");
        }

        _db.SeatLayouts.Remove(soDo);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Ca đua: ghế vừa được sinh xen vào giữa lúc kiểm tra và lúc xoá.
            return ServiceResult<bool>.Conflict("Sơ đồ vừa được dùng để sinh ghế cho xe — không xoá được");
        }

        return ServiceResult<bool>.Ok(true);
    }

    /// <summary>
    /// Xe cùng loại và toàn bộ ghế hiện có của chúng. Lấy một lượt để phần sau khỏi hỏi lại
    /// CSDL: lúc SỬA còn phải trừ đi những ghế sắp xoá (chưa SaveChanges nên hỏi lại vẫn thấy).
    /// </summary>
    private async Task<(List<Bus> XeCungLoai, List<Seat> Ghe)> XeVaGheCuaLoaiAsync(
        string busType,
        CancellationToken cancellationToken)
    {
        var xeCungLoai = await _db.Buses
            .Where(b => b.BusType == busType)
            .ToListAsync(cancellationToken);

        if (xeCungLoai.Count == 0)
        {
            return (xeCungLoai, new List<Seat>());
        }

        var idXeCungLoai = xeCungLoai.Select(b => b.Id).ToList();
        var ghe = await _db.Seats
            .Where(s => idXeCungLoai.Contains(s.BusId))
            .ToListAsync(cancellationToken);

        return (xeCungLoai, ghe);
    }

    /// <summary>
    /// Sinh ghế cho mọi xe trong <paramref name="xeCungLoai"/> chưa có ghế, trả về số xe đã sinh.
    /// <paramref name="idXeDaCoGhe"/> có thể đã trừ sẵn những xe vừa bị xoá ghế trong cùng lượt lưu.
    /// </summary>
    private int SinhGheChoTatCaXe(SeatLayout soDo, IReadOnlyList<Bus> xeCungLoai, IReadOnlySet<Guid> idXeDaCoGhe)
    {
        var dem = 0;

        foreach (var bus in xeCungLoai)
        {
            if (idXeDaCoGhe.Contains(bus.Id))
            {
                continue;
            }

            _db.Seats.AddRange(SinhGhe(bus, soDo));
            dem++;
        }

        return dem;
    }

    /// <summary>Ghế của các xe đó có đang bị giữ chỗ hoặc đã bán vé không.</summary>
    private async Task<bool> CoGheDangBiDungAsync(List<Guid> idGhe, CancellationToken cancellationToken)
    {
        if (idGhe.Count == 0)
        {
            return false;
        }

        // Ticket.SeatId là Guid (không nullable) nên không cần lọc null; hai bảng đều Restrict.
        return await _db.SeatHolds.AnyAsync(h => idGhe.Contains(h.SeatId), cancellationToken)
            || await _db.Tickets.AnyAsync(t => idGhe.Contains(t.SeatId), cancellationToken);
    }

    /// <summary>
    /// Kiểm tra hình học của lưới gửi lên: null nếu hợp lệ, ngược lại là (trường, câu thông báo).
    /// Field null = lỗi không gắn với ô nhập nào (cả lưới vượt trần) — Controller trả 400 chỉ có message.
    /// </summary>
    private static (string? Field, string Message)? KiemTraHinhHoc(int soTang, int soHang, int soCot)
    {
        if (soTang is < 1 or > MaxFloors)
        {
            return ("numberOfFloors", FloorsInvalidMessage);
        }

        if (soHang is < 1 or > MaxRowsPerFloor)
        {
            return ("rowsPerFloor", RowsInvalidMessage);
        }

        if (soCot is < 1 or > MaxColumnsPerRow)
        {
            return ("columnsPerRow", ColumnsInvalidMessage);
        }

        // Ba biên trên đã chặn nên tích lớn nhất là 2 × 26 × 10 = 520, không tràn int.
        return soTang * soHang * soCot > MaxTotalSeats ? (null, TooManySeatsMessage) : null;
    }

    /// <summary>
    /// Sức chứa của MỌI xe cùng loại phải khớp số ghế của lưới — lệch thì trả câu thông báo
    /// nêu đích danh xe đầu tiên lệch (null nếu khớp hoặc chưa có xe nào).
    /// </summary>
    private async Task<string?> KiemTraSucChuaAsync(string busType, int tongSoGhe, CancellationToken cancellationToken)
    {
        var xeLech = await _db.Buses
            .AsNoTracking()
            .Where(b => b.BusType == busType && b.Capacity != tongSoGhe)
            .Select(b => new { b.LicensePlate, b.Capacity })
            .FirstOrDefaultAsync(cancellationToken);

        return xeLech is null
            ? null
            : $"Xe {xeLech.LicensePlate} có sức chứa {xeLech.Capacity} nhưng lưới ghế cho ra {tongSoGhe} ghế";
    }

    /// <summary>
    /// Kiểm tra và chuẩn hoá danh sách vị trí VIP. Trả câu lỗi (null nếu hợp lệ) và chuỗi đã nối
    /// ';' để lưu xuống cột <c>SeatLayout.VipSeatPositions</c>.
    ///
    /// Toạ độ được chuẩn hoá qua <see cref="ViTri"/> — "01-1-1" lưu thành "1-1-1" — để khoá lúc
    /// tra cứu VIP trong <see cref="SinhGhe"/> luôn khớp, không phụ thuộc người gõ.
    /// </summary>
    private static string? ThuThapViTriVip(
        IReadOnlyList<string>? viTriVip,
        int soTang,
        int soHang,
        int soCot,
        out string vipChuoi)
    {
        var daGap = new HashSet<string>(StringComparer.Ordinal);
        var chuanHoa = new List<string>();

        foreach (var viTri in viTriVip ?? new List<string>())
        {
            var o = viTri?.Trim() ?? string.Empty;

            if (!TryChuanHoaViTri(o, soTang, soHang, soCot, out var oChuanHoa))
            {
                vipChuoi = string.Empty;
                return $"Vị trí ghế VIP \"{o}\" phải có dạng \"tầng-hàng-cột\" nằm trong lưới ghế";
            }

            if (!daGap.Add(oChuanHoa))
            {
                vipChuoi = string.Empty;
                return $"Vị trí ghế VIP \"{oChuanHoa}\" bị lặp";
            }

            chuanHoa.Add(oChuanHoa);
        }

        vipChuoi = string.Join(';', chuanHoa);
        return null;
    }

    /// <summary>Tách "tầng-hàng-cột" và kiểm tra nằm trong lưới; ra khoá chuẩn hoá qua <see cref="ViTri"/>.</summary>
    private static bool TryChuanHoaViTri(string viTri, int soTang, int soHang, int soCot, out string chuanHoa)
    {
        chuanHoa = string.Empty;
        var phan = viTri.Split('-');

        if (phan.Length != 3
            || !int.TryParse(phan[0], out var tang) || tang < 1 || tang > soTang
            || !int.TryParse(phan[1], out var hang) || hang < 1 || hang > soHang
            || !int.TryParse(phan[2], out var cot) || cot < 1 || cot > soCot)
        {
            return false;
        }

        chuanHoa = ViTri(tang, hang, cot);
        return true;
    }

    /// <summary>Hai tập vị trí VIP có giống nhau không (thứ tự không quan trọng).</summary>
    private static bool CungTapVip(string vipCu, string vipMoi) =>
        ViTriVipCua(vipCu).SetEquals(ViTriVipCua(vipMoi));

    private static SeatLayoutResponse ToResponse(SeatLayout soDo, int busesSeated = 0) => new()
    {
        Id = soDo.Id,
        BusType = soDo.BusType,
        NumberOfFloors = soDo.NumberOfFloors,
        RowsPerFloor = soDo.RowsPerFloor,
        ColumnsPerRow = soDo.ColumnsPerRow,
        TotalSeats = soDo.TotalSeats,
        VipSeatPositions = TachViTriVip(soDo.VipSeatPositions),
        BusesSeated = busesSeated,
        CreatedAt = soDo.CreatedAt,
        UpdatedAt = soDo.UpdatedAt,
    };

    // ---------------------------------------------------------------------------------------
    // Sinh ghế — chép từ SampleDataSeeder.SinhGhe / ViTriVipCua / ViTri / MaGhe.
    // Sửa một bên phải sửa bên kia: mã ghế do seeder sinh (ghế cũ) và do service sinh (ghế mới)
    // phải giống hệt nhau, không thì cùng một xe có hai kiểu mã.
    // ---------------------------------------------------------------------------------------

    /// <summary>Sinh đủ lưới ghế của sơ đồ cho MỘT xe, chưa gắn vào DbContext.</summary>
    private static IEnumerable<Seat> SinhGhe(Bus bus, SeatLayout soDo)
    {
        var viTriVip = ViTriVipCua(soDo.VipSeatPositions);

        for (var tang = 1; tang <= soDo.NumberOfFloors; tang++)
        {
            for (var hang = 1; hang <= soDo.RowsPerFloor; hang++)
            {
                for (var cot = 1; cot <= soDo.ColumnsPerRow; cot++)
                {
                    yield return new Seat
                    {
                        BusId = bus.Id,
                        SeatLayoutId = soDo.Id,
                        Floor = tang,
                        RowIndex = hang,
                        ColumnIndex = cot,
                        SeatNumber = MaGhe(soDo.NumberOfFloors, tang, hang, cot),
                        SeatType = viTriVip.Contains(ViTri(tang, hang, cot)) ? SeatType.Vip : SeatType.Standard,
                    };
                }
            }
        }
    }

    /// <summary>Tách cột VipSeatPositions thành mảng toạ độ, giữ nguyên thứ tự đã lưu.</summary>
    private static string[] TachViTriVip(string vipSeatPositions) =>
        vipSeatPositions.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Chép từ SampleDataSeeder.ViTriVipCua — so Ordinal cho khớp khoá đã lưu.</summary>
    private static HashSet<string> ViTriVipCua(string vipSeatPositions) =>
        TachViTriVip(vipSeatPositions).ToHashSet(StringComparer.Ordinal);

    /// <summary>Chép từ SampleDataSeeder.ViTri — khoá toạ độ lưu ở cột VipSeatPositions, đếm từ 1.</summary>
    private static string ViTri(int floor, int row, int column) => $"{floor}-{row}-{column}";

    /// <summary>
    /// Chép từ SampleDataSeeder.MaGhe — hàng A–Z + số cột; xe hai tầng thêm tiền tố T{tầng}- vì
    /// mã ghế chỉ độc nhất trong một xe khi khác tầng (chỉ mục unique (BusId, SeatNumber)).
    /// </summary>
    private static string MaGhe(int numberOfFloors, int floor, int row, int column)
    {
        var ma = $"{(char)('A' + row - 1)}{column}";

        return numberOfFloors > 1 ? $"T{floor}-{ma}" : ma;
    }

    /// <summary>Lỗi 400 gắn vào một ô nhập cụ thể — hợp đồng lỗi chung { message, errors }.</summary>
    private static ServiceResult<SeatLayoutResponse> InvalidField(string field, string message) =>
        ServiceResult<SeatLayoutResponse>.Invalid(
            message,
            new Dictionary<string, string[]> { [field] = [message] });

    /// <summary>Lỗi 400 không gắn ô nhập nào (cả lưới sai, không phải một ô) — errors rỗng.</summary>
    private static ServiceResult<SeatLayoutResponse> InvalidMessage(string message) =>
        ServiceResult<SeatLayoutResponse>.Invalid(message, new Dictionary<string, string[]>());
}
