# Thông báo trễ chuyến / đổi lịch trình — Sprint 4: notifier + lớp bọc

> **US 10** — *"Là hành khách, tôi muốn nhận thông báo khi xe sắp đến trạm đón/trạm xuống để chuẩn bị di chuyển."*
>
> Việc này là **dòng 44** sheet Sprint 4 — *"Service thông báo trễ chuyến / thay đổi lịch trình"* (Cao · 5 điểm · Hoàng). Các dòng chung quanh trong cùng cụm thông báo: **40** Hiếu (API cập nhật vị trí chuyến) · **41** Kiên (service báo khi xe cách trạm <500 m) · **42** Hiếu (Web Push PWA) · **43** Hiếu (API theo dõi thông báo theo tuyến/trạm) · **45** Dăm (API lịch sử thông báo đã nhận) · **46** Dăm (component bật/tắt nhận thông báo theo trạm) · **47** Băng (chuông + badge) · **48** Hạnh (màn chi tiết + đánh dấu đã đọc).
>
> 🟢 **Trạng thái**: nhánh `feature/44-thong-bao-tre-chuyen-doi-lich-trinh` đứng trên main `d5ee43f`. **3 file mới + `Program.cs` + 2 file entity (thêm hộ Dăm) + `api-contract.md` + 3 file test mới (23 ca)**; toàn bộ backend **1615/1615 xanh** (lượt đầy đủ 1614/1615, bài ghế SQLite chập chờn đã biết chạy lại một mình xanh). Không mở endpoint nào — thông báo đi qua hub `notifications` và endpoint `GET /api/notifications/me` đã có. PR này **không đụng `Migrations/`**, **không đụng `frontend/`**.

---

## 1. Ai đọc tài liệu này, để làm gì

| Người | Dòng | Đọc mục | Để làm gì |
| --- | --- | --- | --- |
| **Băng / Hạnh** | 47–48 | **§5.2** | Hai loại `Trip…` + target `Trip` là **hợp đồng frontend mới**: màn thông báo phải khai thêm vào union TypeScript và ánh xạ biểu tượng; điều hướng `RelatedEntityType = Trip` về chi tiết chuyến / vé của khách |
| **Dăm** | 45–46 | §6.1, §9 | Đối chiếu: hai giá trị enum mới + target `Trip` trong **file của Dăm** (thêm hộ, ghi rõ trong PR) — cột `varchar` không CHECK nên **không cần migration**. Hai doc-comment trong `NotificationResponse.cs` còn ghi "sáu giá trị"/"ba target" — cập nhật khi tiện |
| **Hiếu** | **40** | **§4.1** | `PUT /routes/{routeId}/trips/{id}` là **nguồn duy nhất** phát thông báo này. Nếu sau này có đường khác đổi giờ chuyến (job, endpoint mới), phải đi qua `IRouteTripsService` hoặc gọi thẳng `ITripScheduleChangeNotifier` — sửa CSDL trực tiếp là thông báo biến mất |
| **Kiên** | **41** | §3.5 | Thông báo "xe cách trạm <500 m" là **nguồn khác**, không dùng `TripDelayed`; nhưng dùng đúng lối hai khối `try` của notifier này (không ném lỗi ra luồng chính) |
| **Giàng A Vàng** | test | §7 | Danh sách 23 ca đã có — khỏi viết trùng |

**Nguồn gốc nhiệm vụ** — dòng 44 giao cho Hoàng: service tự phát thông báo khi quản lý sửa giờ chuyến. Dòng 40–43 và 45–48 cùng cụm US 10; tài liệu này chỉ mô tả phần **thông báo trễ / đổi lịch trình**, không mô tả thông báo theo trạm.

---

## 2. Ba file mới + hai chỗ sửa

