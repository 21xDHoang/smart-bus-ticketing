# Đối soát hoàn tiền hằng ngày + cảnh báo khi lệch số liệu — Sprint 4

> **US 8** — *"Là hệ thống, tôi muốn tự động xử lý hoàn tiền theo quy định khi giao dịch thanh toán thất bại hoặc vé bị hủy hợp lệ."*
>
> Việc này là **dòng 32** sheet Sprint 4 — *"Đối soát hoàn tiền hằng ngày + cảnh báo khi lệch số liệu"*
> (Trung bình · 4 điểm · **Nguyễn Duy Kiên**). Các dòng chung quanh: **27–31** Dăm/Hoàng/Dăm/Hoàng/Hiếu
> (bảng, service, cổng, thử lại, API tra cứu) · **33–34** Hạnh/Hoàng (màn theo dõi, thông báo) ·
> **35–37** Băng/Vàng (màn Admin, test).
>
> 🟢 **Trạng thái**: nhánh `feature/32-doi-soat-hoan-tien-hang-ngay` đứng trên main `f469ec8`.
> **6 file mới ở backend + 2 file sửa (enum thông báo, `Program.cs`) + 4 file hạ tầng test + 3 file test
> mới**. Không mở endpoint nào — cảnh báo đi qua bảng `Notifications` và hub `notifications` đã có.
> PR này **không đụng `Migrations/`**, **không đụng frontend**.

---

## 1. Vì sao cần bước này

Ba job trước đã lo phần *đi* của đồng tiền: tạo khoản hoàn (`RefundService`, dòng 28), gọi cổng
(`RefundAttemptService`, dòng 29–30), thử lại theo lịch (`RefundRetryBackgroundService`, dòng 30).
Nhưng cả ba đều chỉ hành động trên **trạng thái đã ghi**. Có những ca trạng thái ghi sai hoặc không
bao giờ được ghi:

| Ca | Job nào bắt? | Hậu quả nếu không có đối soát |
| --- | --- | --- |
| Cổng đã trả tiền, lượt `SaveChanges` ghi kết quả thua (`DbUpdateConcurrencyException`) | không job nào | Dòng `Refunds` nằm `Pending`, `RefundLogs` giữ đúng một dòng `Succeeded`. **Không job nào gọi lại khoản Pending** — khách phải khiếu nại mới biết |
| Cổng không trả lời (`Unresolved`) rồi app tắt | không job nào | `Pending` vĩnh viễn, không lịch thử lại, không dấu vết ngoài bảng log |
| Mã giao dịch của lượt cuối khác mã của lượt đã trả tiền | không job nào | Không đối chiếu được với sao kê; có thể là dấu hiệu hoàn hai lần |
| Hai khoản hoàn cùng sống trên một giao dịch | chỉ mục `IX_Refunds_PaymentId_Active` (nếu có) | Nguy cơ hoàn hai lần nếu chỉ mục bị thiếu hoặc dữ liệu nạp tay |
| Khoản `Failed` có mốc thử lại đã quá hạn | không job nào (job thử lại chỉ nhặt khoản tới hạn) | Job thử lại chết mà không ai biết |

Bước đối soát là **lưới an toàn cuối**, không phải một đường xử lý tiền thứ hai. Nó **chỉ đọc**:
không gọi cổng, không lật trạng thái, không đặt lịch thử lại. Một khoản hoàn bị báo lệch vẫn phải do
người tra sao kê rồi quyết định — gọi lại một khoản cổng đã trả là hoàn hai lần.

---

## 2. Thành phần

| File | Làm gì |
| --- | --- |
| `Dtos/Refunds/RefundReconciliationReport.cs` | Báo cáo một lượt chạy + 3 enum: mức (`Warning`/`Critical`), loại điểm lệch (17 giá trị), một điểm lệch |
| `Services/IRefundReconciliationService.cs` | Hợp đồng `RunAsync(utcNow)` — nhận "bây giờ" từ người gọi để test ghim được mốc |
| `Services/RefundReconciliationService.cs` | Hai nhóm phép kiểm: **sổ nội bộ** (luôn chạy) và **sao kê cổng** (chỉ khi có dữ liệu) |
| `Services/IRefundStatementSource.cs` | Seam đọc sao kê + `UnavailableRefundStatementSource` trả `null` (chưa cổng nào có API tra cứu **hoàn tiền**) |
| `Services/IRefundReconciliationNotifier.cs` | Hợp đồng báo cho quản trị — **không bao giờ ném lỗi**, **không nhận `CancellationToken`** |
| `Services/RefundReconciliationNotifier.cs` | Ghi một dòng `Notifications` cho mỗi Admin/Manager đang hoạt động + đẩy SignalR |
| `Services/RefundReconciliationBackgroundService.cs` | Job chạy mỗi ngày lúc **00:05 UTC = 07:05 giờ Việt Nam** |
| `Entities/NotificationType.cs` | Thêm `RefundReconciliationAlert` — loại thứ chín, loại đầu tiên nhắm vào quản trị |
| `Program.cs` | 3 dòng `AddScoped` + 1 dòng `AddHostedService` (khối thêm vào, **nhờ Hoàng xem qua trong PR**) |
| `docs/api-contract.md` | "Tám loại" → **"Chín loại"**, thêm dòng `RefundReconciliationAlert`, ghi chú người nhận/`null` target/không số tiền |

