namespace SmartBus.Api.Entities;

/// <summary>
/// Bảng <c>Subscriptions</c> — MỘT LƯỢT ĐĂNG KÝ THEO DÕI THÔNG BÁO của một tài khoản trên một tuyến,
/// hoặc trên một trạm cụ thể của tuyến đó. Trả lời đúng câu <i>"ai muốn nhận thông báo về tuyến
/// nào"</i> mà <c>docs/33</c> §1 đã dùng để hoãn bảng này lại.
///
/// Thuộc task *"Migrate bảng Notifications, NotificationTemplates, Subscriptions"* của bảng phân công
/// **Sprint 5, dòng 14** (Vàng Thị Dăm). Người tiêu thụ — đều là task Sprint 5 **chưa viết**:
///
/// | Dòng | Task | Dùng bảng này để làm gì |
/// |---|---|---|
/// | 15 | Service bắn thông báo khi xe cách trạm dưới 500m (Kiên) | lọc ra khách đang theo dõi trạm đó |
/// | 17 | API đăng ký / huỷ theo dõi thông báo theo tuyến và trạm (Hiếu) | ghi và xoá dòng ở đây |
/// | 20 | Component bật/tắt nhận thông báo theo trạm (Thịnh) | đọc trạng thái bật/tắt để vẽ công tắc |
/// | 29 | Service tự động đẩy sự cố đến hành khách đang theo dõi tuyến (Kiên) | lọc ra khách theo dõi **cả tuyến** |
///
/// ⚠️ <b>Bảng này KHÔNG có trong A9</b> — cùng tình trạng và cùng cách xử lý như
/// <see cref="NotificationTemplate"/> (xem phần đầu file đó): dựng theo phân công, rồi báo nhóm.
///
/// ⚠️ <b>Tên <c>Subscriptions</c> rộng hơn nghĩa thật của bảng.</b> Ở đây nó luôn nghĩa là *"đăng ký
/// nhận THÔNG BÁO"*, không phải đăng ký gói dịch vụ hay thuê bao trả tiền — repo không có nghiệp vụ
/// nào như vậy. Giữ đúng tên bảng phân công đã ghi (cùng lối <c>TicketValidations</c> ở <c>docs/34</c>),
/// nhưng nếu nhóm định đổi thì <b>đổi ngay bây giờ</b>: đổi tên bảng lúc đã có dữ liệu đắt hơn hẳn.
///
/// ⚠️ <b>Cố ý KHÔNG có cột "loại thông báo muốn nhận".</b> Bảng phân công dòng 17 chốt độ chi tiết là
/// *theo tuyến và trạm*, không phải theo loại. Cả hai nghiệp vụ đã biết đều dùng chung một lượt đăng
/// ký: thông báo trạm (dòng 15) và thông báo sự cố (dòng 29 — *"hành khách đang theo dõi tuyến"*).
/// Thêm cột loại bây giờ là dựng schema cho một màn hình chưa ai viết, mà thêm sau chỉ là một cột
/// nullable — cùng lập luận đã dùng ở <c>docs/33</c> §1 để hoãn chính bảng này.
/// </summary>
public class Subscription
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Người theo dõi — FK → <c>Users</c>, <c>RESTRICT</c>.
    ///
    /// Không nullable: một lượt theo dõi không có người theo dõi thì không có ai để bắn thông báo.
    /// <c>RESTRICT</c> theo A5: xoá một tài khoản không được kéo theo dấu vết đăng ký của họ. Hệ quả
    /// chấp nhận được vì dự án "xoá" tài khoản bằng <c>User.IsActive</c> chứ không xoá cứng (A4) —
    /// cùng lập luận đã ghi cho <c>Trip.DriverId</c>.
    /// </summary>
    public Guid UserId { get; set; }

    public User? User { get; set; }

    /// <summary>
    /// Tuyến được theo dõi — FK → <c>Routes</c>, <c>RESTRICT</c>.
    ///
    /// Không nullable: mọi lượt theo dõi của dự án đều gắn với một tuyến. Đây là cột mà dòng 29 lọc
    /// theo (*"hành khách đang theo dõi tuyến"*), nên nó có chỉ mục riêng.
    ///
    /// ⚠️ <b>Một lượt theo dõi KHÔNG gắn với chuyến hay ngày.</b> Khách theo dõi *"tuyến 01"* chứ
    /// không theo dõi *"chuyến 07:30 ngày mai"* — nếu gắn với chuyến thì mỗi ngày khách phải đăng ký
    /// lại một lần, và đăng ký sẽ hết hạn sau khi chuyến chạy xong. Đây là chủ ý, không phải chỗ sót.
    /// </summary>
    public Guid RouteId { get; set; }

    public Route? Route { get; set; }

    /// <summary>
    /// Trạm được theo dõi — FK → <c>Stops</c>, <c>RESTRICT</c> — hoặc <c>null</c>.
    ///
    /// 🔴 <b>Hai nghĩa, phân biệt bằng NULL:</b>
    ///
    /// | Giá trị | Nghĩa | Ai dùng |
    /// |---|---|---|
    /// | Có trạm | Chỉ báo khi xe tới gần **trạm này** (dòng 15, 20) | công tắc bật/tắt theo trạm |
    /// | <c>null</c> | Theo dõi **cả tuyến** — mọi trạm, và mọi sự cố của tuyến | dòng 29 |
    ///
    /// Không tách thành hai bảng vì hai thứ này là **cùng một hành động của người dùng** (một công
    /// tắc), chỉ khác độ chi tiết; tách bảng thì màn dòng 20 phải đọc hai nguồn để vẽ một công tắc.
    ///
    /// ⚠️ <b>Ràng buộc "trạm này phải thuộc tuyến này" KHÔNG chặn được ở tầng CSDL</b> — khoá ngoại
    /// chỉ nói <c>StopId</c> có tồn tại, không nói nó nằm trên <c>RouteId</c>. Việc kiểm thuộc
    /// <c>RouteStops</c> là của endpoint đăng ký (dòng 17, Hiếu). Ghi rõ ở đây vì đây là chỗ dễ tưởng
    /// khoá ngoại đã lo hộ.
    ///
    /// ⚠️ <b>Câu hỏi mở — CHIỀU ĐI:</b> bảng này giả định một <c>Route</c> là một chiều (repo không có
    /// cột <c>Direction</c> nào — đã kiểm bằng <c>git grep</c>). Nếu nhóm chốt rằng một tuyến chạy cả
    /// lượt đi lẫn lượt về (A8.6 cảnh báo *"xe đi và xe về trên cùng tuyến trùng toạ độ"*), thì khách
    /// theo dõi một trạm sẽ nhận thông báo cho **cả hai chiều**, và bảng phải thêm cột chiều. Chưa
    /// thêm bây giờ vì chưa ai chốt — xem <c>docs/37</c> §6.
    /// </summary>
    public Guid? StopId { get; set; }

    public Stop? Stop { get; set; }

    /// <summary>
    /// Thời điểm khách bấm theo dõi (UTC).
    ///
    /// ⚠️ <b>Cố ý KHÔNG có <c>UpdatedAt</c>, và đây là quyết định chứ không phải chỗ sót:</b> bảng
    /// không có cột nào sửa được. *"Huỷ theo dõi"* (dòng 17) và *"tắt công tắc"* (dòng 20) đều là
    /// **XOÁ dòng**, không phải lật một cờ — A4 cấm <c>IsDeleted</c> và cảnh báo đúng tình huống này:
    /// *"Hai cơ chế xoá mềm song song sẽ có ngày mâu thuẫn nhau."* Bật lại thì ghi dòng mới, và
    /// <c>CreatedAt</c> mang nghĩa *"theo dõi từ khi nào"* của lượt theo dõi đang có hiệu lực.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
