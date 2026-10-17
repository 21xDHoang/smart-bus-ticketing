# API gọi cổng thanh toán để hoàn tiền — MoMo / VNPay / ZaloPay

> Viết cho task **Sprint 4, US 8** *"API gọi cổng thanh toán để hoàn tiền (MoMo/VNPay/ZaloPay)"*
> — **Trần Trung Hiếu** (Chủ API). Đây là phần *"nối cổng thật"* của seam
> `IRefundGatewayCaller` mà các task trước để lại: bảng `Refunds`/`RefundLogs` (Dăm), service thực
> hiện lượt gọi + giành dòng `xmin` (Hoàng, dòng 30), job thử lại theo lịch (Hoàng, dòng 30), API tra
> cứu (Dăm, dòng 31) đều đã dựng trên một `PlaceholderRefundGatewayCaller` luôn trả `Unresolved`.
> Nhánh này thay đúng chỗ nối đó bằng cài đặt thật — hợp đồng đã ghi sẵn ở doc-comment
> `Services/IRefundGatewayCaller.cs`.
>
> 🟢 **Trạng thái:** đã dựng, đã kiểm bằng 39 ca test mới + toàn bộ bộ test (xem §5).
> ⚠️ Chưa chạy với sandbox thật — nhóm chưa có khoá đối tác MoMo/ZaloPay, và sandbox VNPay hạn chế
> hoàn tiền (§4).

## 1. Đã dựng những gì

| File | Vai trò |
|---|---|
| `Services/IRefundGatewayClient.cs` | Interface chung cho ba client hoàn tiền — nhận trọn dữ liệu giao dịch gốc (`Payment`) và dịch kết luận cổng sang `RefundGatewayCallOutcome` |
| `Services/RefundGatewayCaller.cs` | Cài đặt thật của `IRefundGatewayCaller`: đọc lại khoản hoàn + giao dịch gốc, tra client theo `MethodCode`, bọc lỗi vận chuyển thành `Unresolved` |
| `Services/MoMoRefundClient.cs` | `POST {Endpoint}/v2/gateway/api/refund` — chữ ký HMAC-SHA256 bộ trường hoàn tiền |
| `Services/VnPayRefundClient.cs` | `POST {RefundEndpoint}` với `vnp_Command=refund`, `vnp_TransactionType=02` — chữ ký HMAC-SHA512 nối '|' theo thứ tự tài liệu cổng |
| `Services/VnPayRefundOptions.cs` | Cấu hình riêng cho endpoint hoàn VNPay (cùng section `VnPay`, không thêm bí mật mới) — vì `VnPayOptions` hiện có chỉ phục vụ chiều thu tiền |
| `Services/ZaloPayRefundClient.cs` | `POST {Endpoint}/v2/refund` — MAC HMAC-SHA256 bằng Key1, `m_refund_id` ổn định theo `RefundId` |
| `Program.cs` (5 dòng) | Đổi đăng ký `IRefundGatewayCaller` từ Placeholder sang `RefundGatewayCaller` + `Configure<VnPayRefundOptions>` + 3 `AddHttpClient` |

Đều là **file mới của Hiếu**, trừ 5 dòng trong `Program.cs` (file của Hoàng — hợp đồng seam đã uỷ
quyền sẵn *"nối cổng thật = thay đúng một dòng đăng ký trong Program.cs"*; nhờ Hoàng xem qua trong PR).
Class `PlaceholderRefundGatewayCaller` giữ nguyên ở `Services/` (không còn được đăng ký) — file của
Hoàng, Hoàng quyết xoá hay giữ.

## 2. Ba quyết định thiết kế — và vì sao

### 2.1 Hoàn qua ĐÚNG kênh đã thu, tra client theo `MethodCode` của giao dịch gốc

`RefundGatewayCallRequest.MethodCode` là phương thức của **giao dịch gốc** (`Payments.MethodCode`),
không có tham số chọn kênh từ người dùng (đã chốt ở `docs/40` §2.5). `BankCard` là kênh thẻ của VNPay
nên lệnh hoàn cũng đi qua API hoàn của VNPay — cùng cách `BankCardGatewayAdapter` tái dùng client
VNPay ở chiều thu.