| File | Làm gì |
| --- | --- |
| `Services/ITripScheduleChangeNotifier.cs` (**64 dòng**) | Hợp đồng `NotifyAsync(tripId, previous)` — ba chốt: **không bao giờ ném lỗi**, **không nhận `CancellationToken`**, `previous = null` thì im lặng thay vì đoán |
| `Services/TripScheduleChangeNotifier.cs` (**291 dòng**) | Cài đặt: so ảnh chụp giờ cũ với chuyến đã lưu → phân loại trễ/đổi → ghi `Notifications` cho từng khách có vé `Paid` + đẩy SignalR |
| `Services/NotifyingRouteTripsService.cs` (**158 dòng**) | **Lớp bọc** quanh `RouteTripsService`: chụp giờ cũ → chạy lượt sửa → chỉ báo khi lượt sửa thành công |
| `Program.cs` | Thay 1 dòng `AddScoped<IRouteTripsService, RouteTripsService>()` bằng 3 dòng: lớp trần + notifier + lớp bọc (cùng lối `RefundAttemptService`) |
| `Entities/NotificationType.cs` (**file Dăm**) | Thêm `TripDelayed`, `TripScheduleChanged` — **thêm hộ, ghi rõ trong PR**; cột `varchar`, không CHECK, không migration |
| `Entities/NotificationTarget.cs` (**file Dăm**) | Thêm `Trip` — cùng ghi chú trên |
| `docs/api-contract.md` | 5 chỗ: "ba nguồn" → **"bốn nguồn"**, khối nguồn thứ tư + bảng phân loại, "sáu loại" → **"tám loại"**, target thêm `Trip`, ghi chú `PUT`/`DELETE` ở mục chuyến, ghi chú lệch frontend |

Toàn bộ nằm trong **một luồng không có endpoint mới**: khách nhận thông báo qua `/hubs/notifications` đã có từ US 5 và qua `GET /api/notifications/me` (dòng 45, đã vào main).

---

## 3. Chính sách — báo gì, im gì

### 3.1 Một sự kiện = một lần đổi thật

Hai notifier cũ (kết quả huỷ/đổi vé, kết quả hoàn tiền) báo **một lần cho một quyết định** và có chốt đọc-trước-khi-ghi để nuốt lượt gọi lặp. Notifier này **cố ý khác**:

- **Không báo theo lượt PUT.** PUT lại đúng dữ liệu cũ, chỉ đổi xe, hoặc chỉ đổi `status` → không có gì để nói.
- **Không có chốt chống trùng.** Một chuyến bị dời giờ hai lần là **hai sự kiện**; khách phải nhận cả hai. Áp chốt "mỗi chuyến một thông báo" vào đây sẽ nuốt mất lần thứ hai — lần có thể là lần khách thực sự cần biết.
- **So ảnh chụp trước khi sửa với dữ liệu đã lưu sau khi sửa.** Đây là thứ duy nhất trả lời được "lượt PUT này có thực sự đổi giờ không".

| Thay đổi trong lượt PUT | Phát? | Loại |
| --- | --- | --- |
| Có mốc đổi, **mọi mốc đổi đều lùi ra sau**, `arrivalTime` không đổi giữa null và có giá trị | ✅ | `TripDelayed` |
| Có mốc đổi và có **ít nhất một mốc sớm hơn**, hoặc `arrivalTime` đổi giữa null và có giá trị | ✅ | `TripScheduleChanged` |
| Không mốc nào đổi (chỉ xe / chỉ `status` / PUT lại dữ liệu cũ) | ❌ | — |
| Chuyến **đã lưu** ở `Cancelled` hoặc `Completed` | ❌ | — |

Hai loại phủ kín mọi lần đổi giờ thật: hoặc toàn bộ là lùi giờ, hoặc có ít nhất một mốc sớm hơn / mốc tới bị thêm-bỏ. Trường hợp "khởi hành lùi nhưng tới dự kiến sớm hơn" rơi vào `TripScheduleChanged`, không phải `TripDelayed` — gọi là "trễ" khi một mốc lại sớm hơn là nói sai.

### 3.2 Cổng trạng thái đọc trạng thái CUỐI

Notifier đọc `Trip.Status` **sau khi lớp bọc đã chạy xong lượt sửa**, không đọc trạng thái lúc chụp ảnh. Hệ quả:

- Dời giờ một chuyến `Cancelled`/`Completed` → im lặng. Bản ghi đó không còn ai chờ; báo "chuyến bị trễ" lúc này là nói sai sự thật.
- Một lượt PUT vừa **mở lại** chuyến `Cancelled → Scheduled` vừa đổi giờ → **có** thông báo, vì trạng thái cuối là `Scheduled`. Đây là lần mở chuyến kèm dời giờ, không phải ca dời một chuyến đã chết.
- Chỉ đổi `status` sang `Cancelled` mà giờ giữ nguyên → im lặng: không có mốc nào đổi, và huỷ chuyến không phải thông báo trễ/đổi lịch trình (§8).

### 3.3 Người nhận — khách đã thực sự mua vé

Tập người nhận là **các `UserId` khác nhau** có ít nhất một vé `Ticket.Status == Paid` của chuyến. Hệ quả:

