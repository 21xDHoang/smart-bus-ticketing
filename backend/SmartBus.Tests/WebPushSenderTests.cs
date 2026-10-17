using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Test cho <see cref="WebPushClient"/> + <see cref="WebPushSender"/> — cặp "gửi từng thiết bị" và
/// "gửi cho một người". HTTP giả bằng <see cref="FakeHttpMessageHandler"/>: không gọi mạng thật,
/// chỉ trả kết cục do test chọn. Việc mã hoá RFC 8291 + ký VAPID là phần của thư viện
/// Lib.Net.Http.WebPush (đã được chính thư viện test) — các test ở đây chỉ ghim phần của dự án.
///
/// Hai hợp đồng được ghim ở đây: client KHÔNG BAO GIỜ ném lỗi vận chuyển (gom về Failed), và
/// sender DỌN subscription bị push service báo chết (404/410) nhưng GIỮ nguyên khi chỉ là lỗi
/// mạng — nhầm hai ca này là một là xoá sạch thiết bị sau một cú nghẽn mạng.
/// </summary>
public class WebPushSenderTests
{
    private const string TestPublicKey = "BFKkHFhOr1Z6OZH2yVJioAulrtRcOcPKdIvP_R8muDo0vr8ChsNLegy-jh6JeVtW2V-3vdlWpGnwz0d3vihu6n4";
    private const string TestPrivateKey = "MIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQQg-MppzMm5rod28JK9AVahSuzBcnxnNb5UOb3TLGLBcaShRANCAARSpBxYTq9WejmR9slSYqALpa7UXDnDynSLz_0fJrg6NL6_AobDS3oMvo4eiXlbVtlft73ZVqRp8M9Hd74obup-";

    private static WebPushOptions TestOptions() => new()
    {
        Subject = "mailto:test@smartbus.example",
        PublicKey = TestPublicKey,
        PrivateKey = TestPrivateKey,
    };

    private static WebPushSubscriptionInfo ThietBi(Guid userId, Guid? id = null, string endpoint = "https://fcm.googleapis.com/fcm/send/thi-eb-i") => new()
    {
        Id = id ?? Guid.NewGuid(),
        UserId = userId,
        Endpoint = endpoint,
        P256Dh = "BKOtatI8ZS2MqSjG3PlfrDb3rYwHAI2qHRO1LuT_fkrr8fFARpPU8B8Rpv44Uvc4BuHY7Ow5oiPCZPYXcQOXDp4",
        Auth = "AAECAwQFBgcICQoLDA0ODw",
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static WebPushClient Client(FakeHttpMessageHandler handler) => new(
        new HttpClient(handler),
        Options.Create(TestOptions()),
        NullLogger<WebPushClient>.Instance);

    private static InMemoryWebPushSubscriptionStore Store() => new();

    private static WebPushSender Sender(FakeHttpMessageHandler handler, InMemoryWebPushSubscriptionStore store)
        => new(Client(handler), store, NullLogger<WebPushSender>.Instance);

    private static WebPushMessage Tin() => new()
    {
        Title = "Xe 01 sắp đến",
        Body = "Trạm Công Viên còn 500 m.",
        Url = "/notifications",
    };

    // -------------------------------------------------------------- WebPushClient

    [Fact]
    public async Task Push_service_nhan_thi_tra_Sent()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Created));

        var result = await Client(handler).SendAsync(ThietBi(Guid.NewGuid()), Tin(), CancellationToken.None);

        Assert.Equal(PushDeliveryResult.Sent, result);
        Assert.StartsWith("https://fcm.googleapis.com", handler.Requests.Single().RequestUri!.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    public async Task Push_service_bao_subscription_chet_thi_tra_Stale(HttpStatusCode statusCode)
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(statusCode));

        var result = await Client(handler).SendAsync(ThietBi(Guid.NewGuid()), Tin(), CancellationToken.None);

        Assert.Equal(PushDeliveryResult.Stale, result);
    }

    [Fact]
    public async Task Push_service_tra_5xx_thi_tra_Failed_khong_nem()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var result = await Client(handler).SendAsync(ThietBi(Guid.NewGuid()), Tin(), CancellationToken.None);

        Assert.Equal(PushDeliveryResult.Failed, result);
    }

    [Fact]
    public async Task Mang_chet_thi_tra_Failed_khong_nem()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("mạng chết"));

        var result = await Client(handler).SendAsync(ThietBi(Guid.NewGuid()), Tin(), CancellationToken.None);

        Assert.Equal(PushDeliveryResult.Failed, result);
    }

    // -------------------------------------------------------------- WebPushSender

    [Fact]
    public async Task Thiet_bi_chet_bi_don_khoi_store_con_thiet_bi_song_thi_giu()
    {
        var userId = Guid.NewGuid();
        var store = Store();
        var song = ThietBi(userId, endpoint: "https://fcm.googleapis.com/fcm/send/song");
        var chet = ThietBi(userId, endpoint: "https://fcm.googleapis.com/fcm/send/chet");
        store.Upsert(song, out _);
        store.Upsert(chet, out _);

        var handler = new FakeHttpMessageHandler(request => new HttpResponseMessage(
            request.RequestUri!.ToString().Contains("chet", StringComparison.Ordinal) ? HttpStatusCode.Gone : HttpStatusCode.Created));
        var sender = Sender(handler, store);

        await sender.SendToUserAsync(userId, Tin());

        // Thiết bị "sống" vẫn còn, thiết bị "chết" đã bị dọn — và KHÔNG có ngoại lệ nào thoát ra.
        Assert.Single(store.ListForUser(userId));
        Assert.Contains(store.ListForUser(userId), d => d.Id == song.Id);
    }

    [Fact]
    public async Task Loi_mang_khong_lam_mat_thiet_bi()
    {
        var userId = Guid.NewGuid();
        var store = Store();
        var device = ThietBi(userId);
        store.Upsert(device, out _);

        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("mạng chết"));
        var sender = Sender(handler, store);

        await sender.SendToUserAsync(userId, Tin());

        Assert.Single(store.ListForUser(userId));
    }

    [Fact]
    public async Task Nguoi_dung_khong_co_thiet_bi_nao_la_noop_thanh_cong()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("không được gọi"));
        var sender = Sender(handler, Store());

        await sender.SendToUserAsync(Guid.NewGuid(), Tin());

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Gui_toi_thiet_bi_khong_ton_tai_tra_false_va_khong_goi_mang()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("không được gọi"));
        var userId = Guid.NewGuid();
        var sender = Sender(handler, Store());

        Assert.False(await sender.SendToDeviceAsync(userId, Guid.NewGuid(), Tin()));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Gui_toi_thiet_bi_dang_co_tra_true()
    {
        var userId = Guid.NewGuid();
        var store = Store();
        var device = ThietBi(userId);
        store.Upsert(device, out _);
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Created));
        var sender = Sender(handler, store);

        Assert.True(await sender.SendToDeviceAsync(userId, device.Id, Tin()));
        Assert.Single(handler.Requests);
    }
}

/// <summary>Handler giả: ghi lại mọi request rồi trả kết cục do test chọn.</summary>
internal sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(respond(request));
    }
}
