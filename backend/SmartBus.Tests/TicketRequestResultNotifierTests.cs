using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SmartBus.Api.Data;
using SmartBus.Api.Entities;
using SmartBus.Api.Hubs;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Test bộ phận ghi + đẩy thông báo kết quả xử lý yêu cầu huỷ / đổi vé —
/// <see cref="TicketRequestResultNotifier"/> (task *"Ghi log và thông báo kết quả xử lý cho hành
/// khách"*, dòng 7 Sprint 4, Vàng Thị Dăm).
///
/// Gọi thẳng service, KHÔNG dựng host: đây là hàm thuần theo dữ liệu đầu vào (đọc trạng thái của
/// yêu cầu rồi ghi một dòng), nên không có gì phải chờ. Phần "hub và notifier có thật sự được nối vào
/// app không" nằm ngoài tầm file này — xem ghi chú ở cuối §"Chỗ nối vào app" của
/// docs/33-thong-bao-ket-qua-huy-doi-ve.md.
///
/// Dùng provider InMemory như phần còn lại của bộ test. Không seed Users/Tickets/Trips: InMemory
/// không cưỡng chế khoá ngoài, mà service chỉ đọc đúng dòng yêu cầu truyền vào và ghi bảng
/// Notifications.
///
/// ⚠️ InMemory cũng KHÔNG cưỡng chế chỉ mục, nên file này xanh không nói gì về chuyện
/// IX_Notifications_UserId_CreatedAt (A6) có tồn tại hay không — chỉ mục đó nằm ở
/// AppDbContext.Notification.cs và là việc của migration, không phải của test này.
///
/// ⚠️ Chốt chống ghi trùng của service là phép ĐỌC TRƯỚC KHI GHI, không phải unique index — xem
/// §4 của docs/33. Bài <see cref="Goi_lai_lan_hai_khong_sinh_dong_thu_hai"/> kiểm đúng hành vi đó
/// trên MỘT tiến trình; nó không nói gì về hai bản app chạy song song.
/// </summary>
public class TicketRequestResultNotifierTests
{
    // ── Duyệt / từ chối yêu cầu HUỶ ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Duyet_yeu_cau_huy_thi_ghi_mot_dong_va_day_dung_nhom()
    {
        await using var db = TaoDb();
        var userId = Guid.NewGuid();
        var request = YeuCauHuy(userId, CancellationRequestStatus.Approved);
        var client = new ClientGhiNhan();
        string? nhom = null;

        await TaoService(db, client, group => nhom = group).NotifyCancellationReviewedAsync(request);

        var thongBao = Assert.Single(db.Notifications);

        Assert.Equal(userId, thongBao.UserId);
        Assert.Equal(NotificationType.CancellationApproved, thongBao.Type);

        // Con trỏ đa hình phải trỏ về ĐÚNG bảng và ĐÚNG dòng — đây là thứ màn danh sách thông báo
        // dùng để mở màn chi tiết, và là thứ phép chống ghi trùng lọc theo.
        Assert.Equal(NotificationTarget.CancellationRequest, thongBao.RelatedEntityType);
        Assert.Equal(request.Id, thongBao.RelatedEntityId);

        Assert.Equal("Đã duyệt yêu cầu huỷ vé", thongBao.Title);
        Assert.Equal("Yêu cầu huỷ vé của bạn đã được duyệt. Nhà xe sẽ xử lý hoàn tiền theo quy định.", thongBao.Body);

        // Không có CHỮ SỐ nào trong nội dung, và đó là chốt chứ không phải chỗ sót: việc hoàn tiền là
        // của bảng Refunds (US 8, sprint khác) nên thông báo không được hứa một con số hệ thống chưa
        // chuyển, và định dạng tiền là việc của frontend. Bài này TỪNG ĐỎ nếu ai đó thêm "hoàn
        // 70.000đ" hay "phí huỷ 30.000đ" vào body.
        Assert.DoesNotMatch("[0-9]", thongBao.Body);

        // Tên nhóm do NotificationHub.GroupOf sinh — một chỗ duy nhất, OnConnectedAsync phía hub dùng
        // đúng hàm đó, nên ghim hình dạng "user:{id}" ở đây là ghim luôn hai đầu khớp nhau.
        Assert.Equal($"user:{userId}", nhom);
        Assert.Equal(new[] { thongBao.Id }, client.NotificationIds);
    }

