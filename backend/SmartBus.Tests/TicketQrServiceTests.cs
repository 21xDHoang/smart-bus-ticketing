using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using SmartBus.Api.Services;

namespace SmartBus.Tests;

/// <summary>
/// Test cho <see cref="TicketQrService"/> — sinh mã QR duy nhất + ký số chống làm giả (US 4, Sprint 3,
/// task *"Service sinh mã QR duy nhất + ký số chống làm giả"* — Nguyễn Duy Kiên).
///
/// Mọi phép sinh/kiểm là hàm thuần — không CSDL, không mạng, không thời gian — nên không cần HTTP
/// giả lẫn InMemory, cùng lối "hàm thuần" của <see cref="VnPayGatewayServiceTests"/>.
///
/// Không có vector tính sẵn kiểu VNPay vì nonce ngẫu nhiên nên mã đổi mỗi lượt; thay vào đó
/// <see cref="Verify_chu_ky_dung_chuan_dung_bang_tay"/> tự DỰNG payload hoàn toàn bằng tay từ các
/// nguyên hàm (HMACSHA256 + Base64Url) — ghim độc lập đúng định dạng đường dây: tiền tố, dạng Guid
/// "D", thứ tự nonce ‖ chữ ký trong blob, bảng mã Base64Url không padding, và chuỗi đem ký.
/// </summary>
public class TicketQrServiceTests
{
    // Khoá giả rõ ràng của test — không phải khoá thật (luật 2).
    private const string SigningKey = "khoa-ky-gia-cua-test-dai-hon-32-ky-tu-abc";

    // ---- Sinh mã ------------------------------------------------------------