Tra client qua `IServiceProvider` theo mã (đúng lối `PaymentGatewayResolver`): máy chỉ cấu hình một
cổng thì chỉ client cổng đó được dựng — client cổng khác ném lỗi thiếu khoá (luật 2) không kéo sập
cả dây chuyền.

### 2.2 "Không bao giờ đoán" — ba kết cục, mỗi cổng dịch theo câu trả lời của chính nó

| Cổng | Trả lời gì | Kết cục |
|---|---|---|
| MoMo | `resultCode = 0` | `Succeeded` — `GatewayTransactionId` = `transId` của response |
| MoMo | `resultCode != 0` | `Failed` — kèm lời cổng |
| VNPay | `vnp_ResponseCode = 00` (trừ `vnp_TransactionStatus = 09`) | `Succeeded` — `GatewayTransactionId` = `vnp_TransactionNo` của response |
| VNPay | `vnp_ResponseCode = 00` + `vnp_TransactionStatus = 09` | `Failed` — cổng từ chối hoàn |
| VNPay | `vnp_ResponseCode = 94` | `Unresolved` — lệnh hoàn đã tồn tại trên cổng (lượt trước tới nơi), kết quả cuối chưa biết |
| VNPay | code khác | `Failed` — kèm `vnp_Message` |
| ZaloPay | `return_code = 1` | `Succeeded` — `GatewayTransactionId` = `refund_id` |
| ZaloPay | `return_code = 2` | `Failed` — kèm `return_message`/`sub_return_message` |
| ZaloPay | `return_code = 2` + `sub_return_code = -23` (`DUPLICATE_REFUND`) | `Unresolved` — lệnh hoàn đã tồn tại trên cổng |
| ZaloPay | `return_code = 3` (đang xử lý) hoặc mã lạ | `Unresolved` — kết quả cuối chỉ biết qua query_refund |
| Cả ba | không trả lời (timeout, đứt kết nối, body rác) | `Unresolved` — `RefundGatewayCaller` bọc mọi ngoại lệ vận chuyển |

Điểm mấu chốt: **`Unresolved` giữ khoản nguyên `Pending`, không xếp lịch thử lại** (đã chốt ở
`docs/40` §3.2 — gọi lại một lượt chưa rõ kết quả là đường dẫn tới hoàn hai lần). Những ca "lệnh đã
tồn tại trên cổng" (VNPay 94, ZaloPay -23, ZaloPay "đang xử lý") cố ý đưa về `Unresolved` chứ không
phải `Failed`: chúng là bằng chứng lượt trước **đã tới nơi**, thử lại chỉ tạo thêm rủi ro. Bước đối
soát (dòng 43) là nơi duy nhất tra cho ra nhẽ bằng sao kê cổng.

### 2.3 Khoá chống hoàn trùng — mỗi cổng một cơ chế, đều ghim vào `RefundId`

| Cổng | Khoá | Vì sao |
|---|---|---|
| MoMo | `requestId = RefundId` (idempotency key của cổng) | gửi lại với cùng requestId được cổng trả kết quả lượt trước |
| VNPay | `vnp_RequestId` mới mỗi lượt (cổng bắt không lặp trong ngày) | chống trùng nằm ở chính cổng: code 94 khi cùng `vnp_TxnRef` đã có lệnh hoàn |
| ZaloPay | `m_refund_id` ỔN ĐỊNH theo `RefundId` (`{yymmdd}_{appId}_{32 ký tự}`, 44/45 ký tự) | ZaloPay cho phép hoàn NHIỀU lần trên một giao dịch — sinh mã mới mỗi lượt thử là một lệnh hoàn mới, tức hoàn hai lần |

## 3. Chi tiết theo từng cổng

### 3.1 MoMo — `POST {Endpoint}/v2/gateway/api/refund`

- Chuỗi ký: `accessKey|amount|description|orderId|partnerCode|requestId|transId` xếp **theo bảng chữ
  cái**, HMAC-SHA256, hex chữ thường — bộ trường KHÁC chiều thu tiền (có `description`/`transId`,
  không có `ipnUrl`/`redirectUrl`/`requestType`).
