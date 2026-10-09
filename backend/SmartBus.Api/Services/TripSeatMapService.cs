using Microsoft.EntityFrameworkCore;
using SmartBus.Api.Data;
using SmartBus.Api.Dtos.Trips;
using SmartBus.Api.Entities;

namespace SmartBus.Api.Services;

/// <summary>
/// Cài đặt <see cref="ITripSeatMapService"/> — hợp đồng đầy đủ ở mục "GET /trips/{id}/seats" của
/// docs/api-contract.md.
///
/// Chỉ đọc: không có thao tác ghi nào ở đây, nên cũng không có <c>SaveChangesAsync</c> — cùng lối
/// <see cref="TripService"/>. Trạng thái ghế (Available/Held/Paid) là SUY RA theo chuyến từ
/// <c>SeatHolds</c> và <c>Tickets</c>, không phải cột của bảng <c>Seats</c>: trạng thái là của CẶP
/// (chuyến, ghế), cùng một ghế chuyến này đã bán còn chuyến khác vẫn trống.
/// </summary>
public class TripSeatMapService : ITripSeatMapService
{
    private const string TripNotFoundMessage = "Không tìm thấy chuyến xe";

    private const string StatusAvailable = "Available";

    private const string StatusHeld = "Held";

    private readonly AppDbContext _db;

    public TripSeatMapService(AppDbContext db) => _db = db;

    public async Task<ServiceResult<TripSeatMapResponse>> GetSeatMapAsync(
        Guid tripId,
        CancellationToken cancellationToken = default)
    {
        // Ghép tường minh bằng join thay vì Include — cùng lối TripService: join trong là thứ đưa
        // chuyến mồ côi (xe không còn) vào nhánh 404 một cách tự nhiên. Chuyến có thật mà không
        // xem được xe thì không có sơ đồ ghế để trả.
        var row = await (
            from trip in _db.Trips.AsNoTracking()
            join bus in _db.Buses.AsNoTracking() on trip.BusId equals bus.Id
            where trip.Id == tripId
            select new { Trip = trip, Bus = bus }).FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return ServiceResult<TripSeatMapResponse>.NotFound(TripNotFoundMessage);
        }

        // Thứ tự vẽ sơ đồ: tầng → hàng → cột. Xếp tiếp theo Id khi hai dòng trùng cả ba (dữ liệu
        // lỗi ngoài khuôn lưới) để thứ tự hiển thị luôn ổn định giữa các lần gọi.
        var seats = await _db.Seats.AsNoTracking()
            .Where(s => s.BusId == row.Bus.Id)
            .OrderBy(s => s.Floor)
            .ThenBy(s => s.RowIndex)
            .ThenBy(s => s.ColumnIndex)
            .ThenBy(s => s.Id)
            .ToListAsync(cancellationToken);

        // Số tầng lấy từ SeatLayouts theo BusType (khoá nghiệp vụ unique ở AppDbContext.Seat.cs):
        // sơ đồ là MẪU của một loại xe, ghế thật trỏ về nó qua SeatLayoutId. Loại xe chưa có sơ đồ
        // là trạng thái dữ liệu hợp lệ (xe chưa được sinh ghế, docs/26 §1) → floors = 0 và seats
        // rỗng, không phải lỗi.
        var layout = await _db.SeatLayouts.AsNoTracking()
            .FirstOrDefaultAsync(l => l.BusType == row.Bus.BusType, cancellationToken);

        // Giá phổ thông của tuyến — cùng nguồn và cùng nghĩa với price của GET /trips/search
        // (TripSearchService). Tuyến chưa cấu hình giá là trạng thái dữ liệu bình thường → null.
        var pricePerSeat = await _db.Fares.AsNoTracking()
            .Where(f => f.RouteId == row.Trip.RouteId && f.PassengerType == PassengerType.Standard)
            .Select(f => (decimal?)f.Price)
            .FirstOrDefaultAsync(cancellationToken);

        // Ghế đang bị giữ cho chuyến này: chỉ dòng Status = Holding mới chặn ghế — đúng điều kiện
        // của partial unique index (TripId, SeatId) ở AppDbContext.Seat.cs. Hold đã Expired /
        // Released / Confirmed không giữ ghế nữa. Index bảo đảm một (chuyến, ghế) có tối đa MỘT
        // dòng Holding; Distinct phòng provider InMemory của bộ test không cưỡng chế index.
        var heldSeatIds = await _db.SeatHolds.AsNoTracking()
            .Where(h => h.TripId == tripId && h.Status == SeatHoldStatus.Holding)
            .Select(h => h.SeatId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var heldSet = heldSeatIds.ToHashSet();

        // Trạng thái "Paid" hôm nay không có nguồn: bảng Tickets (US 4 — Vàng Thị Dăm) chưa được
        // migrate nên không có gì để hỏi ghế đã bán. Khi bảng vé vào, chỉ cần thêm một truy vấn
        // Tickets (TripId, SeatId, Status = 'Paid'/'Held') để đưa ghế đã bán về "Paid" — hình dạng
        // response không đổi, đã chốt trong api-contract.md.
        var result = new TripSeatMapResponse
        {
            TripId = tripId,
            BusType = row.Bus.BusType,
            Floors = layout?.NumberOfFloors ?? 0,
            PricePerSeat = pricePerSeat,
            // Phụ trội VIP chưa có nguồn dữ liệu (phần "giá theo ghế" là task của Dương Thị Hạnh)
            // nên luôn null cho tới khi nhóm chốt con số này.
            VipSurcharge = null,
            Seats = seats.Select(seat => new TripSeatMapSeatResponse
            {
                Id = seat.Id,
                SeatNumber = seat.SeatNumber,
                Floor = seat.Floor,
                RowIndex = seat.RowIndex,
                ColumnIndex = seat.ColumnIndex,
                SeatType = seat.SeatType.ToString(),
                Status = heldSet.Contains(seat.Id) ? StatusHeld : StatusAvailable,
                // Ghế VIP chưa được cộng phụ trội vì vipSurcharge chưa có nguồn — khi có, chỉ dòng
                // này đổi thành pricePerSeat + (Vip ? vipSurcharge : 0).
                Price = pricePerSeat,
            }).ToList(),
        };

        return ServiceResult<TripSeatMapResponse>.Ok(result);
    }
}