- Khách mua hai vé `Paid` cho cùng chuyến → **một** thông báo, không phải hai. Thông báo nói về chuyến, không nói về từng vé.
- Vé `Used` và `Cancelled` không tính — người đã đi xong hoặc đã huỷ không còn chờ chuyến đó.
- Không có khách `Paid` nào → không ghi dòng nào, không đẩy sự kiện nào.

Mỗi người nhận được **một dòng `Notifications` riêng** (cùng `Title`/`Body`/`RelatedEntityId`, khác `Id` và `UserId`) — bảng thông báo là dữ liệu riêng của từng người, không có bản ghi dùng chung.

### 3.4 Các đường im lặng có chủ ý

| Ca | Xử lý | Vì sao |
| --- | --- | --- |
| `previous = null` (chụp ảnh cũ thất bại) | `LogWarning`, không ghi | Không có mốc cũ thì không biết đã đổi gì; đoán "trễ" hay "đổi" đều có thể sai. Lượt sửa vẫn thành công — mất một thông báo còn hơn chặn thao tác của quản lý |
| Không tìm thấy chuyến / tuyến khi đọc lại | `LogWarning`, không ghi | Chuyến có thể đã bị xoá cứng ngoài luồng (dữ liệu bất thường); không có chuyến thì không có người nhận để báo |
| Không có mốc nào đổi | Không ghi, **không log** | Đường này đi qua ở mọi lượt PUT chỉ đổi xe/trạng thái; log ở đây là làm ngập log vận hành |
| Trạng thái cuối `Cancelled`/`Completed` | Không ghi, không log | Xem §3.2 |
| Không có vé `Paid` | Không ghi, không log | Chuyến rỗng là chuyện bình thường |
| Lượt sửa lỗi (404 sai tuyến, 400 giờ tới trước giờ đi) | **Không gọi notifier** | Lớp bọc chỉ báo khi `result.Success` — không có thay đổi nào đã lưu thì không có gì để báo |

### 3.5 Ghi hỏng / đẩy hỏng — không được làm hỏng lượt sửa

Hai khối `try` **riêng** (cùng lối `RefundResultNotifier` và `TicketRequestResultNotifier`):

- **Ghi `Notifications` hỏng** → `LogError`, bỏ qua. Lượt sửa chuyến đã commit từ trước; thông báo không được kéo theo thao tác nghiệp vụ.
- **Ghi xong, đẩy SignalR hỏng** → `LogWarning` từng người. Dòng đã nằm trong CSDL, khách vẫn thấy khi mở lại màn thông báo — chỉ mất lời nhắc tức thời.
- **Toàn bộ `NotifyAsync`** được bọc thêm một `try/catch` ngoài cùng, ghi `LogError`. Hợp đồng của interface là **không bao giờ ném lỗi** — người gọi không phải nhớ bọc.

---

## 4. Chỗ nối — vì sao là lớp bọc, không phải sửa thẳng service

### 4.1 `PUT /routes/{routeId}/trips/{id}` là cửa duy nhất hôm nay

Đã kiểm: chỉ `RouteTripsService.UpdateAsync` gán lại giờ của một chuyến **đã tồn tại**; các đường tạo chuyến, seeder, mapper không đụng giờ chuyến cũ. Vì vậy bọc `IRouteTripsService` là đủ để mọi lượt sửa qua API đi qua notifier.

Lớp bọc chạy ba bước, theo đúng thứ tự:

1. **Chụp ảnh cũ** (`DepartureTime`, `ArrivalTime`) bằng `AsNoTracking` — trước khi lớp trần ghi.
2. Gọi `_inner.UpdateAsync(...)` — toàn bộ validate, 404, 400, lưu vẫn do service của Hiếu làm.
3. `result.Success` → `_notifier.NotifyAsync(id, previous)`. Kết quả trả về cho controller **không bị sửa** — lớp bọc không đổi hình dạng API.

Chụp ảnh hỏng không chặn lượt sửa: bọc trong `try/catch`, trả `null`, notifier thấy `null` thì im lặng (§3.4). Đây là chủ ý — thà mất một thông báo còn hơn biến một thao tác quản trị hợp lệ thành 500.

### 4.2 Vì sao không sửa `RouteTripsService.cs`

`RouteTripsService` là **file của Hiếu** (luật E1) và đang giữ toàn bộ validate của nghiệp vụ sửa chuyến. Trộn thêm một việc có I/O kênh thông báo vào cùng lớp là mở đường cho lỗi phụ trợ làm hỏng lượt sửa — đúng thứ hai khối `try` ở §3.5 muốn tránh. Bọc ở ngoài giữ service đó nguyên vẹn và cho phép test lớp bọc độc lập với service thật.