    [Fact]
    public void Sinh_ma_dung_dinh_dang_SBT1_trong_ngan_sach_do_dai()
    {
        var id = Guid.NewGuid();

        var code = ServiceWithDefaults().GenerateCode(id);

        Assert.StartsWith("SBT1:", code);

        var parts = code.Split('.');
        Assert.Equal(2, parts.Length);
        Assert.Equal($"SBT1:{id:D}", parts[0]);

        // Blob chỉ được chứa bảng chữ Base64Url (không '+', '/', '=') — có padding hay ký tự đặc
        // biệt là lúc dán vào URL phải mã hoá lại, thêm một chỗ để sai.
        Assert.All(parts[1], c => Assert.True(
            (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_',
            $"Base64Url chỉ gồm A-Za-z0-9-_ — gặp '{c}'"));

        // 5 tiền tố + 36 Guid + 1 dấu chấm + 64 blob (48 byte ÷ 3 × 4) = 106. Ghim đúng con số: cột
        // Code cho 200 (docs/28 §2) nhưng ngân sách thiết kế ghi 90–130 — lệch là đổi định dạng.
        Assert.Equal(106, code.Length);
        Assert.InRange(code.Length, 90, 130);
        Assert.True(code.Length <= 200);
    }

    [Fact]
    public void Sinh_hai_lan_cho_cung_ve_ra_hai_ma_khac_nhau()
    {
        var id = Guid.NewGuid();
        var service = ServiceWithDefaults();

        // CHỦ ĐÍCH (nonce): hai lượt gọi cùng một vé phải ra hai mã khác nhau — đó là đường lùi khi
        // lượt ghi đâm chỉ mục UNIQUE của cột Code: bắt lỗi, gọi lại GenerateCode, ghi lại (docs/28 §6).
        // Test này đỏ nghĩa là nonce bị bỏ — lượt đâm mất đường lùi.
        Assert.NotEqual(service.GenerateCode(id), service.GenerateCode(id));
    }

    // ---- Kiểm mã ------------------------------------------------------------

    [Fact]
    public void Verify_ma_vua_sinh_tra_dung_ticketId()
    {
        var service = ServiceWithDefaults();
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();

        var codeA = service.GenerateCode(idA);
        var codeB = service.GenerateCode(idB);

        Assert.True(service.TryVerify(codeA, out var fromA));
        Assert.True(service.TryVerify(codeB, out var fromB));

        // Id trả về phải là id ĐÃ KÝ, không lẫn giữa hai vé.
        Assert.Equal(idA, fromA);
        Assert.Equal(idB, fromB);
    }

    [Fact]
    public void Verify_chu_ky_dung_chuan_dung_bang_tay()
    {
        // Vector định dạng: payload dựng HOÀN TOÀN bằng tay từ nguyên hàm — không đụng GenerateCode —
        // nên đổi bất kỳ mảnh nào của đường dây (tiền tố, dạng Guid, chuỗi đem ký, thứ tự nonce-chữ
        // ký, padding Base64) là test đỏ ngay. Cũng là chốt chống hồi quy cho bản vá 106 ký tự:
        // blob phải là nonce ‖ chữ ký, mã ngoài CHỈ có một dấu chấm.
        var id = Guid.Parse("3f8c1d2e-4b5a-4c6d-8e7f-9012345678ab");
        var nonce = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };

        var signedPart = $"SBT1:{id:D}.{Base64Url.EncodeToString(nonce)}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(SigningKey));
        var signature = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPart));

        var blob = nonce.Concat(signature).ToArray();
        var payload = $"SBT1:{id:D}.{Base64Url.EncodeToString(blob)}";

        Assert.Equal(106, payload.Length);
        Assert.True(ServiceWithDefaults().TryVerify(payload, out var ticketId));
        Assert.Equal(id, ticketId);
    }

    [Fact]
    public void Verify_doi_ticketId_giu_nguyen_chu_ky_tra_false()
    {
        var service = ServiceWithDefaults();
        var code = service.GenerateCode(Guid.NewGuid());

        // Đổi id trong mã, giữ nguyên blob (nonce + chữ ký cũ) — gian lận kinh điển: chữ ký gắn cứng
        // vào id nên phải sập.
        var blob = code.Split('.')[1];
        var forged = $"SBT1:{Guid.NewGuid():D}.{blob}";

        Assert.False(service.TryVerify(forged, out var ticketId));
        Assert.Equal(Guid.Empty, ticketId);
    }

    [Fact]
    public void Verify_sua_mot_ky_tu_trong_chu_ky_tra_false()
    {
        var service = ServiceWithDefaults();
        var code = service.GenerateCode(Guid.NewGuid());

        // Lật một ký tự Base64Url cuối (blob vẫn đủ 48 byte sau giải mã, chỉ một bit chữ ký đổi) —
        // FixedTimeEquals phải sập.
        var last = code[^1];
        var flipped = code[..^1] + (last == 'A' ? 'B' : 'A');

        Assert.False(service.TryVerify(flipped, out _));
    }

    [Fact]
    public void Verify_ky_bang_khoa_khac_tra_false()
    {
        var code = ServiceWithDefaults().GenerateCode(Guid.NewGuid());

        // Mã y nguyên nhưng bên kiểm cầm khoá khác — ví dụ khoá lộ ra cho người ngoài dựng lại hệ.
        var other = new TicketQrService(Options.Create(new TicketQrOptions
        {
            SigningKey = "MOT_KHOA_HOAN_TOAN_KHAC_KHONG_PHAI_CUA_HE_THONG",
        }));

        Assert.False(other.TryVerify(code, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Verify_chuoi_rong_tra_false(string? code)
    {
        Assert.False(ServiceWithDefaults().TryVerify(code, out var ticketId));
        Assert.Equal(Guid.Empty, ticketId);
    }

    [Fact]
    public void Verify_ma_rac_tra_false()
    {
        var service = ServiceWithDefaults();
        var id = Guid.NewGuid();
        var code = service.GenerateCode(id);
        var blob = code.Split('.')[1];

        var rac = new[]
        {
            $"SBT1:{id:D}",                          // thiếu hẳn blob
            "SBT1:",                                  // đúng tiền tố, rỗng ruột
            $"SBT1:khong-phai-guid.{blob}",           // id không parse được
            $"XYZ1:{id:D}.{blob}",                    // sai tiền tố phiên bản
            $"{id:D}.{blob}",                         // thiếu tiền tố
            code[..^1],                               // cụt một ký tự — blob thiếu byte
            $"SBT1:{id:D}.!!!khong-phai-base64!!!",   // ký tự ngoài bảng Base64Url
            $"SBT1:{id:D}.AAAA",                      // blob quá ngắn, không đủ nonce + chữ ký
            code.ToLowerInvariant(),                  // sai hoa/thường — QR đọc đúng nguyên văn
        };

        foreach (var candidate in rac)
        {
            Assert.False(service.TryVerify(candidate, out var ticketId), $"Chuỗi rác phải bị từ chối: {candidate}");
            Assert.Equal(Guid.Empty, ticketId);
        }
    }

    [Fact]
    public void Verify_bo_qua_khoang_trang_thua_hai_dau()
    {
        var service = ServiceWithDefaults();
        var code = service.GenerateCode(Guid.NewGuid());

        // Dán từ clipboard hay máy quét thêm xuống dòng là chuyện thường.
        Assert.True(service.TryVerify($"  {code}\n", out var ticketId));
        Assert.NotEqual(Guid.Empty, ticketId);
    }

    // ---- Cấu hình thiếu (luật 2) --------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Thieu_SigningKey_thi_bao_dung_ten_khoa_thieu(string signingKey)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new TicketQrService(
            Options.Create(new TicketQrOptions { SigningKey = signingKey })));

        Assert.Contains("TicketQr:SigningKey", ex.Message);
        // Câu lỗi phải chỉ luôn đường điền khoá — người dựng máy mới không phải mò.
        Assert.Contains("user-secrets", ex.Message);
    }

    // ---- Dựng dữ liệu -------------------------------------------------------

    private static TicketQrService ServiceWithDefaults()
        => new(Options.Create(new TicketQrOptions { SigningKey = SigningKey }));
}