    [Fact]
    public async Task Tu_choi_yeu_cau_huy_thi_body_mang_nguyen_van_ly_do()
    {
        await using var db = TaoDb();
        var request = YeuCauHuy(Guid.NewGuid(), CancellationRequestStatus.Rejected);
        request.RejectReason = "  Vé đã quá sát giờ chạy, không thể huỷ.  ";

        await TaoService(db).NotifyCancellationReviewedAsync(request);

        var thongBao = Assert.Single(db.Notifications);

        Assert.Equal(NotificationType.CancellationRejected, thongBao.Type);
        Assert.Equal("Từ chối yêu cầu huỷ vé", thongBao.Title);

        // Lý do là nội dung DUY NHẤT trả lời được câu "vì sao tôi bị từ chối", nên nó phải nằm thẳng
        // trong thông báo: A8.5 chốt khách đọc lại được khi offline, mà câu trỏ sang màn khác thì không
        // đọc được ở đó. Cắt khoảng trắng thừa, giữ nguyên văn.
        Assert.Equal(
            "Yêu cầu huỷ vé của bạn đã bị từ chối. Lý do: Vé đã quá sát giờ chạy, không thể huỷ.",
            thongBao.Body);
    }

    [Fact]
    public async Task Tu_choi_khong_neu_ly_do_thi_body_noi_ro_chu_khong_de_trong()
    {
        await using var db = TaoDb();
        var request = YeuCauHuy(Guid.NewGuid(), CancellationRequestStatus.Rejected);

        // Lý do để trống là hợp lệ (cột nullable, quản lý không buộc phải gõ). Nội dung vẫn phải là một
        // câu đọc được — để trống phần đuôi thì thông báo cụt thành "…đã bị từ chối." và khách không
        // phân biệt được với trường hợp lý do bị mất.
        request.RejectReason = "   ";

        await TaoService(db).NotifyCancellationReviewedAsync(request);

        Assert.Equal(
            "Yêu cầu huỷ vé của bạn đã bị từ chối. Người duyệt không nêu lý do.",
            Assert.Single(db.Notifications).Body);
    }

    // ── Duyệt / từ chối yêu cầu ĐỔI CHUYẾN ──────────────────────────────────────────────────────

    [Fact]
    public async Task Duyet_yeu_cau_doi_thi_bao_ve_moi_da_phat_hanh()
    {
        await using var db = TaoDb();
        var userId = Guid.NewGuid();
        var request = YeuCauDoi(userId, TicketChangeStatus.Approved);
        var client = new ClientGhiNhan();
        string? nhom = null;

        await TaoService(db, client, group => nhom = group).NotifyTicketChangeReviewedAsync(request);

        var thongBao = Assert.Single(db.Notifications);

        Assert.Equal(NotificationType.TicketChangeApproved, thongBao.Type);
        Assert.Equal(NotificationTarget.TicketChange, thongBao.RelatedEntityType);
        Assert.Equal(request.Id, thongBao.RelatedEntityId);

        // Duyệt một yêu cầu đổi là HUỶ vé cũ + phát hành vé MỚI (docs/32 §3.1) — câu này là thứ nói
        // cho khách biết vé mới tồn tại, vì Tickets không có cột nào trỏ ngược về lượt đổi đã sinh ra nó.
        Assert.Equal("Đã duyệt yêu cầu đổi chuyến", thongBao.Title);
        Assert.Equal("Yêu cầu đổi chuyến của bạn đã được duyệt. Vé mới của bạn đã được phát hành.", thongBao.Body);

        Assert.Equal($"user:{userId}", nhom);
        Assert.Equal(new[] { thongBao.Id }, client.NotificationIds);
    }

    [Fact]
    public async Task Tu_choi_yeu_cau_doi_thi_gom_ly_do_va_khong_day_sai_nhom()
    {
        await using var db = TaoDb();
        var userId = Guid.NewGuid();
        var request = YeuCauDoi(userId, TicketChangeStatus.Rejected);
        request.RejectReason = "Chuyến mới đã hết ghế.";
        var client = new ClientGhiNhan();
        string? nhom = null;

        await TaoService(db, client, group => nhom = group).NotifyTicketChangeReviewedAsync(request);

        var thongBao = Assert.Single(db.Notifications);

        Assert.Equal(NotificationType.TicketChangeRejected, thongBao.Type);
        Assert.Equal("Từ chối yêu cầu đổi chuyến", thongBao.Title);
        Assert.Equal("Yêu cầu đổi chuyến của bạn đã bị từ chối. Lý do: Chuyến mới đã hết ghế.", thongBao.Body);

        // Thông báo là dữ liệu RIÊNG của một người: đẩy nhầm nhóm nghĩa là khách A đọc được kết quả
        // của khách B. Nhóm phải suy từ UserId của yêu cầu, không từ bất cứ thứ gì khác.
        Assert.Equal($"user:{userId}", nhom);
    }