### 4.3 Thứ tự đăng ký DI

```csharp
builder.Services.AddScoped<RouteTripsService>();                       // lớp trần
builder.Services.AddScoped<ITripScheduleChangeNotifier, TripScheduleChangeNotifier>();
builder.Services.AddScoped<IRouteTripsService>(provider => new NotifyingRouteTripsService(...)); // lớp bọc
```

DI lấy **đăng ký sau cùng** cho cùng một service type, nên `IRouteTripsService` resolve ra lớp bọc. Ba dòng này là ba mắt xích, và cả ba đều **hỏng im lặng** nếu thiếu:

- thiếu dòng lớp bọc → mọi endpoint vẫn 200, giờ vẫn đổi, chỉ khách là không bao giờ được báo;
- thiếu dòng notifier hoặc dòng lớp trần → factory của lớp bọc không resolve được tham số, **mọi lượt PUT thành 500**.

`TripScheduleChangeWiringTests` ghim cả ba bằng `Assert.IsType`, không dùng `NotNull`.

### 4.4 `CancellationToken` đi đâu

Token vẫn chuyển tiếp nguyên vẹn qua **sáu phương thức** của `IRouteTripsService` (hợp đồng của Hiếu). `ITripScheduleChangeNotifier.NotifyAsync` **cố ý không nhận token**: lượt sửa đã commit, và một thông báo đã được quyết định phải được ghi bất kể request có bị huỷ ngay sau đó. Token được phép cắt request, không được phép làm dòng thông báo biến mất.

---

## 5. Khách thấy gì

### 5.1 Hai đường đến

1. **Đang mở app** — sự kiện `NotificationReceived(notificationId)` đẩy vào nhóm `user:{userId}` qua `/hubs/notifications` (hub đã có từ US 5). Client nhận **id** rồi gọi lại danh sách.
2. **Mở lại sau** — `GET /api/notifications/me` (dòng 45, đã vào main) trả các dòng `Notifications` của chính người gọi, mới nhất trước.

### 5.2 Hợp đồng frontend (Băng / Hạnh — dòng 47–48)

Đây là **thay đổi hợp đồng**:

- `NotificationType` từ bốn giá trị (thực tế backend đã phát sáu) thành **tám**: thêm `TripDelayed`, `TripScheduleChanged`.
- `NotificationTarget` thêm **`Trip`** — bấm vào thông báo mở chi tiết chuyến / vé của khách, dùng `RelatedEntityId` làm id chuyến.

`frontend/src/api/notificationApi.ts` hiện mới khai bốn loại + hai target và vẫn `USE_MOCK_DATA = true`; `NotificationBell.isApproved` chỉ biết `CancellationApproved`/`TicketChangeApproved`, nên **mọi loại khác bị vẽ bằng biểu tượng đỏ "bị từ chối"** — `TripDelayed` sẽ hiện dấu X đỏ, sai nghĩa. Cần:

- khai thêm bốn giá trị vào union TypeScript (hai `Refund…` của dòng 34 cũng đang thiếu) và target `Refund`/`Trip`;
- ánh xạ biểu tượng riêng cho nhóm `Trip…` (gợi ý: đồng hồ/cảnh báo cho `TripDelayed`, đồng hồ/thông tin cho `TripScheduleChanged` — không phải dấu X đỏ);
- điều hướng `RelatedEntityType = Trip` về màn chi tiết chuyến hoặc vé tương ứng.

`api-contract.md` (mục "Tám loại thông báo" và bảng trường `Notification`) là bản hợp đồng đã cập nhật — đọc ở đó khi nối UI. Backend ghi sẵn cả `Title` lẫn `Body`; frontend chỉ hiển thị.

### 5.3 Trạng thái "đã đọc" — không đổi

Bảng `Notifications` vẫn chỉ-ghi-thêm, chưa có cột `ReadAt`; chuông + badge vẫn dùng mốc "đã xem lần cuối" trong `localStorage` như docs/33 §2. Thông báo trễ/đổi lịch trình **không mở thêm cơ chế đã đọc nào**.

---

## 6. Nội dung thông báo — hai loại, một khuôn

| Loại | Tiêu đề |
| --- | --- |
| `TripDelayed` | Chuyến xe bị trễ |
| `TripScheduleChanged` | Lịch trình chuyến xe thay đổi |

