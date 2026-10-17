# Thử lại hoàn tiền thất bại theo lịch — Sprint 4: job nền + giao thức giành dòng

> **US 8** — *"Là hệ thống, tôi muốn tự động xử lý hoàn tiền theo quy định khi giao dịch thanh toán thất bại hoặc vé bị hủy hợp lệ."*
>
> Việc này là **dòng 30** sheet Sprint 4 — *"Xử lý hoàn tiền thất bại + retry theo lịch"* (Cao · 5 điểm · Hoàng). Các dòng chung quanh: **27** Dăm (Migrate `Refunds`/`RefundLogs`) · **28** Kiên (service hoàn tiền tự động) · **29** Hiếu (gọi cổng MoMo/VNPay/ZaloPay) · **31** Dăm (API tra cứu trạng thái) · **32** Kiên (đối soát hằng ngày) · **33** Hạnh (màn theo dõi hành khách) · **34** Hoàng (thông báo kết quả) · **35–36** Băng (màn Admin + lọc/xuất) · **37–38** Vàng (test + kiểm thử chéo).
>
> ⚠️ **Hai cách đánh số**: tài liệu thiết kế US 8 (`docs/40` của Dăm) đánh số lệch +8 — việc trong tài liệu này là *"dòng 41"* ở đó. Cùng một task; sheet hiện hành gọi là dòng 30.
>
> 🟢 **Trạng thái**: nhánh `feature/30-xu-ly-hoan-tien-that-bai-retry-theo-lich`, đứng trên nhánh CSDL hoàn tiền của Dăm (`feature/8-migrate-refunds-refundlogs`, chưa vào main). **11 file mới + Program.cs + 5 file test (43 bài mới)**; toàn bộ backend **1331/1331 xanh**. Chưa ai gọi service ngoài job — **Kiên (dòng 28) là bên gọi đầu tiên** với lượt `Initial`. Cổng hoàn tiền đang là **placeholder** (mọi lượt ra `Unresolved` cho tới khi Hiếu cắm cổng thật — dòng 29). Hai bảng `Refunds`/`RefundLogs` **chưa có trên CSDL chung** (theo `docs/40` §8.4: chỉ áp sau khi nhánh Dăm merge).
>
> 🔢 **Cập nhật 15/10/2026 — số hiệu và hai chỗ đã cũ.** Bản này viết ra với số `docs/40`, **đổi
> thành `docs/42`** khi gộp vào `main`: `main` đã có `docs/40-csdl-hoan-tien.md` (tài liệu CSDL hoàn
> tiền của Dăm, vào trước qua PR #203) nên bản này **nhường số** theo lối *"người viết sau nhường
> số"* — Hoàng đã đồng ý. Cùng lượt đó **16 con trỏ `docs/35`** trong bản này trỏ về tài liệu CSDL
> hoàn tiền đã đổi thành **`docs/40`** (`docs/35` nay là tài liệu xuất PDF báo cáo, US 21 — không
> liên quan hoàn tiền), và **34 con trỏ cùng loại** trong code/test cũng vậy.
>
> Hai câu ở khối *"Trạng thái"* trên **đã cũ, giữ nguyên làm mốc lịch sử**: nhánh CSDL của Dăm
> (`feature/8-migrate-refunds-refundlogs`) **đã vào `main`** qua PR #203 — nên điều kiện *"chỉ áp lên
> CSDL sau khi merge"* ở §7 nay **đã thoả**, chỉ còn chờ chạy `dotnet ef database update`. Con số
> **1331/1331** là lượt chạy của riêng nhánh này lúc đó; sau khi gộp `main`, toàn bộ backend là
> **1460/1460**.
>
> 🔢 **Cập nhật 17/10/2026 — bốn chỗ nữa đã cũ.** Lượt soát ngày 17/10/2026 trên `main` @ `3dc2b0e`
> tìm thêm **bốn câu** đã hết đúng. Vẫn theo lối của khối trên: **giữ nguyên phần thân làm mốc lịch
> sử**, chỗ nào cần thì thêm một dòng trỏ về đây.
>
> - 🔴 **Hai bảng `Refunds`/`RefundLogs` NAY ĐÃ có trên CSDL chung** — `dotnet ef migrations list`
>   liệt kê **18/18** migration, **0** dòng `(Pending)`; lượt kiểm nối tới `13.237.241.81:5432`, tức
>   **CSDL chung** chứ không phải bản Docker ở máy. Câu *"chưa có trên CSDL chung"* ở khối *"Trạng
>   thái"*, ở §5 và ở §6 nay **sai**.
> - 🔴 **Cổng hoàn tiền KHÔNG còn là placeholder** — **PR #214** (Trần Trung Hiếu, task dòng 29) đã
>   thay `PlaceholderRefundGatewayCaller` bằng `RefundGatewayCaller` thật (`docs/43`). Hệ quả: lượt
>   gọi **không còn** mặc định ra `Unresolved`, nên câu *"hôm nay mọi lượt gọi ra `Unresolved`"* ở
>   §3.2 và gạch đầu dòng *"Placeholder làm mọi lượt ra `Unresolved → Pending`"* ở §5 hết đúng.
>   ⚠️ **Nhưng đừng đọc ngược lại thành "thang thử lại nay đã sống"**: sandbox **chưa được chạy
>   thật** — nhóm chưa có khoá đối tác MoMo/ZaloPay và sandbox VNPay hạn chế hoàn tiền (`docs/43`
>   §đầu). Đường thử lại theo thang chỉ sống khi cổng thật trả `Failed`, và điều đó **chưa quan sát
>   được**.
> - **Nhánh CSDL của Dăm đã vào `main`** — **PR #203** (đã ghi ở khối trên). Điều kiện *"merge nhánh
>   Dăm trước rồi mới áp lên CSDL chung"* ở §5 và §6 nay **đã thoả và đã chạy xong**.

---

## 1. Ai đọc tài liệu này, để làm gì

| Người | Dòng | Đọc mục | Để làm gì |
| --- | --- | --- | --- |
| **Kiên** | **28** | **§4.4**, §2.3 | Gọi `ExecuteAsync(..., Initial, ...)` ngay sau khi tạo khoản hoàn `Pending` — **bên gọi đầu tiên** |
| **Hiếu** | **29** | **§3** | Cắm cổng hoàn tiền thật vào `IRefundGatewayCaller` — thay **một dòng** `Program.cs`, không đổi hợp đồng |
| **Dăm** | 27, 31 | §2.3, §6 | Đối chiếu: giao thức giành dòng (`docs/40` §4.2) đã được cài đúng; bảng và migration vẫn thuộc nhánh Dăm |
| **Kiên** | **32** | **§5** | Khoản `Unresolved → Pending` là **đầu vào của đối soát** — hàng đợi tự động KHÔNG nhặt chúng |
| **Băng** | **35**, 36 | **§4.5** | Nút "Thử lại" → lượt `ManualRetry`; `retryCount` hiển thị gồm cả lượt thử tay |
| **Hạnh** | 33 | §2.2, §4.2 | Ba giá trị `Status`; `Pending` gồm cả "chưa gọi được cổng" — không phải lỗi hiển thị |
| **Giàng A Vàng** | 37–38 | §6 | Danh sách 43 bài test đã có — khỏi viết trùng, biết chỗ nào còn trống |

**Nguồn gốc nhiệm vụ** — `docs/40` §6, dòng giao cho Hoàng (nguyên văn):

> - **Hoàng** (dòng 41) — job thử lại theo lịch. 🔴 **Giành dòng bằng `xmin` TRƯỚC khi gọi cổng** (§4.2) — đây là chỗ dễ sai nhất và sai thì mất tiền thật. Ghi `NextRetryAt` theo chính sách (§2.6); hết lịch thì để `null`, **đừng** thêm giá trị enum mới. ⚠️ **Đừng** xếp lịch cho khoản có lượt cuối là `Unresolved` (§3.2).

---

## 2. Mảnh SERVER — cái gì đã có trong repo

### 2.1 Mười một file + bốn dòng đăng ký

| File | Làm gì |
| --- | --- |
| `Services/RefundRetryPolicy.cs` (52 dòng) | Thang 5 nấc `[5, 30, 120, 720, 1440]` phút + hàm thuần tính mốc kế tiếp |
| `Services/RefundRetryService.cs` (119 dòng) | **Một lượt quét**: chọn khoản `Failed` tới hạn, mở scope riêng từng khoản |
| `Services/IRefundRetryService.cs` | Hợp đồng một lượt quét (`SweepAsync(now)`) |
| `Services/RefundRetryBackgroundService.cs` (97 dòng) | Vòng lặp `PeriodicTimer` nhịp **1 phút** — job nền thứ năm của app |
| `Services/RefundAttemptService.cs` (**318 dòng**) | **Trái tim**: giành dòng → gọi cổng → ghi kết quả + nhật ký |
| `Services/IRefundAttemptService.cs` | Hợp đồng một lượt gọi cổng (`ExecuteAsync`) — ba đường cùng đi qua đây |
| `Services/IRefundGatewayCaller.cs` | **Seam cổng hoàn tiền** — chỗ Hiếu cắm vào |
| `Services/PlaceholderRefundGatewayCaller.cs` | Cắm tạm — luôn trả `Unresolved`, không bao giờ bịa kết quả |
| `Dtos/Refunds/RefundGatewayCallRequest.cs` | Thân yêu cầu gửi cổng (mã khoản, phương thức, số tiền) |
| `Dtos/Refunds/RefundGatewayCallOutcome.cs` | Cổng trả về gì (kết quả + lời nhắn + mã giao dịch cổng) |
| `Dtos/Refunds/RefundAttemptOutcome.cs` | Một lượt gọi kết thúc ra sao — **cho bên gọi đọc** |
| `Program.cs` | 3 dòng `AddScoped` + 1 dòng `AddHostedService` (khối cạnh các service thông báo) |

Test mới (5 file, **43 bài**): `RefundRetryPolicyTests` (10) · `RefundAttemptServiceTests` (18) · `RefundRetryServiceTests` (7) · `RefundClaimConcurrencyTests` (5) · `RefundRetryWiringTests` (3).

**Thêm hộ 3 file test của người khác** (job nền 4 → 5, ghi rõ trong PR): `JwtAuthTests.cs`, `TestHostKhongChayJobNenTests.cs`, `SeatHoldRaceFlowApiTests.cs`.

### 2.2 Job làm gì — và KHÔNG làm gì

| ✅ Có làm | ❌ Không làm (việc của ai) |
| --- | --- |
| Quét khoản `Failed` tới hạn theo `(Status, NextRetryAt)` mỗi phút | Tạo khoản hoàn (`RefundCode`, `Source`, `Amount`) — **dòng 28 Kiên** |
| **Giành dòng bằng `xmin` TRƯỚC khi gọi cổng** (`docs/40` §4.2) | Nói chuyện với MoMo/VNPay/ZaloPay — **dòng 29 Hiếu** (service chỉ gọi qua seam) |
| Gọi cổng qua `IRefundGatewayCaller` — mỗi khoản một lượt gọi tại một thời điểm | Tính số tiền hoàn — tiền là `Refund.Amount` **đã ghi**, không đọc lại vé |
| Ghi `RefundLogs` **một dòng mỗi lượt gọi** — kể cả lượt hỏng lẫn lượt `Unresolved` | Lật trạng thái vé / hủy-đổi vé — nhóm huỷ-đổi (dòng 5 Sprint 4) |
| Cập nhật `Status` / `FailReason` / `NextRetryAt` / `ProcessedAt` / `UpdatedAt` | Đối soát khoản `Unresolved` với sao kê cổng — **dòng 32 Kiên** |
| Trả về số lượt gọi cổng **đã ra ngoài** (kể cả `Unresolved`) | Mở endpoint nào — PR này **không đụng `api-contract.md`** |
| Mọi mốc thời gian lấy từ tham số `now` — không đọc đồng hồ hệ thống trong ruột | Nút "Thử lại" ở UI — **dòng 35 Băng** (bấm vào `ManualRetry`) |

**Vì sao tách đôi** *quét* và *một lượt gọi*: lượt quyết định **AI** (đọc bảng, sắp thứ tự, mở scope từng khoản), lượt gọi quyết định **CÓ GỌI HAY KHÔNG và gọi thế nào** (giành dòng, gọi cổng, ghi kết quả). Cả **ba** đường gọi cổng — job tự động, nút thử tay của quản trị, service tự động của Kiên — đi qua đúng **một cửa** `ExecuteAsync`; ba đường tự gọi cổng riêng thì sớm muộn lệch nhau về giành dòng và nhật ký.

### 2.3 Giao thức giành dòng — trái tim của cả việc

Đúng theo `docs/40` §4.2: **đọc → sửa → `SaveChanges()` (câu UPDATE có `WHERE "xmin" = <giá trị đã đọc>)` → RỒI MỚI gọi cổng**. Cửa sổ đua nằm giữa "đọc" và "ghi"; lượt thua nhận `DbUpdateConcurrencyException` và **bỏ lượt trong im lặng có kiểm soát**:

| Tình huống | Cơ chế | Test ghìm |
| --- | --- | --- |
| Lượt khác giành trước (nút thử tay, phiên bản job khác) | `WHERE "xmin" = 1` không khớp → `DbUpdateConcurrencyException` → **bỏ lượt, không gọi cổng, không ghi gì** | `Luot_gianh_dong_thua_thi_khong_goi_cong_va_khong_ghi_gi` — SQLite thật + interceptor bơm `xmin` (Part B) |
| Khoản không còn đủ điều kiện (trạng thái vừa đổi) | Đọc lại kiểm điều kiện theo trigger → trả `Claimed = false` | `Khoan_bi_luot_khac_gianh_truoc_thi_bo_qua_khong_goi_cong` |
| Không tìm thấy khoản | Trả `Claimed = false` (khác lượt thua ở chỗ `RefundCode` rỗng) | `Khong_tim_thay_khoan_thi_tra_ve_em_khong_nem_khong_goi` |
| Mô hình mất khoá phiên bản | — | `Refund_giu_khoa_phien_ban_xmin_dung_cot_he_thong` (soi metadata Npgsql) |

Lượt thua **không ghi `RefundLogs`** — nhật ký chỉ có dòng cho những lượt *thực sự gọi cổng*, nên "số dòng nhật ký = số lượt tiền đã ra ngoài" luôn đúng.

### 2.4 Thang thử lại — năm nấc, đếm bằng nhật ký

`RefundRetryPolicy.Delays = [5, 30, 120, 720, 1440]` phút (≈ 38,6 giờ cộng dồn). Số thứ tự nấc tính **trước khi ghi dòng nhật ký của lượt hiện tại**:

> **k** = số dòng `RefundLogs` có `Trigger ≠ Initial` đã ghi **trước** lượt này **+** (lượt này ≠ `Initial` ? 1 : 0)

| Lượt hỏng | k | Mốc hẹn kế tiếp |
| --- | --- | --- |
| `Initial` hỏng (lượt đầu, từ dòng 28) | 0 | **+5 phút** |
| `ScheduledRetry` #1 hỏng | 1 | +30 phút |
| #2 | 2 | +120 phút (2 giờ) |
| #3 | 3 | +720 phút (12 giờ) |
| #4 | 4 | +1440 phút (24 giờ) |
| #5 trở đi | ≥ 5 | **`null` — hết lịch tự động** (không thêm giá trị enum mới) |

Ba điểm dễ đọc nhầm:

- **Lượt quét đầu tiên của job cho một khoản đã `Failed` đã là `ScheduledRetry` #1** → hỏng tiếp thì hẹn **+30 phút**, KHÔNG phải +5. Nấc +5 phút chỉ sinh ra từ một lượt `Initial` hỏng (đường của Kiên). Ghim đúng con số ở `Moc_thoi_gian_di_xuyen_suot_luot_quet`.
- **Thử tay tiêu nấc y như thử tự động** — k đếm theo nhật ký, mà nhật ký không phân biệt ai bấm (`docs/40` §3.4). Quản trị bấm tay liên tục có thể đốt hết thang.
- **Hết thang vẫn thử tay được** — `ManualRetry` cố ý KHÔNG đọc `NextRetryAt` (`Bấm_tay_tren_khoan_da_het_thang_van_chay`); nhưng lượt tay hỏng sau khi hết thang lại ra `null` tiếp.

### 2.5 Bốn bất biến (`docs/40` §4.3) — ai giữ

| Bất biến | Ai giữ | Ghim ở đâu |
| --- | --- | --- |
| 1. `CancellationRequestId` khác null ⟺ `Source = TicketCancellation` | **Dòng 28 (Kiên)** lúc tạo khoản | Service này không đụng hai cột đó |
| 2. `FailReason` null ⟺ `Status ≠ Failed` | Service này — lượt hỏng LUÔN set (kèm câu thay thế khi cổng không nêu lý do); lượt thành công/`Unresolved` xoá sạch dấu vết lần hỏng cũ | `That_bai_ghi_ly_do_va_hen_moc_ke_tiep`, `That_bai_khong_neu_ly_do_thi_dung_chuoi_du_phong`, `Luot_dau_thanh_cong_lat_sang_Succeeded_va_xoa_sach_dau_vet_lan_hong_cu` |
| 3. `NextRetryAt` chỉ khác null khi `Status = Failed` | Service này — mọi nhánh khác đều để `null` | `Cong_khong_tra_loi_giu_nguyen_Pending_va_khong_xep_lich` |
| 4. Mỗi khoản một lượt gọi cổng tại một thời điểm | **Giao thức giành dòng** (§2.3) | `Luot_gianh_dong_thua_...` + `Refund_giu_khoa_phien_ban_xmin...` |

### 2.6 Quyết định cài đặt (đọc trước khi sửa service)

1. **Giành dòng bằng `xmin` qua EF, không khoá bi quan** — cột `xmin` đã khai `IsRowVersion()` trên `Refund`; EF tự đưa nó vào `WHERE` của câu UPDATE. Không `SELECT ... FOR UPDATE`, không bảng khoá riêng (`docs/40` §4.2 chốt).
2. **Thua cuộc đua là chuyện thường, không phải lỗi** — log `Information`; job không dừng, không `LogError` (nếu không, mỗi lần quản trị bấm tay nhanh hơn job là một dòng đỏ vô nghĩa).
3. **Mỗi khoản một scope riêng** (`IServiceScopeFactory`) — một `AppDbContext` dùng chung cho cả lượt quét nghĩa là mọi thay đổi của cả lượt nằm trong một mẻ dở dang; một khoản ném giữa đường là kéo theo cả lượt.
4. **Hai lượt ghi, lượt thứ hai có đường cứu** — lượt ghi *giành dòng* trước khi gọi cổng (bắt buộc); lượt ghi *kết quả* sau khi cổng trả lời. Lượt thứ hai đứt thì `LogError` với thông điệp **"TIỀN CÓ THỂ ĐÃ ĐI…"**, gỡ entity khỏi `ChangeTracker` và ghi lại — không bao giờ nuốt im lặng.
5. **Job không tự sát khi một lượt quét ném** — `catch Exception` → `LogError` → chờ nhịp kế (khác `StopHost` của các job khác): một khoản hỏng dữ liệu không được phép giết cả job.
6. **Nhịp 1 phút không ghim bằng test** — test ruột gọi thẳng `SweepAsync(now)`; nhịp là quyết định vận hành (cùng lối `SeatHoldExpiry`).
7. **Nhật ký giữ nguyên văn lời cổng** (chỉ cắt 500 ký tự), còn `FailReason`/`outcome.Message` nhận câu thay thế khi cổng không nêu lý do — nhật ký là **bằng chứng**, không được diễn giải.

---

## 3. Cổng hoàn tiền — chỗ cắm của Hiếu (dòng 29)

### 3.1 Hợp đồng seam

```csharp
Task<RefundGatewayCallOutcome> CallAsync(
    RefundGatewayCallRequest request, CancellationToken cancellationToken = default);

// Request:  RefundId · RefundCode · MethodCode · Amount
// Outcome:  Result (Succeeded | Failed | Unresolved) · Message · GatewayTransactionId
```

- **`RefundId` vừa là khoá chống trùng** gửi kèm cổng (idempotency key) — lượt gửi lại vì mạng đứt không được tạo giao dịch hoàn thứ hai.
- **`MethodCode` luôn lấy từ `Payments.MethodCode` của giao dịch gốc** (`docs/40` §2.5) — service đã đọc sẵn và truyền vào; cổng thật không phải tra lại, cũng không nhận phương thức từ tham số người dùng.
- **`Amount` là con số đã ghi trên `Refund`** — không tính lại từ vé.
- **Ba giá trị `Result` là ba thái độ với tiền**: `Succeeded` = chắc chắn xong (có mã giao dịch cổng); `Failed` = cổng **từ chối dứt khoát**, chắc chắn tiền KHÔNG đi; `Unresolved` = **không biết** (timeout, mạng đứt, lỗi mơ hồ). Đoán sai hai chiều đều mất tiền: nhận `Succeeded` nhầm là báo khách tiền đã về khi chưa gửi ai; nhận `Failed` nhầm là job gọi lại một khoản **có thể đã hoàn** → hoàn hai lần (`docs/40` §3.2).

### 3.2 `PlaceholderRefundGatewayCaller` — vì sao luôn `Unresolved`

Lớp cắm tạm log `Warning` mỗi lượt rồi trả `{ Result = Unresolved, Message = "Chưa nối cổng hoàn tiền thật (task dòng 29)." }`. Nó **không bao giờ bịa** `Succeeded`/`Failed` — vì bịa một trong hai là mất tiền thật, còn `Unresolved` chỉ đẩy khoản về bàn đối soát.

**Hệ quả đang sống**: hôm nay mọi lượt gọi ra `Unresolved` → khoản về `Pending`, `NextRetryAt = null` → hàng đợi tự động đứng yên (không xếp lịch cho lượt `Unresolved` — luật của `docs/40` §3.2). Đường thử lại theo thang chỉ sống dậy khi Hiếu cắm cổng thật trả về `Failed`. Đừng đọc việc "job chạy mà không thấy retry" là job hỏng.

⚠️ **Đã cũ 17/10/2026** — cổng thật **đã được cắm** (PR #214) nên câu *"hôm nay mọi lượt gọi ra `Unresolved`"* hết đúng. Nhưng lời khuyên cuối câu **vẫn giữ nguyên giá trị**: sandbox chưa chạy thật (`docs/43`) nên vẫn có thể chưa lượt nào trả `Failed`. Xem khối *Cập nhật* ở đầu tài liệu.

### 3.3 Hiếu cắm cổng thật — ba việc

> ✅ **Đã xong 17/10/2026** — cả ba việc đã làm ở **PR #214** (`docs/43`). Giữ nguyên ba dòng dưới làm mốc lịch sử.

1. **Thay một dòng** trong `Program.cs`: `AddScoped<IRefundGatewayCaller, PlaceholderRefundGatewayCaller>()` → lớp thật. **Không đổi chữ ký seam, không đổi DTO** — mọi thứ quanh nó đã có test (`RefundRetryWiringTests` cố ý không ghim tên lớp cắm tạm).
2. **Ánh xạ kết quả đúng bảng §3.1.** Ném ngoại lệ ra ngoài cũng được — service bọc thành `Unresolved` (`Cong_nem_ngoai_le_cung_thanh_Unresolved_chu_khong_sap_luot_quet`) — nhưng **nếu phân biệt được "cổng từ chối" thì ĐỪNG ném, hãy trả `Failed`**: ném là bỏ mất đường thử lại theo thang, khoản nằm `Pending` chờ đối soát.
3. **Không tự ghi `RefundLogs`** — service đã ghi một dòng cho mỗi lượt gọi (kể cả `Unresolved`); cổng ghi thêm là hai dòng cho một lượt.

Endpoint `POST /admin/refunds/{id}/retry` (nút "Thử lại" — hợp đồng frontend `refundAdminApi.ts` đã có sẵn, backend **chưa mở**): khi ai đó mở, ruột nó chỉ cần gọi `ExecuteAsync(id, RefundLogTrigger.ManualRetry, DateTime.UtcNow)` và trả outcome — mọi luật ở tài liệu này áp dụng nguyên vẹn.

---

## 4. Bên gọi — ba trigger, ba đường, một cửa

### 4.1 Ba trigger

| Trigger | Ai gọi | Nhận lượt khi (`IsEligible`) |
| --- | --- | --- |
| `Initial` | **Kiên — dòng 28**, ngay sau khi tạo khoản hoàn `Pending` | `Status = Pending` |
| `ScheduledRetry` | **Job này** — mỗi lượt quét | `Status = Failed` ∧ `NextRetryAt ≠ null` ∧ `≤ now` |
| `ManualRetry` | Endpoint "Thử lại" (Băng bấm — dòng 35) | `Status = Failed` — **cố ý KHÔNG đọc `NextRetryAt`**: hết thang tự động vẫn thử tay được |

### 4.2 Đọc `RefundAttemptOutcome` thế nào

| Outcome | Nghĩa | Bên gọi làm gì |
| --- | --- | --- |
| `Claimed = true`, `Result = Succeeded` | Cổng xác nhận đã hoàn; khoản `Succeeded`, `ProcessedAt` đặt | Báo thành công cho người dùng (dòng 34) |
| `Claimed = true`, `Result = Failed` | Cổng từ chối; `FailReason` + `NextRetryAt` kế tiếp đã ghi | Không xếp gì thêm — thang tự chạy |
| `Claimed = true`, `Result = Unresolved` | Đã gọi nhưng không biết kết quả; khoản giữ `Pending`, **không xếp lịch** | Chuyển danh sách đối soát (dòng 32) |
| `Claimed = false` | **Không gọi cổng** — khoản không tìm thấy (`RefundCode` rỗng), không đủ điều kiện, hoặc thua cuộc đua (`RefundCode` có giá trị) | **Không phải lỗi** — không báo động, không thử lại ngay |

### 4.3 Mốc `now` truyền xuyên suốt

Service **không tự đọc đồng hồ** — mọi mốc (`NextRetryAt`, `ProcessedAt`, `UpdatedAt`, `RefundLogs.CreatedAt`) tính từ tham số `now` của bên gọi. Job truyền `DateTime.UtcNow` **một lần cho mỗi lượt quét** rồi dùng chung cho cả lượt (`Moc_thoi_gian_di_xuyen_suot_luot_quet`; lỡ có chỗ đọc đồng hồ hệ thống là test đỏ vì lệch hẳn mốc ghim).

### 4.4 Kiên (dòng 28) — lượt `Initial`, bên gọi đầu tiên

Sau khi tạo khoản hoàn `Pending` (`RefundCode` unique, `Source`, `Amount`, giữ 4 bất biến §2.5 — nhớ bất biến 1), gọi `ExecuteAsync(id, RefundLogTrigger.Initial, now)`. Hỏng ở lượt này sinh nấc **+5 phút** — nấc duy nhất mà chỉ đường của Kiên tạo ra được. Vi phạm unique `RefundCode` lúc tạo = **thành công idempotent** (`docs/40` §6), không phải lỗi cần xử lý.

### 4.5 Băng (dòng 35) — nút "Thử lại"

Nút gọi endpoint `ManualRetry` (§3.3). Hai điều để hiển thị đúng: **`retryCount` = số dòng `RefundLogs` có `Trigger ≠ Initial`** (lượt thử tay tiêu nấc y như lượt tự động — §2.4); và **hết thang (`NextRetryAt = null`) không có nghĩa nút vô dụng** — `ManualRetry` vẫn chạy.

---

## 5. Ranh giới đã biết

- **Nhịp 1 phút không ghim bằng test** — cố ý; test gọi thẳng `SweepAsync(now)`, nhịp là quyết định vận hành.
- **Thua cuộc đua không ghi gì cả** — không `RefundLogs`, không `UpdatedAt`; chỉ một dòng `Information` phía job. Nhật ký "số dòng = số lượt tiền ra ngoài" vì vậy luôn đúng.
- **Placeholder làm mọi lượt ra `Unresolved → Pending`** — không xếp lịch, đúng luật (`docs/40` §3.2: đừng thử lại khoản có thể đã hoàn). Thang thử lại chỉ sống khi cổng thật trả `Failed`.
  ⚠️ **Đã cũ 17/10/2026** — cổng thật đã được cắm (PR #214), nên lượt gọi **không còn** mặc định ra `Unresolved`; xem khối *Cập nhật* ở đầu tài liệu.
- **Khoản `Pending` do `Unresolved` chỉ hiện ra qua đối soát (dòng 32)** — hàng đợi quét chỉ nhặt `Failed`; Kiên phải đọc `RefundLogs` (lượt cuối `Unresolved`) để nhặt chúng lên bàn.
- **Hết thang (`k ≥ 5`) → `NextRetryAt = null` vĩnh viễn trên đường tự động** — quản trị vẫn bấm tay được, nhưng lượt tay hỏng cũng ra `null` tiếp; muốn chạy lại thang phải tạo khoản mới (chỉ mục lọc duy nhất cho phép: chỉ tính `Pending`/`Succeeded`).
- **Sập giữa giành dòng và lượt ghi kết quả** → `LogError` "TIỀN CÓ THỂ ĐÃ ĐI…" + gỡ entity + ghi lại; ghi lại cũng đứt thì khoản nằm `Pending`, `GatewayTransactionId` null — **đối soát (dòng 32) phải nhặt được cả trường hợp này**.
- **`xmin` chỉ cưỡng chế được trên nhà cung cấp có cột hệ thống (PostgreSQL)** — InMemory/SQLite không có; `RefundClaimConcurrencyTests` Part B dựng lại lượt thua trên SQLite thật bằng interceptor bơm `xmin` (cơ chế `DbUpdateConcurrencyException` là thật, chỉ nguồn tăng `xmin` là giả).
- **Lượt quét không phân trang** — mọi khoản tới hạn trong một lượt; đủ cho quy mô hiện tại, ghi lại để biết trước khi bảng lớn.
- **PR này không mở endpoint nào** — `api-contract.md` không đụng; endpoint "Thử lại" + API tra cứu (dòng 31) là việc của các dòng khác.
- **`Refund`/`RefundLog`/`xmin`/migration thuộc nhánh Dăm** (`feature/8-migrate-refunds-refundlogs`, chưa vào main) — nhánh này xếp **trên** nhánh đó; merge theo thứ tự nhánh Dăm trước (hoặc cùng lúc), và chỉ **sau khi merge** mới áp lên CSDL chung (`docs/40` §8.4).
  ⚠️ **Đã cũ 17/10/2026** — nhánh Dăm đã vào `main` (PR #203) và CSDL chung đã áp xong (**18/18**, 0 `(Pending)`); xem khối *Cập nhật* ở đầu tài liệu.

---

## 6. File, test, số liệu

**43 bài test mới** trên 5 file — nhóm theo thứ tự độ khó của thứ chúng ghìm:

| Nhóm | Số bài | Ghìm điều gì |
| --- | --- | --- |
| Thang thử lại (`RefundRetryPolicyTests`) | 10 | Năm nấc đúng từng con số; mốc tính từ lần hỏng gần nhất; hết thang để `null` |
| Một lượt gọi cổng (`RefundAttemptServiceTests`) | 18 | Ba trigger × điều kiện nhận; giành dòng thua thì không gọi cổng không ghi gì; ba kết cục Succeeded/Failed/Unresolved đổi trạng thái đúng bất biến; cổng ném ngoại lệ → `Unresolved`; cắt chuỗi 500; phương thức lấy từ giao dịch gốc |
| Một lượt quét (`RefundRetryServiceTests`) | 7 | Chỉ chạm khoản `Failed` tới hạn; thứ tự chờ lâu nhất trước; `now` đi xuyên suốt; `Unresolved` vẫn tính là một lượt gọi; lượt sau không gọi lại khoản đã xong |
| Giành dòng thật (`RefundClaimConcurrencyTests`) | 5 | 4 bài soi mô hình Npgsql (`xmin`, index quét, index lọc duy nhất, nhật ký không có `xmin`) + 1 bài dựng lại lượt thua trên SQLite thật |
| Nối DI (`RefundRetryWiringTests`) | 3 | Job đăng ký đúng MỘT lần; service và chuỗi `Attempt → Gateway` resolve được từ scope |

**Bộ backend đầy đủ: 1331/1331 xanh** (trước nhánh: 1288 — nhánh này đứng trên nhánh Dăm, chưa gồm các dòng 5/18/23 vừa vào main).

**Bảng CSDL:** `Refunds`/`RefundLogs` thuộc migration `20261014153025_Sprint4_Refunds_RefundLogs` của **Dăm** — **chưa có trên CSDL chung**. Theo `docs/40` §8.4: merge nhánh Dăm rồi mới `dotnet ef database update --project backend/SmartBus.Api`. Trước mốc đó, mọi endpoint chạm hai bảng sẽ lỗi `relation "Refunds" does not exist`; riêng job thì thử lại mỗi phút và log `Error` — chấp nhận được, không phải sự cố.

⚠️ **Đã cũ 17/10/2026** — câu trên hết đúng: nhánh Dăm đã vào `main` (PR #203), `database update` **đã chạy**, hai bảng **đã có** trên CSDL chung (**18/18** migration, **0** `(Pending)`). Câu *"1331/1331"* ngay trên cũng là số của riêng nhánh này lúc đó — sau khi gộp `main` là **1460/1460**. Xem khối *Cập nhật* ở đầu tài liệu.