Không có bảng mới, không có cột mới, **không cần migration**. `NotificationType` lưu dạng `varchar`
(A3) nên thêm giá trị enum không tốn migration — nhưng là thay đổi hợp đồng frontend, đã ghi ở
`api-contract.md` và mục §6 dưới.

---

## 3. Phạm vi quét — vì sao TOÀN BỘ bảng `Refunds`

Mỗi lượt nạp **toàn bộ** `Refunds` một lần (`AsNoTracking`, sắp theo `CreatedAt` rồi `Id`), rồi kiểm
bất biến trên tập con **đã chọn**:

```
Pending  ∨  CreatedAt ≥ now − 24h  ∨  UpdatedAt ≥ now − 24h
```

- **Mọi khoản `Pending`** — không job nào nhặt khoản Pending, kể cả khoản mắc kẹt từ tuần trước.
- **Khoản vừa chạm trong 24 giờ** — kể cả khoản cũ vừa được bấm "Thử lại" tay.
- **Khoản `Succeeded`/`Failed` cũ đã chốt thì không kiểm lại** — tránh cùng một kết luận lặp lại
  mỗi ngày 365 lần.

Bảng chỉ có một dòng cho mỗi giao dịch hỏng / mỗi yêu cầu huỷ được duyệt — nhỏ hơn hẳn bảng vé — và
job chạy một lần mỗi ngày, nên một lượt đọc toàn bảng là cái giá đúng. Nhờ vậy **không phải thêm chỉ
mục mới** (mọi chỉ mục của bảng thuộc Vàng Thị Dăm).

Riêng phép kiểm **hai khoản hoàn cùng sống trên một giao dịch** quét toàn bộ bảng bất kể khoảng thời
gian: một cặp trùng từ tuần trước vẫn là một cặp trùng.

---

## 4. Hai nhóm phép kiểm

### 4.1 Sổ nội bộ — luôn chạy

| Loại | Mức | Bắt ca gì |
| --- | --- | --- |
| `SuccessEvidenceOnOpenRefund` | 🔴 Critical | Nhật ký có lượt gọi **thành công** nhưng khoản hoàn chưa `Succeeded` — **điểm lệch quan trọng nhất của task** |
| `StuckPending` | 🔴 | `Pending` quá 24 giờ — không có đường tự động nào gọi lại |
| `SucceededRefundMissingEvidence` | 🔴 | `Succeeded` thiếu mã cổng / `ProcessedAt` / dòng log thành công |
| `SucceededRefundGatewayIdMismatch` | 🔴 | Mã trên dòng khác mã của lượt log thành công |
| `MultipleSucceededLogs` | 🔴 | Hơn một lượt báo thành công cho cùng khoản — nguy cơ hoàn hai lần |
| `MultipleActiveRefundsForPayment` | 🔴 | Hai khoản chưa `Failed` trên cùng giao dịch |
| `StatusFieldInvariantBroken` | 🔴 | `Pending` mang dấu vết phán quyết; `Succeeded` còn `FailReason`/`NextRetryAt` |
| `SourceInvariantBroken` | 🔴 | `TicketCancellation` thiếu yêu cầu huỷ/vé; `PaymentFailure` có `CancellationRequestId`; trạng thái giao dịch gốc sai nhánh; người nhận khác chủ giao dịch |
| `InvalidAmount` | 🔴 | Số tiền ≤ 0 hoặc lớn hơn giao dịch gốc |
| `MissingPayment` | 🔴 | Trỏ tới giao dịch không còn trong CSDL |
| `FailedRefundMissingReason` | 🟡 Warning | `Failed` không có lý do |
| `RetryOverdue` | 🟡 | Mốc thử lại quá hạn hơn 1 giờ — job thử lại có thể không chạy |
| `MissingAttemptLogs` | 🟡 | Đã chốt trạng thái mà không có dòng nhật ký nào |

> 🔴 **Không tự tính lại số tiền từ `Ticket.Price`.** `Refund.Amount` là con số **đã gửi lên cổng**;
> so nó với một công thức theo chính sách hôm nay sẽ báo lệch giả mỗi lần nhà xe đổi phí huỷ. Chỉ
> kiểm những gì không phụ thuộc chính sách: số dương và không vượt giao dịch gốc.