`Body` luôn gồm **câu mở đầu + các mốc đã đổi**, ghép bằng dấu cách:

- Mở đầu: `Chuyến xe {tên tuyến} bị trễ.` hoặc `Lịch trình chuyến xe {tên tuyến} đã thay đổi.`
- Nếu giờ khởi hành đổi: `Khởi hành: {cũ} → {mới}.`
- Nếu giờ tới dự kiến đổi: `Tới dự kiến: {cũ} → {mới}.`

Chỉ các mốc **thực sự đổi** mới xuất hiện — body không nhắc lại mốc giữ nguyên. Ví dụ đúng như test ghim (lùi cả hai mốc 30 phút):

```text
Chuyến xe Bến Thành — Chợ Lớn bị trễ. Khởi hành: 17/10/2026 15:00 → 17/10/2026 15:30. Tới dự kiến: 17/10/2026 16:00 → 17/10/2026 16:30.
```

### 6.1 Ba chốt về chữ nghĩa

1. **Giờ Việt Nam (UTC+7), định dạng `dd/MM/yyyy HH:mm`, bất biến theo culture.** Dùng hằng `VietnamUtcOffset = TimeSpan.FromHours(7)`, **không** `TimeZoneInfo.FindSystemTimeZoneById` — id vùng khác nhau giữa Windows và Linux, CI chạy ubuntu. Đây cùng lối `VietnamUtcOffsetHours` đã dùng ở vé/PDF.
2. **`arrivalTime = null` hiển thị `(chưa có)`.** Việc thêm hoặc bỏ mốc tới cũng là một lần đổi lịch trình; body ghi `Tới dự kiến: (chưa có) → 17/10/2026 16:00` thay vì để trống hoặc đoán giờ.
3. **Không suy ra "trễ bao lâu" / "sớm bao lâu" thành câu riêng.** Một lượt sửa có thể đổi cả hai mốc theo hai hướng; khách cần **mốc cũ và mốc mới** để tự đối chiếu, còn con số chênh lệch tính sai một mốc là sai toàn câu. Không có số tiền trong body — cùng luật với các loại thông báo khác.

---

## 7. File, test, số liệu

### 7.1 File của Dăm bị đụng (thêm hộ)

`NotificationType.cs` (+2 giá trị), `NotificationTarget.cs` (+1 giá trị) — **không cần migration**: cả hai cột lưu `varchar`, không `CHECK`, không index theo tập giá trị. Việc này ghi rõ trong PR theo lệ "thêm hộ".

### 7.2 Test — 23 ca mới

**`TripScheduleChangeNotifierTests.cs` (526 dòng, 16 ca)** — dựng service tay với `InMemory`:

| Nhóm | Số ca | Ghim điều gì |
| --- | --- | --- |
| Người nhận | 1 | Hai khách `Paid` (một người có hai vé) + một khách có vé `Used`/`Cancelled` → **hai** dòng, đúng `UserId`, `Type`, `RelatedEntityType = Trip`, `RelatedEntityId`, câu `Body` nguyên văn cho cả hai loại mốc, đúng nhóm `user:{id}`, id đẩy khớp id đã lưu |
| Phân loại trễ | 2 | Chỉ khởi hành lùi → `TripDelayed`; chỉ giờ tới lùi → `TripDelayed` |
| Phân loại đổi lịch trình | 4 | Khởi hành sớm hơn; một mốc sớm + một mốc muộn; thêm mốc tới từ null (body có `(chưa có)`); bỏ mốc tới về null |
| Im lặng có chủ ý | 6 | Không mốc nào đổi; trạng thái cuối `Cancelled`/`Completed` (theory 2 ca); không có vé `Paid`; `previous = null`; không tìm thấy chuyến |
| Báo theo sự kiện | 1 | Dời giờ hai lần liên tiếp → **hai** dòng, **hai** lần đẩy (không có chốt chống trùng) |
| Hai nhánh hỏng | 2 | Kênh đẩy ném → không lỗi nổi ra + dòng vẫn ghi; `DbContext` đã dispose → không lỗi nổi ra |

**`NotifyingRouteTripsServiceTests.cs` (203 dòng, 4 ca)** — dùng `RouteTripsService` thật:

| Ca | Ghim điều gì |
| --- | --- |
| Sửa thành công | Notifier nhận đúng `(tripId, ảnh chụp cũ)`, kết quả của lớp trần được trả nguyên, giờ mới đã nằm trong CSDL |
| Sai tuyến → 404 | Không gọi notifier |
| Giờ tới trước giờ đi → 400 | Không gọi notifier, dữ liệu trong CSDL không đổi |
| `GetById` / `List` | Chuyển tiếp bình thường, không gọi notifier |