    // ── Hai trạng thái không có gì để báo ───────────────────────────────────────────────────────

    [Fact]
    public async Task Yeu_cau_con_Pending_thi_khong_ghi_khong_day()
    {
        await using var db = TaoDb();
        var request = YeuCauHuy(Guid.NewGuid(), CancellationRequestStatus.Pending);
        var client = new ClientGhiNhan();

        await TaoService(db, client).NotifyCancellationReviewedAsync(request);
        await TaoService(db, client).NotifyTicketChangeReviewedAsync(YeuCauDoi(Guid.NewGuid(), TicketChangeStatus.Pending));

        // Chưa ai xử lý thì chưa có kết quả nào để báo. Gọi ở trạng thái này là lỗi ở chỗ gọi (quên
        // lưu trạng thái trước khi báo) nên service ghi log cảnh báo — nhưng tuyệt đối không được sinh
        // ra một thông báo "đã bị từ chối" cho một yêu cầu còn đang chờ.
        Assert.Empty(db.Notifications);
        Assert.Empty(client.NotificationIds);
    }

    [Fact]
    public async Task Goi_lai_lan_hai_khong_sinh_dong_thu_hai()
    {
        await using var db = TaoDb();
        var request = YeuCauHuy(Guid.NewGuid(), CancellationRequestStatus.Approved);
        var client = new ClientGhiNhan();
        var service = TaoService(db, client);

        await service.NotifyCancellationReviewedAsync(request);
        await service.NotifyCancellationReviewedAsync(request);

        // Một quyết định = MỘT thông báo. Không có chốt này thì một lượt gọi lại (client retry, hoặc
        // endpoint bị gọi hai lần) làm khách thấy hai dòng y hệt nhau trên màn thông báo.
        Assert.Single(db.Notifications);

        // Lượt thứ hai KHÔNG đẩy lại: đẩy lại chỉ làm client tải lại danh sách vô ích, mà lần này
        // khác SeatMapNotifier ("thà thừa") — ở đây không có gì mới để nhắc.
        Assert.Single(client.NotificationIds);
    }

    [Fact]
    public async Task Yeu_cau_KHAC_thi_van_bao_binh_thuong()
    {
        await using var db = TaoDb();
        var userId = Guid.NewGuid();

        // Phép chống ghi trùng lọc theo ĐÚNG dòng yêu cầu, không theo người nhận: khách gửi hai yêu cầu
        // khác nhau (một vé bị từ chối rồi xin đổi vé khác) phải nhận đủ hai thông báo.
        await TaoService(db).NotifyCancellationReviewedAsync(YeuCauHuy(userId, CancellationRequestStatus.Rejected));
        await TaoService(db).NotifyTicketChangeReviewedAsync(YeuCauDoi(userId, TicketChangeStatus.Rejected));

        Assert.Equal(2, db.Notifications.Count());
    }

    // ── Hai nhánh hỏng: hợp đồng "không bao giờ ném lỗi" ────────────────────────────────────────

    [Fact]
    public async Task Ken_duy_hong_khong_nem_loi_va_dong_van_duoc_ghi()
    {
        await using var db = TaoDb();

        var service = TaoService(db, new ClientGhiNhan(), _ => throw new InvalidOperationException("kênh đẩy hỏng"));

        var loi = await Record.ExceptionAsync(
            () => service.NotifyCancellationReviewedAsync(YeuCauHuy(Guid.NewGuid(), CancellationRequestStatus.Approved)));

        // Người gọi đang ở cuối một thao tác nghiệp vụ ĐÃ THÀNH CÔNG (quản lý vừa duyệt xong). Để lỗi
        // kênh đẩy nổi lên là biến lượt duyệt thành công thành 500 — quản lý tưởng chưa duyệt được và
        // bấm lại.
        Assert.Null(loi);

        // Và dòng thông báo vẫn phải còn: mất lời nhắc tức thời là chuyện nhỏ, mất bản ghi để khách đọc
        // lại khi offline (A8.5) mới là chuyện lớn.
        Assert.Single(db.Notifications);
    }