### 4.2 Sao kê cổng — chỉ chạy khi có dữ liệu

Khoá đối chiếu là **mã giao dịch hoàn phía cổng** (`Refunds.GatewayTransactionId`, bổ sung bằng mã
trong `RefundLogs` của khoảng sao kê — lượt thất bại vẫn có thể mang mã). So **không phân biệt hoa
thường** vì các cổng không thống nhất.

| Loại | Mức | Bắt ca gì |
| --- | --- | --- |
| `StatementMissingRefund` | 🔴 | Hệ thống ghi `Succeeded` trong kỳ nhưng sao kê không có lệnh đó |
| `StatementAmountMismatch` | 🔴 (🟡 khi cả hai bên đều nói thất bại) | Số tiền trên sao kê khác số đã gửi |
| `StatementStatusMismatch` | 🔴 | Hệ thống `Succeeded` nhưng sao kê ghi thất bại — **hoặc ngược lại: sao kê thành công nhưng khoản còn `Pending`/`Failed`** |
| `StatementUnknownRefund` | 🔴 nếu lệnh thành công, 🟡 nếu lệnh thất bại | Tiền đã rời cổng mà hệ thống không có dòng nào |

### 4.3 🔴 `null` ≠ sao kê rỗng

Bản chạy hiện tại **chưa có nguồn sao kê thật**: `MoMoGatewayService.QueryTransactionAsync` và
`ZaloPayGatewayService.QueryOrderAsync` tra **giao dịch thu tiền**, không tra lệnh hoàn; ZaloPay có
`query_refund` nhưng client hoàn tiền chưa dùng; VNPay sandbox giới hạn chức năng hoàn.

Vì vậy `IRefundStatementSource` trả **`null` = "không biết"**, còn một `RefundStatement` rỗng nghĩa là
"cổng xác nhận không có lệnh hoàn nào". Nhầm hai thứ này sẽ báo **mọi** khoản hoàn thành công trong
ngày là "không có trên sao kê" — cảnh báo giả hàng loạt. Báo cáo ghi rõ
`StatementAvailable = false`; không được suy ra "không lệch" từ chỗ không có dữ liệu.

Khi có cổng thật (hoặc muốn nhập file sao kê do cổng gửi), chỉ cần thêm một lớp cài
`IRefundStatementSource` và đổi **đúng một dòng** đăng ký trong `Program.cs` — service và notifier
không phải sửa.

---

## 5. Cảnh báo cho quản trị

### 5.1 Người nhận

Mọi tài khoản **Admin/Manager đang hoạt động** — xét **cả** vai trò chính `User.RoleId` **lẫn** bảng
nối `UserRoles`, đúng lối RBAC nhiều vai trò của dự án. Tài khoản đã khoá không đăng nhập được để đọc
cảnh báo, nên không nhận. Hành khách không thấy dòng tiền của cả hệ thống, nên không nhận.

### 5.2 Nội dung

- Tiêu đề: **"Đối soát hoàn tiền phát hiện lệch số liệu"**.
- Body: mốc chạy theo **giờ Việt Nam (`dd/MM/yyyy HH:mm`)**, tổng số điểm lệch, số điểm nghiêm trọng,
  tối đa **10 mã khoản hoàn** (nhiều hơn thì cắt và ghi `….`).
- **Không có số tiền** — cùng luật với mọi thông báo khác. Số tiền tra ở chính dòng `Refunds`; chép
  vào thông báo là dựng bản sao thứ hai có thể lệch. Chi tiết từng lỗi nằm trong log máy chủ.
- `RelatedEntityType`/`RelatedEntityId` = **null**: cảnh báo nói về nhiều khoản hoàn cùng lúc, gán một
  mã đại diện là nói dối về phạm vi.

### 5.3 Chính sách báo

| Tình huống | Hành vi |
| --- | --- |
| Không có điểm lệch | **Không** ghi thông báo nào (một dòng "hôm nay không lệch" mỗi ngày sẽ dạy người trực bỏ qua chuông) |
| Có lệch | Một dòng cho mỗi người nhận, cùng một `SaveChanges`, rồi đẩy SignalR từng người trong `try` riêng |
| Job chạy lại trong **cùng ngày** | Bỏ qua người đã có cảnh báo cùng loại trong ngày — khoá chống trùng `(người nhận, loại, ngày)` |
| Sang ngày mới | Báo lại bình thường |
| Đọc/ghi CSDL hỏng, kênh đẩy hỏng | **Nuốt lỗi**, ghi log, không ném ra ngoài |