**`TripScheduleChangeWiringTests.cs` (69 dòng, 3 ca)** — resolve qua scope như request thật: `IRouteTripsService` đúng là `NotifyingRouteTripsService`; `ITripScheduleChangeNotifier` đúng là `TripScheduleChangeNotifier`; lớp trần vẫn resolve được.

**Bộ backend đầy đủ: 1615/1615 xanh** (trước nhánh: 1592). Lượt chạy đầy đủ có 1 bài đỏ là `SeatHoldRaceFlowApiTests.Nam_muoi_khach_cung_giu_mot_ghe_thi_dung_mot_nguoi_thang` — lỗi SQLite `Error 5` chập chờn đã biết ở bước mở kết nối (`JwtMiddleware.cs:185`), chạy lại một mình xanh; không liên quan diff này.

**Không có test nào chạm CSDL thật** — InMemory; đường này không có tranh chấp ghi nên không cần test đua.

---

## 8. Ranh giới đã biết

- **Không có bộ phát hiện trễ nền.** Thông báo chỉ phát khi ai đó **sửa giờ chuyến** qua `PUT /routes/{routeId}/trips/{id}`. Xe chạy chậm ngoài đời mà không ai sửa `DepartureTime` thì không có thông báo — đó là việc của luồng vị trí/trạm (dòng 40–41), không phải dòng 44.
- **Huỷ chuyến bằng `DELETE` không phát thông báo.** Đó là nghiệp vụ huỷ chuyến, không phải trễ/đổi lịch trình; bảng phân công không có dòng nào cho thông báo huỷ chuyến nên không tự đặt ra loại mới. Đổi `status` sang `Cancelled` bằng PUT cũng im lặng.
- **Không có chốt chống trùng.** Dời giờ rồi dời lại về giờ cũ → khách nhận hai thông báo (một `TripDelayed`, một `TripScheduleChanged`) — đúng hai sự kiện đã xảy ra. Đây là khác biệt có chủ ý với hai notifier kia, không phải thiếu sót.
- **Chỉ in-app.** A8.5 chốt in-app qua SignalR + bảng `Notifications`, không email/SMS/Web Push. Dòng 42 (Web Push) là việc riêng, không đi qua notifier này.
- **Danh sách thông báo dài dần vô hạn** — mỗi lần dời giờ là một dòng cho mỗi khách. Cùng đánh đổi đã ghi ở `api-contract.md` mục `/notifications`; cách sửa khi cần là con trỏ `?before=`, không phải cắt ngầm.
- **Không mở endpoint, không đụng `Migrations/`, không đụng `frontend/`** — bốn giá trị thông báo mới và target `Trip` là việc nối UI của Băng/Hạnh (§5.2).
- **`NotificationResponse.cs` (file Dăm, dòng 45) còn ghi "sáu giá trị"/"ba target"** trong doc-comment. Không ảnh hưởng chạy — service chép chuỗi enum nguyên vẹn, nên hai loại `Trip…` tự động đi ra API; chỉ comment lệch, Dăm cập nhật khi tiện.

---

## 9. Bàn giao

| Việc | Ai | Ghi chú |
| --- | --- | --- |
| Nối hai loại `Trip…` + target `Trip` vào màn thông báo, sửa biểu tượng `TripDelayed` đang bị vẽ dấu X đỏ | **Băng / Hạnh** (47–48) | Hợp đồng đã cập nhật ở `api-contract.md`; `Title`/`Body` backend gửi sẵn |
| Cập nhật doc-comment `NotificationResponse.cs` từ sáu → tám loại, ba → bốn target | **Dăm** (45) | Enum thêm hộ trong PR này; không migration |
| Giữ `PUT /routes/{routeId}/trips/{id}` là cửa duy nhất đổi giờ chuyến; đường mới phải đi qua `IRouteTripsService` | **Hiếu** (40) | Sửa CSDL trực tiếp là thông báo biến mất, không có lỗi biên dịch |
| Dùng đúng lối hai khối `try` khi làm thông báo "xe cách trạm <500 m" | **Kiên** (41) | Nguồn khác — không dùng lại `TripDelayed` |
| 23 ca test đã có | **Giàng A Vàng** | §7 |
| Đối chiếu enum/target trong file mình, ghi "thêm hộ" trong PR | **Dăm** (45–46) | Không migration |