- `orderId` = `RefundCode` — tài liệu cổng bắt orderId của lệnh hoàn **khác** orderId của giao dịch
  mua (`PaymentCode`).
- `transId` = `Payment.GatewayTransactionId` của lượt thu.
- Timeout 30 giây — con số TỐI THIỂU cổng ghi trong tài liệu; đặt ngắn hơn là tự cắt mất câu trả lời.

### 3.2 VNPay — `POST {RefundEndpoint}` (`vnp_Command=refund`)

- Chuỗi ký **KHÁC chiều thu tiền**: không xếp bảng chữ cái, không urlencode — nối `|` theo đúng thứ
  tự tài liệu cổng, HMAC-SHA512, hex chữ HOA:
  `vnp_RequestId|vnp_Version|vnp_Command|vnp_TmnCode|vnp_TransactionType|vnp_TxnRef|vnp_Amount|vnp_TransactionNo|vnp_TransactionDate|vnp_CreateBy|vnp_CreateDate|vnp_IpAddr|vnp_OrderInfo`
- `vnp_Amount` = số tiền **gốc** × 100 (đơn vị nhỏ nhất); `vnp_TransactionType = 02` (hoàn toàn phần —
  US 8 không có hoàn một phần).
- `vnp_TransactionDate` = `Payment.PaidAt` sang GMT+7 — mốc **thu tiền gốc**, không phải giờ gọi hoàn.
- `vnp_TransactionNo` (response) là mã giao dịch hoàn phía VNPay — vào `GatewayTransactionId`.
- Endpoint đọc từ `VnPay__RefundEndpoint` (mặc định sandbox) — production đổi qua cấu hình vì URL
  thật khác host (`merchant.vnpayment.vn`), không suy ra được từ `PaymentUrl`.

### 3.3 ZaloPay — `POST {Endpoint}/v2/refund`

- MAC: HMAC-SHA256 bằng **Key1** trên `app_id|zp_trans_id|amount|description|timestamp` (giá trị gốc,
  không urlencode) — đúng lối `ZaloPayGatewayService.CreateOrderAsync`.
- `zp_trans_id` = `Payment.GatewayTransactionId` của lượt thu; `app_id` gửi dạng **số** (int32).
- `m_refund_id` xem §2.3; `description = "Hoan tien {RefundCode}"` (≤100 ký tự).

## 4. Câu hỏi "cổng sandbox nào hoàn tiền được" — đã tra, cập nhật vào hợp đồng