> Vì sao khoá chống trùng theo **ngày** chứ không theo bộ điểm lệch: nội dung cảnh báo thay đổi mỗi
> lượt chạy khi có khoản mới, nhưng kết luận "đêm nay có lệch" thì không. Một cảnh báo mỗi ngày là
> đủ để có người mở bảng `Refunds` ra tra; nhiều hơn là nhiễu.

---

## 6. Việc còn lại của người khác

| Người | Việc | Vì sao không tự làm |
| --- | --- | --- |
| **Phùng Duy Hoàng** | Xem lại khối DI thêm vào `Program.cs` (3 `AddScoped` + 1 `AddHostedService`) | `Program.cs` là file của Hoàng; đã ghi rõ trong PR |
| **Nguyễn Đình Băng** | `src/api/notificationApi.ts`: thêm `RefundReconciliationAlert` vào union `NotificationType`. `NotificationBell`: nhãn/biểu tượng riêng cho cảnh báo vận hành; bấm vào mở màn quản lý hoàn tiền (không điều hướng theo `relatedEntityType` vì nó null) | `src/api/` và `App.tsx`/`main.tsx` là file của Băng. Hiện tại loại này rơi vào nhóm đỏ mặc định — **đúng hướng**, nhưng nhãn sẽ đọc thành "bị từ chối" nếu không thêm |
| **Vàng Thị Dăm** | Không cần migration. Nếu sau này thêm chỉ mục cho bảng `Refunds` (ví dụ theo `GatewayTransactionId`) thì đó là việc của Dăm | `Migrations/*` và `ModelSnapshot.cs` là file của Dăm |
| **Nguyễn Duy Kiên** | Khi có cổng trả sao kê hoàn tiền: cài `IRefundStatementSource` thật và đổi một dòng đăng ký | Seam đã sẵn; không có dữ liệu thật thì chưa làm được |

---

## 7. Kiểm thử

| File | Số bài | Ghim gì |
| --- | --- | --- |
| `RefundReconciliationServiceTests.cs` | 21 | Từng loại điểm lệch nội bộ; phạm vi quét; sao kê khớp/lệch/`null`/rỗng; nguồn sao kê ném lỗi vẫn giữ kết quả nội bộ |
| `RefundReconciliationNotifierTests.cs` | 11 | Lọc Admin/Manager đang hoạt động (vai trò chính + bảng nối); chống trùng theo ngày; không có lệch thì im; body không có số tiền; cắt danh sách mã; hai nhánh hỏng không ném lỗi |
| `RefundReconciliationWiringTests.cs` | 3 | Job đăng ký đúng một lần; service/notifier resolve được từ scope; nguồn mặc định là `UnavailableRefundStatementSource` (không phải sao kê rỗng) |
| `RefundReconciliationBackgroundServiceTests.cs` | 4 | Mốc 00:05 UTC; độ trễ trước/sau mốc; đúng mốc thì chờ trọn một ngày (không trả 0) |
| `JwtAuthTests.cs`, `TestHostKhongChayJobNenTests.cs`, `TicketValidationTestFactory.cs`, `SeatHoldRaceFlowApiTests.cs` | sửa | Thêm job thứ sáu vào danh sách noop/gỡ job của host test — quên job mới sẽ đỏ ngay tại `TestHostKhongChayJobNenTests` |

Chạy riêng:

```bash
dotnet test backend/SmartBus.Tests/SmartBus.Tests.csproj \
  --filter "FullyQualifiedName~RefundReconciliation|FullyQualifiedName~TestHostKhongChayJobNen"
```

---

## 8. Giới hạn đã biết

- **Chưa có sao kê cổng thật** (§4.3) — nhóm 4.2 im lặng trong bản chạy hiện tại. Báo cáo vẫn ghi
  `StatementAvailable = false`, không giả vờ đã đối chiếu.
- **Không có API đọc báo cáo**: bề mặt quản trị hoàn tiền (`GET /admin/refunds`) thuộc Băng và chưa
  có endpoint nào cho báo cáo đối soát. Thêm endpoint không có màn hình gọi là tự mở hình dạng API
  mới (luật 5). Chi tiết kỹ thuật đi vào log máy chủ; cảnh báo đi qua bảng `Notifications`.
- **Job không chạy ngay lúc khởi động**: một lượt ngay sau deploy có thể chạy chồng với bản cũ trong
  lúc chuyển và gửi cảnh báo "đêm nay" hai lần. Lượt đầu tiên là 00:05 UTC gần nhất.
- **Chạy một lần mỗi ngày**: một lệch phát sinh lúc 08:00 sẽ được thấy vào 00:05 hôm sau. Đây là
  đối soát theo ngày, không phải giám sát thời gian thực.