    [Fact]
    public async Task Ghi_hong_khong_nem_loi()
    {
        var db = TaoDb();
        var service = TaoService(db, new ClientGhiNhan());

        // DbContext đã bị dispose → mọi lượt ghi đều ném ObjectDisposedException. Đây là cách rẻ nhất
        // để dựng nhánh "CSDL hỏng" mà không cần một provider biết ném lỗi.
        await db.DisposeAsync();

        var loi = await Record.ExceptionAsync(
            () => service.NotifyCancellationReviewedAsync(YeuCauHuy(Guid.NewGuid(), CancellationRequestStatus.Approved)));

        Assert.Null(loi);
    }

    // ── Hạ tầng dùng chung của lớp test ──────────────────────────────────────────────────────────
    // Mỗi lớp test tự chép helper, không có lớp base chung — đúng lệ đang có của dự án.

    private static AppDbContext TaoDb()
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"thong-bao-ket-qua-{Guid.NewGuid()}")
            .Options);

    /// <summary>
    /// Service với một kênh đẩy giả. <paramref name="onGroup"/> nhận tên nhóm mà service yêu cầu —
    /// dùng để ghim hình dạng tên nhóm, thứ mà một lớp <c>IHubContext</c> giả không tự kiểm được.
    /// </summary>
    private static TicketRequestResultNotifier TaoService(
        AppDbContext db,
        ClientGhiNhan? client = null,
        Action<string>? onGroup = null)
        => new(
            db,
            new HubContextGia(group =>
            {
                onGroup?.Invoke(group);
                return client ?? new ClientGhiNhan();
            }),
            NullLogger<TicketRequestResultNotifier>.Instance);

    /// <summary>
    /// Yêu cầu huỷ tối thiểu để service đi hết một nhánh. Không cần Ticket/User thật: InMemory không
    /// cưỡng chế khoá ngoài, mà service chỉ đọc chính dòng này.
    /// </summary>
    private static CancellationRequest YeuCauHuy(Guid userId, CancellationRequestStatus status)
        => new()
        {
            UserId = userId,
            TicketId = Guid.NewGuid(),
            Status = status,
            ReviewedByUserId = status == CancellationRequestStatus.Pending ? null : Guid.NewGuid(),
            ReviewedAt = status == CancellationRequestStatus.Pending ? null : DateTime.UtcNow,
        };

    private static TicketChange YeuCauDoi(Guid userId, TicketChangeStatus status)
        => new()
        {
            UserId = userId,
            TicketId = Guid.NewGuid(),
            NewTripId = Guid.NewGuid(),
            NewSeatId = Guid.NewGuid(),
            Status = status,
            ReviewedByUserId = status == TicketChangeStatus.Pending ? null : Guid.NewGuid(),
            ReviewedAt = status == TicketChangeStatus.Pending ? null : DateTime.UtcNow,
        };

    private sealed class ClientGhiNhan : INotificationClient
    {
        public List<Guid> NotificationIds { get; } = [];

        public Task NotificationReceived(Guid notificationId)
        {
            NotificationIds.Add(notificationId);
            return Task.CompletedTask;
        }
    }

    private sealed class HubContextGia : IHubContext<NotificationHub, INotificationClient>
    {
        public HubContextGia(Func<string, INotificationClient> group) => Clients = new KhachGia(group);

        public IHubClients<INotificationClient> Clients { get; }

        public IGroupManager Groups => throw new NotSupportedException();

        private sealed class KhachGia : IHubClients<INotificationClient>
        {
            private readonly Func<string, INotificationClient> _group;

            public KhachGia(Func<string, INotificationClient> group) => _group = group;

            public INotificationClient Group(string groupName) => _group(groupName);

            // Tám thành viên còn lại không dùng tới trong các ca ở đây — ném để lộ ngay nếu có người lỡ gọi.
            public INotificationClient All => throw new NotSupportedException();

            public INotificationClient AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();

            public INotificationClient Client(string connectionId) => throw new NotSupportedException();

            public INotificationClient Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();

            public INotificationClient GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();

            public INotificationClient Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();

            public INotificationClient User(string userId) => throw new NotSupportedException();

            public INotificationClient Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
        }
    }
}