Câu hỏi này từng được ghi là *"chặn dòng 40"* trong `docs/api-contract.md` (mục "Bốn câu hỏi nhóm
phải chốt") và `docs/40` §7.5. Nhánh này đã tra và ghi lại kết quả:

| Cổng | Sandbox có API hoàn? | Ghi chú |
|---|---|---|
| MoMo | ✅ Có | `test-payment.momo.vn/v2/gateway/api/refund`; có bộ khoá test công khai; cổng yêu cầu timeout tối thiểu 30s |
| ZaloPay | ✅ Có | `sb-openapi.zalopay.vn/v2/refund`; bộ khoá test công khai (app 2553); hoàn là API **bất đồng bộ** — kết quả cuối qua `query_refund` |
| VNPay | ⚠️ Hạn chế | API tồn tại (`merchant_webapi/api/transaction`), nhưng tài khoản sandbox **mặc định không bật** hoàn tiền — cần liên hệ VNPAY mở |

Hệ quả cho kiểm thử chéo (dòng 38/49 của bảng phân công): luồng hoàn thật chạy được với MoMo/ZaloPay
khi nhóm có khoá đối tác; với VNPay thì chỉ chạy được bằng HTTP giả trong test cho tới khi VNPAY mở
chức năng — **nhóm cần chốt cách xử lý với VNPay** (liên hệ VNPAY, hoặc chấp nhận kiểm thử bằng mock).

## 5. Đã kiểm và ĐẠT

Toàn bộ offline — không cuộc gọi mạng nào, cùng lối `PaymentSandboxCrossLayerTests`:

| Hạng mục | Kết quả |
|---|---|
| `dotnet build backend/SmartBus.Api` | ✅ 0 warning, 0 error |
| 39 ca test mới (`MoMoRefundClientTests` 7 · `VnPayRefundClientTests` 9 · `ZaloPayRefundClientTests` 8 · `RefundGatewayCallerTests` 15) | ✅ 39/39 |
| `dotnet test backend/SmartBus.Tests` (toàn bộ) | ✅ **1581/1581** — hai lượt chạy đầu có một ca `SeatHoldRaceFlowApiTests` đỏ do phụ thuộc thời điểm (đã ghi ở `docs/40` §8.3), chạy lại một mình và chạy lại toàn bộ đều xanh; nhánh này không đụng SeatHolds |
| Chữ ký MoMo hoàn tiền | ✅ vector tính sẵn bằng Python (độc lập với code C#) |
| MAC ZaloPay hoàn tiền | ✅ vector tính sẵn bằng Python |
| Chữ ký VNPay hoàn tiền | ✅ tính lại trong test từ thân request theo thứ tự tài liệu cổng (không vector cố định vì `vnp_RequestId` sinh mới mỗi lượt) |
| Tra đúng client theo `MethodCode` | ✅ cả 4 mã (MoMo/VNPay/ZaloPay/BankCard) + mã lạ → `Failed` không gọi HTTP |
| Chạy trọn luồng qua `RefundAttemptService` | ✅ khoản Pending → Succeeded + đúng 1 dòng `RefundLog`, mã giao dịch hoàn vào đúng cột |
| `Unresolved` không chạm CSDL | ✅ khoản giữ nguyên Pending, không dòng log nào |
| DI của app thật | ✅ `IRefundGatewayCaller` resolve ra `RefundGatewayCaller` (TestAppFactory) |
| Bí mật bị commit | ✅ không — chỉ khoá giả / khoá sandbox công khai đã có sẵn trong repo (luật 2) |

⚠️ **Giới hạn của bộ test** (giữ nguyên cảnh báo `docs/40` §4.4): InMemory không cưỡng chế `xmin`,
unique index lẫn FK — các chốt đó chỉ tồn tại thật ở PostgreSQL. Ca "hoàn hai lần do hai luồng cùng
gọi" chỉ kiểm được trên CSDL thật.

⚠️ **Phân tích đồ thị GitNexus KHÔNG chạy được trên máy này** (repo không có `.gitnexus/` — cùng giới
hạn đã ghi ở `docs/40` §8.5). Bù bằng: đọc trực tiếp các file seam (`IRefundGatewayCaller.cs`,
`RefundAttemptService.cs`, `RefundGatewayCallRequest.cs`) trước khi dựng + `dotnet build` + toàn bộ
bộ test ở trên. Ai có máy chạy được GitNexus nên chạy `impact` cho `Program.cs` trước khi merge.

## 6. Việc còn lại của người khác

- **Hoàng** — xem lại 5 dòng trong `Program.cs` (file của Hoàng); quyết giữ/xoá
  `PlaceholderRefundGatewayCaller` và cập nhật doc-comment của nó (hiện vẫn ghi "chưa nối cổng thật").
- **Kiên (dòng 28)** — service hoàn tiền tự động chưa dựng: khi duyệt huỷ vé / giao dịch hỏng, nó tạo
  khoản `Refund` rồi gọi `IRefundAttemptService` với trigger `Initial` — lúc đó luồng gọi cổng thật
  trong nhánh này mới có người gọi đầu tiên.
- **Kiên (dòng 43)** — bước đối soát hằng ngày là nơi duy nhất xử lý các khoản kẹt ở `Pending` do
  `Unresolved`; với ZaloPay còn có thêm API `query_refund` để tra kết quả cuối của lệnh đang xử lý.
- **Giàng A Vàng (dòng 37)** — test API hoàn tiền tự động + retry vẫn đứng như bảng phân công; các ca
  ở §5 là test của chính task này, không thay thế task đó.
- **Nhóm** — chốt câu hỏi VNPay sandbox (§4) ở mục "Bốn câu hỏi nhóm phải chốt" của hợp đồng.
