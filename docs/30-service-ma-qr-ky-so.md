# Service sinh mã QR vé + ký số chống làm giả (dòng 39, US 4, Sprint 3)

> Task dòng 39 — backlog giao **Nguyễn Duy Kiên**; bản dựng này do **Phùng Duy Hoàng** thực hiện
> theo yêu cầu nhóm để gỡ nút phát hành vé. Service **không có endpoint, không chạm CSDL** — chỉ
> hai phép thuần trên chuỗi: sinh mã, kiểm mã.
>
> Đọc kèm: `docs/28-csdl-ve-dien-tu.md` §2/§6 (ngân sách cột `Tickets.Code` + hai ràng buộc CSDL),
> `docs/29-ban-giao-api-kiem-tra-hieu-luc-qr.md` (endpoint soát vé dòng 43 — nơi mã này được kiểm).

## 1. Đã có gì trên nhánh này

| File | Việc |
|---|---|
| `backend/SmartBus.Api/Services/TicketQrOptions.cs` | Section cấu hình `TicketQr` + `MissingPiece()` |
| `backend/SmartBus.Api/Services/ITicketQrService.cs` | Hợp đồng hai chiều: `GenerateCode` / `TryVerify` |
| `backend/SmartBus.Api/Services/TicketQrService.cs` | Cài đặt — HMAC-SHA256, nonce ngẫu nhiên, FixedTimeEquals |
| `backend/SmartBus.Api/Program.cs` | Hai dòng đăng ký DI (cuối danh sách, sau khối voucher) |
| `backend/SmartBus.Api/appsettings.Development.json.example` | Mục `TicketQr` — khuôn điền khoá |
| `backend/SmartBus.Tests/TicketQrServiceTests.cs` | 14 ca — định dạng, duy nhất, chống giả, rác, luật 2 |
| `backend/SmartBus.Tests/TicketQrWiringTests.cs` | 2 ca chạy trên app thật — xoá dòng DI là đỏ |

## 2. Định dạng mã — chốt

```
SBT1:{ticketId}.{chuỗiBase64}
└─5─┘ └──36──┘└1┘└───64───┘  = 106 ký tự
```

| Phần | Nội dung | Dài |
|---|---|---|
| `SBT1:` | Tiền tố phiên bản. Mã lạ tiền tố bị từ chối thẳng; đổi cách ký sau này thì mở `SBT2:` | 5 |
| `{ticketId}` | Guid dạng `D` — chuẩn hoá, không nhận kiểu khác | 36 |
| `.` | Dấu phân cách (Guid và Base64Url đều không chứa `.`) | 1 |
| blob | `Base64Url(nonce 16 byte ‖ HMAC-SHA256 32 byte)` — RFC 4648 §5, không padding | 64 |

**Chuỗi đem ký** = `SBT1:{ticketId:D}.{Base64Url(nonce)}` — dựng lại được từ nonce nằm trong 16
byte đầu của blob nên không cần gửi kèm. Chữ ký nằm trong blob, mã ngoài chỉ có đúng một dấu chấm.

**Ngân sách ký tự:** 106 — trong khoảng 90–130 ghi ở docs/28 §2, dưới hạn 200 của cột `Code`. Test
ghim đúng 106 (cả vector dựng tay lẫn mã sinh thật): đổi định dạng là test đỏ trước khi cột tràn.

### Vì sao nonce (và vì sao KHÔNG bỏ nó để mã ngắn hơn)

1. **Đường lùi lượt đâm unique index** (docs/28 §6): mã sinh theo vé là thứ ghi được rồi mới biết
   đâm; nonce bảo đảm gọi lại `GenerateCode` là ra mã mới — bên phát hành không phải chế gì thêm.
2. Không bao giờ trùng mã giữa hai vé, kể cả hai lượt sinh cho cùng một vé.

### Vì sao HMAC đối xứng (không phải chữ ký bất đối xứng)

Bên kiểm là server của mình (API soát vé Sprint 4 dòng 15 / dòng 43 — docs/29 §3), máy quét chỉ gửi
chuỗi lên; khoá không bao giờ rời server nên đối xứng là đủ và đơn giản nhất. Khi nào có máy quét
tự kiểm offline không cần mạng thì mới cần bất đối xứng — lúc đó mở tiền tố `SBT2`, mã cũ vẫn phân
biệt được nhờ tiền tố.

