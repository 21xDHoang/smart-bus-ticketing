namespace SmartBus.Api.Entities;

/// <summary>
/// Bảng SeatLayouts — sơ đồ ghế của một LOẠI XE. Task Sprint 3 "Migrate bảng Seats, SeatHolds,
/// SeatLayouts" (Vàng Thị Dăm) + dòng 9 "Cấu hình sơ đồ ghế theo loại xe (số tầng, số ghế, ghế VIP)".
///
/// Đây là MẪU, không phải ghế thật: nó trả lời "loại xe này có mấy tầng, mỗi tầng mấy hàng mấy cột,
/// ghế nào là VIP". Ghế thật của từng xe nằm ở bảng <c>Seats</c> và trỏ về đây bằng
/// <see cref="Seat.SeatLayoutId"/> — nhờ vậy sửa sơ đồ là chuyện của mẫu, còn ghế đã sinh vẫn giữ
/// nguyên vị trí đã in trên vé.
///
/// Sơ đồ tự mô tả trọn hình dạng của nó: <see cref="NumberOfFloors"/> × <see cref="RowsPerFloor"/>
/// × <see cref="ColumnsPerRow"/> là lưới ghế, <see cref="VipSeatPositions"/> là danh sách vị trí VIP
/// trong lưới đó. Từ bốn giá trị này dựng lại được toàn bộ sơ đồ mà không cần đọc bảng <c>Seats</c>.
///
/// Khoá nghiệp vụ là <see cref="BusType"/>: mỗi loại xe đúng MỘT sơ đồ (chỉ mục unique ở
/// AppDbContext.Seat.cs). Dùng thẳng chuỗi loại xe thay vì tạo bảng BusTypes riêng vì
/// <c>Buses.BusType</c> từ Sprint 2 đã là chuỗi tự do — thêm bảng tra cứu lúc này là thêm một
/// tầng ánh xạ mà không story nào hỏi tới.
///
/// ⚠️ A9 của docs/03-quy-uoc.md mới liệt kê 20 bảng, CHƯA có SeatLayouts (và SeatHolds). Hai bảng
/// này là chủ trương có sẵn của bảng phân công Sprint 3 — cần nhóm chốt bổ sung vào A9 cho tài
/// liệu khớp CSDL (việc báo nhóm, không phải việc code).
/// </summary>
public class SeatLayout
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Loại xe áp dụng — khớp giá trị <c>Buses.BusType</c> ("Xe buýt 45 chỗ", "Xe buýt điện").
    /// Là khoá nghiệp vụ: hai sơ đồ cùng một loại xe là dữ liệu mâu thuẫn, chặn ở tầng CSDL.
    /// </summary>
    public string BusType { get; set; } = string.Empty;

    /// <summary>Số tầng của xe — 1 với xe một tầng, 2 với xe hai tầng / giường nằm.</summary>
    public int NumberOfFloors { get; set; } = 1;

    /// <summary>
    /// Số hàng ghế trên MỖI tầng. Mọi tầng dùng chung một con số này: xe hai tầng thật có thể lệch
    /// nhau vài hàng, nhưng chưa story nào cần, mà tách ra thành mảng số hàng theo tầng là thêm một
    /// kiểu dữ liệu nữa cho mọi tầng code phải hiểu.
    /// </summary>
    public int RowsPerFloor { get; set; }

    /// <summary>Số ghế tối đa trên một hàng (cột trái cùng là 1) — hàng nào cũng đủ số cột này.</summary>
    public int ColumnsPerRow { get; set; }

    /// <summary>
    /// Tổng số ghế của sơ đồ. Phải khớp số dòng <c>Seat</c> sinh ra từ sơ đồ này — cùng lối ràng
    /// buộc "Buses.Capacity phải khớp số dòng Seat" đã ghi ở <see cref="Bus.Capacity"/>: kiểm tra ở
    /// tầng service, CSDL không có check constraint (cùng lối các cột nghiệp vụ khác của dự án).
    ///
    /// Bất biến của sơ đồ: <c>TotalSeats = NumberOfFloors × RowsPerFloor × ColumnsPerRow</c> — lưới
    /// ghế không khuyết ô. Cố ý KHÔNG đặt check constraint ở CSDL: cùng lối các cột nghiệp vụ khác
    /// của dự án, ràng buộc này thuộc tầng service.
    /// </summary>
    public int TotalSeats { get; set; }

    /// <summary>
    /// Danh sách vị trí ghế VIP, mỗi vị trí viết theo khoá <c>"&lt;tầng&gt;-&lt;hàng&gt;-&lt;cột&gt;"</c>
    /// và ngăn nhau bằng <c>';'</c> — ví dụ hàng đầu hai ghế trái của xe một tầng:
    /// <c>"1-1-1;1-1-2"</c>. Chuỗi rỗng = sơ đồ không có ghế VIP.
    ///
    /// Vì sao chuỗi chứ không phải bảng <c>SeatLayoutVips</c> riêng: (1) vị trí VIP luôn được đọc và
    /// ghi TRỌN CỤM cùng sơ đồ — màn cấu hình sửa cả lưới lẫn tập VIP trong một lượt, không ai truy
    /// vấn "ghế VIP nào" ở tầng CSDL; (2) mỗi sơ đồ chỉ vài chục vị trí; (3) tránh thêm bảng thứ tư
    /// nằm ngoài danh sách A9 của quy ước; (4) khoá vị trí ở đây đúng bằng khoá ô ghế của màn chọn
    /// ghế, nên không sinh thêm một quy ước toạ độ thứ hai.
    ///
    /// Cái giá phải trả: CSDL không join/index được lên tập VIP. Chấp nhận, vì truy vấn duy nhất cần
    /// đến nó là "lấy sơ đồ của loại xe X" — đã có <see cref="BusType"/> lo.
    /// </summary>
    public string VipSeatPositions { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Sơ đồ có sửa dữ liệu (đổi số tầng, đổi tổng số ghế) nên có UpdatedAt (A4).</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Ghế của các xe được sinh theo sơ đồ này.</summary>
    public ICollection<Seat> Seats { get; set; } = new List<Seat>();
}