## 3. Cách dùng

### 3.1 Phía phát hành vé (Hiếu — sau khi tiền về, trước khi lưu vé)

```csharp
var code = qrService.GenerateCode(ticket.Id);        // 106 ký tự
ticket.Code = code;
await db.SaveChangesAsync();
```

- Lượt ghi đâm unique `IX_Tickets_Code` (cực hiếm — kể cả trùng ngẫu nhiên cũng ~2⁻²⁵⁶): bắt
  `DbUpdateException` có inner là lỗi unique của Npgsql → gọi lại `GenerateCode` → ghi lại. **Không
  dùng lại mã của lượt vừa đâm.**
- In QR từ `ticket.Code` **đã lưu** — không sinh lại lúc hiển thị (mỗi lượt sinh ra mã khác, vé in
  sẽ không khớp mã đã kiểm).
- Vé cũ đang tạm dùng `Guid` (docs/api-contract.md bước 2): `TryVerify` từ chối thẳng (không tiền
  tố) nên vé cũ không lẫn với vé mới.

### 3.2 Phía soát vé (dòng 43 / Sprint 4 dòng 15)

```csharp
if (!qrService.TryVerify(request.Code, out var ticketId))
{
    // Chữ ký sai: KHÔNG phân biệt "mã rác" với "vé bị làm giả" ra ngoài (người soát chỉ cần một
    // câu trả lời: không cho lên xe); log nguyên văn mã để điều tra sau. Với docs/29 §3: "NotFound".
    return ...;
}

// Chữ ký hợp lệ → mới tra vé (theo Id chính id đã ký — bỏ được một phép so chuỗi 106 ký tự),
// rồi xét Status / UsedAt / chuyến (docs/29 §3).
var ticket = await db.Tickets.SingleOrDefaultAsync(t => t.Id == ticketId);
```

⚠️ **Đừng tin `ticketId` trước khi `TryVerify` trả `true`** — mã là chuỗi người ngoài dán vào request.

### 3.3 Cấu hình (luật 2)

| Môi trường | Đặt ở đâu |
|---|---|
| Máy dev | `dotnet user-secrets set "TicketQr:SigningKey" "<chuỗi ≥ 32 ký tự>"` |
| Deploy | Biến môi trường `TicketQr__SigningKey` |
| Khuôn | Mục `TicketQr` trong `appsettings.Development.json.example` |

- Thiếu khoá → service ném ngay khi dựng, nêu đúng `TicketQr:SigningKey` (cùng lối VnPay/ZaloPay).
- **Đổi khoá = mọi vé đã phát hành hỏng chữ ký soát.** Muốn đổi phải ký lại vé cũ hoặc chấp nhận vé
  cũ hỏng — báo nhóm trước, không tự làm trên môi trường đang chạy.

## 4. Test

```
dotnet test backend/SmartBus.Tests/SmartBus.Tests.csproj --filter "FullyQualifiedName~TicketQr"
```

16 ca, ghim những thứ dễ hỏng thầm lặng: định dạng + độ dài 106 (đổi là cột tràn/không khớp), hai
lượt sinh khác nhau (bỏ nonce là lượt đâm mất đường lùi), payload dựng tay theo đúng chuỗi ký (đổi
tiền tố / dạng Guid / thứ tự nonce-chữ ký / padding là đỏ), đổi id giữ chữ ký (gian lận kinh điển),
ký bằng khoá khác, chín chuỗi rác, khoảng trắng thừa, thiếu khoá.

## 5. Việc còn lại — không thuộc service này

| Việc | Ai |
|---|---|
| Nối `GenerateCode` vào service phát hành vé + xử lý lượt đâm unique | Hiếu (docs/28 §6) |
| Endpoint soát vé: `TryVerify` → tra `Tickets` → `valid/reason` | Hiếu (dòng 43 chờ chốt chuyển từ Dăm — docs/29 §1; hình dạng chuẩn ở Sprint 4 dòng 15) |
| `docs/29` §5 ghi "Chưa có file nào trong `Services/`" — nay đã có; dòng phụ thuộc đó hết chặn, tài liệu của Dăm nên để Dăm cập nhật | Dăm (nếu muốn) |
