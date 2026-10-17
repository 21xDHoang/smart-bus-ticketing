# Báo cáo kiểm thử chéo — luồng huỷ / đổi vé (gửi yêu cầu · duyệt · tính phí)

| | |
|---|---|
| **Người thực hiện** | Giàng A Vàng (Kiểm thử) |
| **Task** | Sprint 4, story 5 — *Kiểm thử chéo luồng huỷ - đổi vé trên môi trường online* (dòng r13) |
| **Ngày** | 16/10/2026 |
| **Mốc so sánh** | `main` @ `7ec94a6` |
| **Trạng thái** | ⚠️ Chỉ hoàn thành phần kiểm thử **không cần môi trường online** — xem mục 1 |

> Mã lỗi trong báo cáo này (**D1**…) là cục bộ cho báo cáo này, **khác** mười một bộ đã dùng ở mười
> một báo cáo trước: `L` (`bao-cao-kiem-thu-sprint-1.md`), `C` (lịch trình sinh chuyến), `P` (phân
> công), `N` (tra cứu), `V` (vé tháng), `F` (phản ánh), `G` (giữ chỗ), `T` (API thanh toán
> sandbox), `H` (thanh toán), `I` (mua vé nhận QR), `K` (voucher). Mười hai bộ không liên tục và
> không thay thế nhau — `D` được chọn vì `A`, `B`, `E`, `J`, `M`, `O`, `Q`, `R`, `S`, `U` còn
> trống, và `D` gợi "đổi".
>
> **D8 lặp lại ở báo cáo thứ mười một** — đúng chuỗi mười một lần, không phải lần thứ hai mươi:
> `L20` (Sprint 1) → `P9` (phân công) → `N9` (tra cứu) → `V7` (vé tháng) → `F9` (phản ánh) → `G7`
> (giữ chỗ) → `T3` (sandbox) → `H9` (thanh toán) → `I11` (mua vé nhận QR) → `K8` (voucher) → **D8**.
> Ghi lại là bằng chứng nó vẫn chưa được xử lý, **không phải lỗi mới**.

---

## 1. Phạm vi — phần KHÔNG làm được và vì sao

Task yêu cầu kiểm thử **trên môi trường online**. Ba báo cáo gần nhất đã ghi cùng một thiếu hụt và
tình hình **không đổi**: nhóm có dữ liệu dùng chung (PostgreSQL trên Supabase), nhưng **không có hệ
thống chạy chung** — API vẫn chỉ chạy ở `localhost` máy từng người.

| Kiểm tra | Báo cáo vé tháng | Báo cáo gần nhất | Nay |
|---|---|---|---|
| CSDL dùng chung cho cả nhóm | ✅ Supabase | ✅ | ✅ vẫn có |
| API chạy ngoài máy cá nhân | ❌ không | ❌ không | ❌ **vẫn không** |
| `render.yaml` / `fly.toml` / `Procfile` / `vercel.json` / `docker-compose.yml` | ❌ không | ❌ không | ❌ **vẫn không** |
| Workflow deploy trong `.github/` | ❌ chỉ `ci.yml` | ❌ chỉ `ci.yml` | ❌ **vẫn chỉ `ci.yml`** |
| `appsettings.Production.json` | ❌ không | ❌ không | ❌ **vẫn không** |
| `frontend/.env.example` | `localhost:5080` | `localhost:5080` | ❌ **vẫn vậy** |
| `backend/Dockerfile` | ✅ có | ✅ có | ✅ vẫn có — **`git log` cho thấy nó được viết cho Render (commit `fb49b74`), nhưng chưa từng được deploy** |
| Dòng task deploy trong backlog | ❌ không | ❌ không | ❌ **vẫn không có dòng nào** ở cả Sprint 4 lẫn Sprint 5 |

**Hệ quả:** không chạy được một request HTTP thật nào *qua mạng*. Phần đã làm thay thế — như các báo
cáo trước — là **kiểm thử chéo có bằng chứng chạy lại được**:

1. **Đo trên pipeline HTTP thật** bằng `TestAppFactory` (app thật + EF InMemory, `JwtAuthTests.cs:291`).
   Hai harness tạm: một dựng dữ liệu để **dò 6 endpoint dự kiến** (kèm các lượt `PATCH` và các ca
   `401`/`403`/`404`/`400`/`409`), một dựng tuyến / xe / hai chuyến / bốn vé / hai yêu cầu đổi để
   **chạy trọn nhánh duyệt đổi chuyến** và đọc lại CSDL sau lượt duyệt. Gọi đúng những endpoint mà
   ba module API của frontend **sẽ gọi khi bỏ cờ dữ liệu giả**. Số liệu ở mục 2.1.
   Harness chỉ để đo, **đã xoá trước khi commit**.
2. **Chạy lại hàm tính phí huỷ của frontend** trên chính `dayjs` mà frontend dùng, ở 9 mốc thời
   gian cộng 4 lần dò sát hai ngưỡng, để đối chiếu với quy định trong tài liệu. Số liệu ở mục 2.2.
3. **Đối chiếu bốn phía**: mã nguồn frontend ↔ mã nguồn backend ↔ `docs/32-csdl-huy-doi-ve.md` ↔
   `docs/api-contract.md`.

---

## 2. Mốc chuẩn trên `main` @ `7ec94a6`

| Hạng mục | Lệnh | Kết quả |
|---|---|---|
| Test backend | `dotnet test backend/SmartBus.sln -c Release` | ✅ **1438/1438 pass**, 0 fail, 0 skip, 3 m 43 s |
| Build frontend | `npm --prefix frontend run build` | ✅ exit 0 — chỉ còn cảnh báo chunk > 500 kB |
| Lint frontend | `npm --prefix frontend run lint` | ⚠️ 4 cảnh báo — `StopManagePage.tsx:34`, `HomePage.tsx:194`, `SeatMapPage.tsx:203`, `TicketDownloadShare.tsx:44`, **cả bốn ngoài luồng huỷ/đổi** |
| Rác debug | grep `console.log` / `debugger` / `TODO` / `FIXME` trong `frontend/src` + `backend/SmartBus.Api` | ✅ **0 kết quả** |
| Bí mật bị commit | `git ls-files` lọc `.env`, `appsettings.Development.json`, `*.pfx`, `*.pem`, `id_rsa` | ✅ sạch |
| Migration đã có trên `main` | `ls backend/SmartBus.Api/Migrations/` | ✅ `20261012023745_Sprint4_CancellationRequests_TicketChanges`, `20261013143603_Sprint4_Notifications`, `20261014153025_Sprint4_Refunds_RefundLogs` — **cả ba đã merge**, xem D7 |

### 2.1 Đo trên pipeline HTTP thật — bảng số liệu

| Endpoint | Kết quả | Content-Type | Body |
|---|---|---|---|
| `POST /api/cancellation-requests` | **404** | *(không có)* | *(rỗng)* |
| `POST /api/ticket-changes` | **404** | *(không có)* | *(rỗng)* |
| `GET /api/ticket-requests/me` | **404** | *(không có)* | *(rỗng)* |
| `GET /api/cancellation-requests?status=Pending` | **404** | *(không có)* | *(rỗng)* |
| `GET /api/ticket-changes?status=Pending` | **404** | *(không có)* | *(rỗng)* |
| `GET /api/ticket-requests/me` (không token) | **404** | *(không có)* | *(rỗng)* |
| `PATCH /api/cancellation-requests/{id}` — Manager, vé `Paid` | **200** | `application/json` | đủ 19 trường |
| `PATCH /api/ticket-changes/{id}` — Manager, duyệt đổi chuyến | **200** | `application/json` | đủ 19 trường |
| `PATCH /api/cancellation-requests/{id}` — Admin, yêu cầu khác | **200** | `application/json` | — |
| `PATCH /api/cancellation-requests/{id}` — không token | **401** | `application/json` | `{"message":"Chưa đăng nhập. …"}` |
| `PATCH /api/ticket-changes/{id}` — Passenger | **403** | `application/json` | `{"message":"Bạn không có quyền truy cập tính năng này."}` |
| `PATCH /api/cancellation-requests/{id-lạ}` | **404** | `application/json` | `{"message":"Không tìm thấy yêu cầu huỷ vé."}` |
| `PATCH /api/ticket-changes/{id}` — duyệt **lại** cùng yêu cầu | **409** | `application/json` | `{"message":"Yêu cầu đã được người khác xử lý. …"}` |
| `PATCH /api/ticket-changes/{id}` — ghế mới đã bị chiếm | **409** | `application/json` | `{"message":"Ghế khách xin đổi sang đã có người đặt trong lúc chờ duyệt. …"}` |
| `PATCH /api/ticket-changes/{id}` — `status` rác | **400** | `application/json` | `{"message":"Trạng thái không hợp lệ. …","errors":{"status":[…]}}` |
| `PATCH /api/ticket-changes/{id}` — từ chối **không kèm** `rejectReason` | **200** | `application/json` | `"status":"Rejected"`, `"rejectReason":null` — xem D5 |

**Chi tiết đáng chú ý nhất trong bảng — và là xương sống của D1 và D2:**

Bảng có **hai loại 404 khác nhau về bản chất**, và chỉ nhìn mã trạng thái thì không phân biệt được:

- Dòng `PATCH /api/cancellation-requests/{id-lạ}` trả **404 kèm `Content-Type: application/json` và
  một câu `message`** → *route khớp, nhưng bản ghi không tồn tại* → **endpoint có thật**.
- Sáu dòng đầu trả **404 với `Content-Type` không có và body rỗng** → *routing không khớp được route
  nào* → **endpoint không tồn tại**.

Nếu sáu endpoint đầu **tồn tại nhưng chặn quyền**, lượt gọi không token sẽ trả **401** (đúng như dòng
`PATCH` không token ở trên: route có thật, middleware xác thực chặn trước). Ra 404 không `Content-Type`
nghĩa là chưa có gì để chặn — request không tới được controller nào. Đây là bằng chứng cơ học, không
phải suy đoán từ việc đọc code.

Và điều tương tự cũng đúng với **ba** dòng `GET`: chúng không phải *"endpoint có nhưng trả rỗng"* —
chúng không có ai trả lời.

### 2.2 Hàm tính phí huỷ của frontend — chạy lại trên chính `dayjs` của frontend

`mockCancellationFee` (`frontend/src/api/ticketChangeRequestApi.ts:75-80`):

```ts
const hoursUntil = dayjs(departureTime).diff(dayjs(), 'hour', true);
if (hoursUntil >= 24) return 0;
if (hoursUntil >= 12) return Math.round(price * 0.3);
return Math.round(price * 0.5);
```

Với `price = 250 000`, `departureTime = now + <số giờ>` , gọi hàm thật trong `frontend/`:

| Còn lại tới giờ chạy | Phí hàm trả về | % |
|---|---|---|
| 30 giờ | 0 | 0% |
| **24 giờ** (đúng ngưỡng) | 0 | 0% |
| 13 giờ | 75 000 | 30% |
| **12 giờ** (đúng ngưỡng) | 75 000 | 30% |
| 11 giờ | 125 000 | 50% |
| 3 giờ | 125 000 | 50% |
| **2 giờ** | 125 000 | 50% |
| 1 giờ | 125 000 | 50% |
| 0,5 giờ | 125 000 | 50% |

Hai dòng **đúng ngưỡng** ở trên là chỗ dễ đọc sai nhất, nên đo riêng cho chắc: hàm dùng
`>=`, nên `24 giờ` **đã** được miễn phí và `12 giờ` **đã** rơi vào mức 30% — ngưỡng tính từ phía
sớm hơn, không phải phía muộn hơn. Chỉ cần lệch xuống một chút là đổi mức ngay
(`23,9999 giờ` → 30%, `11,9999 giờ` → 50%).

Ba điều đọc ra từ bảng này:

- **Chỉ có hai ngưỡng, không phải ba.** Quy định trong tài liệu là *"trước 24h / 12h / **2h**"*
  (`docs/32-csdl-huy-doi-ve.md:5` và `:221`), nhưng hàm chỉ rẽ nhánh ở **24h** và **12h** — **không
  có mốc 2h nào**. Và **không tài liệu nào trong repo ghi ra phần trăm**: con số 30% và 50% chỉ tồn
  tại trong hàm này, không ở đâu khác.
- **Dữ liệu giả của chính frontend mâu thuẫn với hàm này.** `ticketRequestApi.ts:225` và
  `ticketRequestAdminApi.ts:251` đều có một bản ghi `Rejected` với lý do *"Chuyến khởi hành dưới 2
  giờ nên không đủ điều kiện hủy."* — tức là theo dữ liệu giả, dưới 2 giờ thì **không được huỷ**;
  còn theo hàm, dưới 2 giờ thì **huỷ được với phí 50%**. Hai câu trả lời cho cùng một ca.
- **Mức 50% là mức duy nhất áp cho mọi ca dưới 12 giờ** — kể cả vé sát giờ chạy. Đây chính là ca mà
  quy định "2h" trong `docs/32` có ý định chặn; hiện hàm không chặn.

---

## 3. Danh sách lỗi

Mức độ: **Cao** = sai chức năng hoặc gây hiểu nhầm nghiêm trọng · **TB** = ảnh hưởng trải nghiệm
hoặc rủi ro · **Thấp** = tài liệu, vệ sinh mã.

| # | Lỗi | Mức | Người xử lý |
|---|---|---|---|
| D1 | Nửa **GHI** của story 5 không tồn tại: `POST /cancellation-requests` và `POST /ticket-changes` đều 404 — màn gửi yêu cầu đã merge nhưng không có gì để gọi | 🔴 Cao | Trần Trung Hiếu (r3) + Nguyễn Duy Kiên (r4) |
| D2 | Cả **HAI endpoint ĐỌC** đều thiếu **và không được phân công cho ai**: ba lượt `GET` 404, không có dòng nào trong `api-contract.md` lẫn backlog, nhưng `docs/32` §5 đã thiết kế và migration đã tạo chỉ mục cho đúng chúng | 🔴 Cao | **nhóm chốt người** — rồi Trần Trung Hiếu |
| D3 | Quy định phí huỷ chỉ tồn tại dưới dạng **hàm giả ở frontend**: hai mốc thay vì ba, không tài liệu nào ghi phần trăm, backend không có service nào; khách thấy số của client trong khi Admin đọc số của server | 🔴 Cao | Nguyễn Duy Kiên (r6) + **nhóm chốt con số** |
| D4 | Hợp đồng trường đã chốt nhưng frontend chưa theo: 2 tên đổi, 10 trường hiển thị backend không trả, và kiểu trả về của lượt gửi thiếu đúng 2 trường mà màn kết quả hiển thị | 🟠 TB | Dương Thị Hạnh + Nguyễn Đình Băng |
| D5 | Quy tắc *"từ chối phải có lý do"* bị đặt **sai tầng** và comment mô tả **sai hợp đồng**; màn theo dõi của khách **bỏ im lặng** lý do rỗng | 🟠 TB | Dương Thị Hạnh (r8, r11) |
| D6 | Hợp đồng lỗi của lớp dữ liệu giả bị vứt: mock ném `Error` thường, trang đọc `customMessage` → mất câu 409 | 🟡 Thấp | Dương Thị Hạnh + Nguyễn Đình Băng |
| D7 | Bốn comment/tài liệu nói sai tình trạng backend — cả bốn đã đúng ở `main` từ 12–14/10 | 🟡 Thấp | Trần Trung Hiếu · Dương Thị Hạnh · Vàng Thị Dăm |
| D8 | `package-lock.json` lạc ở gốc repo (**lần thứ mười một**) | 🟡 Thấp | Phùng Duy Hoàng |

> **Về việc gán người xử lý:** tên người được suy từ sheet `Sprint 4` của
> `Product_Backlog_Smart_Bus.xlsx` (r2–r13 là story 5). Cần đối chiếu lại nếu phân công đã đổi.
>
> ⚠️ **Cột `Status` của backlog đang lạc hậu và không dùng được làm căn cứ.** Cả 12 dòng r2–r13 đều
> ghi *"Chưa làm"*, nhưng thực tế `main` **đã có**: migration của r2, hai controller `PATCH` của r5,
> màn hình của r8 và r11, component của r9, và màn theo dõi của r10. Ba bộ test ghim chỗ nối
> (`TicketRequestReviewWiringTests`, `TicketRequestResultWiringTests`,
> `TicketRequestResultNotifierTests`) cũng đã merge. Nói cách khác **r2, r5, r8, r9, r10, r11 xong**;
> còn **r3, r4, r6 chưa** (D1, D3) và **việc đọc chưa nằm trong backlog** (D2).

---

### D1 — Nửa GHI của story 5 không tồn tại; màn gửi yêu cầu đã merge mà không có gì để gọi 🔴 Cao

**Đường đi của lỗi:**

1. `MyTicketsPage.tsx:247-256` — nút **"Huỷ / Đổi vé"** trên mỗi vé còn dùng được (`isTicketUsable`,
   `:237`) → `navigate('/my-tickets/:ticketId/request')` (`:253`)
2. `App.tsx:250` — route `/my-tickets/:ticketId/request` (không guard — màn của hành khách)
3. `TicketChangeRequestPage.tsx:160-171` — dựng payload và gọi `ticketRequestApi.submit(...)`:
   `{ type: 'Cancel', ticketId, reason }` và
   `{ type: 'Change', ticketId, newTripId, newSeatId, reason }`
4. `ticketChangeRequestApi.ts:142` — nhánh API thật `throw new Error('Backend huỷ/đổi vé chưa có — chưa thể gửi yêu cầu thật.')`; bật cờ lên là chết ở đây
5. `ticketChangeRequestApi.ts:11-12` — endpoint dự kiến: `POST /cancellation-requests`, `POST /ticket-changes`
6. **Đo được: cả hai đều 404, không `Content-Type`, body rỗng** (mục 2.1) — routing không khớp route nào
7. `docs/api-contract.md:3722-3723` — hai dòng tương ứng vẫn ghi ⛔ *"chưa có trong tài liệu này"*
8. `grep "CancellationFee" backend/SmartBus.Api --include=*.cs` (trừ `Migrations`) → chỉ có schema,
   DTO và service **đọc**; **không** service tính phí nào (D3)

**Vì sao đây là lỗi chứ không phải "chờ API":** nút **"Huỷ / Đổi vé"** trên `MyTicketsPage` là
**đường vào duy nhất** của màn gửi yêu cầu — không menu nào trỏ thẳng tới
`/my-tickets/:ticketId/request`, nên không có đường vòng nào khác. Và nút đó hiện ra cho **mọi** vé
`isTicketUsable` (`:237`): khách bấm vào, điền lý do, chọn chuyến mới, bấm gửi — và không có gì xảy
ra. `docs/32` §6 đã ghi rõ hai việc này là của ai (Hiếu dòng 3, Kiên dòng 4), nên đây **không phải
lỗ hổng phối hợp** như `V4` của báo cáo vé tháng — đây là hai task **đã biết và chưa làm**, chỉ có
điều nửa giao diện đã xong trước nửa backend.

**Các bước tái hiện (không cần môi trường online):**

1. Trong `frontend/src/api/ticketChangeRequestApi.ts:133`, đổi `const USE_MOCK_DATA = true` → `false`
   — đúng việc comment ngay trên đó dặn người sau làm.
2. Đăng nhập vai trò Passenger, mở `/my-tickets`.
3. Bấm **"Huỷ / Đổi vé"** trên một vé `Paid`, chọn "Huỷ vé", nhập lý do, bấm gửi.

**Kết quả hiện tại:** `message.error` với câu `'Không thể gửi yêu cầu.'`
(`TicketChangeRequestPage.tsx:190` — chuỗi dự phòng, vì lỗi ném ra là `Error` thường, xem D6).

**Hướng sửa:** Trần Trung Hiếu làm `POST /cancellation-requests` (r3) và Nguyễn Duy Kiên làm
`POST /ticket-changes` (r4) **theo đúng trình tự** `docs/32` §6 đã dặn: kiểm điều kiện thời gian
**trước**, rồi mới tính `CancellationFee` / tra `NewPrice`, và **chụp `DepartureAt`**. Hai endpoint
phải vào `api-contract.md` **trước** khi có ai gọi. Chú ý luật liên bảng ở `docs/32` §4.1: khách gửi
một yêu cầu huỷ **và** một yêu cầu đổi trên cùng một vé thì CSDL không chặn được (hai bảng có chỉ
mục riêng) — service phải chặn.

---

### D2 — Cả hai endpoint ĐỌC đều thiếu và không được phân công cho ai 🔴 Cao

Cùng gốc với D1 nhưng **khác bản chất**, và đây là phát hiện chính của báo cáo này.

Ba module API của frontend khai ba endpoint sẽ gọi khi bỏ cờ dữ liệu giả:

| Module | Endpoint dự kiến | Ghi ở đâu |
|---|---|---|
| Màn theo dõi của khách (r10) | `GET /ticket-requests/me?status=…` | `ticketRequestApi.ts:14` |
| Hàng đợi duyệt (r11) | `GET /cancellation-requests?status=…` | `ticketRequestAdminApi.ts:14` |
| Hàng đợi duyệt (r11) | `GET /ticket-changes?status=…` | `ticketRequestAdminApi.ts:15` |

**Đo được: cả ba lượt `GET` đều 404 với `Content-Type` không có và body rỗng** (mục 2.1). Không
route nào khớp.

**Điều biến đây từ "chờ API" thành lỗ hổng thật — ba tầng đã làm phần của mình, chỉ thiếu người làm
phần ghép nối:**

1. **Tầng thiết kế CSDL đã làm xong.** `docs/32-csdl-huy-doi-ve.md:203-207` §5 liệt kê chỉ mục theo
   **đúng câu hỏi nghiệp vụ**, và có hai dòng khớp từng chữ với hai endpoint trên:
   > | "Yêu cầu huỷ - đổi của tôi" | `IX_CancellationRequests_UserId` · `IX_TicketChanges_UserId` |
   > | Hàng đợi duyệt của Admin (cũ nhất trước) | `IX_CancellationRequests_Status_CreatedAt` · `IX_TicketChanges_Status_CreatedAt` |
2. **Tầng migration đã làm xong.** `20261012023745_Sprint4_CancellationRequests_TicketChanges.cs:122-181`
   **tạo thật** cả bốn chỉ mục đó (13 chỉ mục trong file, kể cả `IX_CancellationRequests_TicketId_Pending`
   và `IX_TicketChanges_TicketId_Pending`). Chỉ mục cho một truy vấn chưa ai viết.
3. **Tầng giao diện đã làm xong.** Hai màn hình đã merge (`App.tsx:220`, `App.tsx:353-359`), có menu
   riêng (`navItems.tsx:120-125` vai `PASSENGER`, `:270-275` vai `ADMIN_MANAGER`), có bộ lọc trạng
   thái, có bảng, có phân trang, có trạng thái rỗng, trạng thái lỗi và nút thử lại.

**Và đây là chỗ thủng:** `api-contract.md` liệt kê **đúng bốn** endpoint cho US 5
(`:3720-3723`), trong đó hai dòng `POST` được đánh ⛔ *"chưa có trong tài liệu này"*. Ba endpoint
`GET` **không xuất hiện ở đâu cả** — kể cả dưới dạng ⛔. Nghĩa là chúng không chỉ chưa được viết:
chúng **chưa từng được đưa vào kế hoạch**. Đối chiếu sheet `Sprint 4` của backlog, story 5 có 12
dòng (r2–r13): migrate, POST huỷ, POST đổi, PATCH, service phí, log + thông báo, màn gửi yêu cầu,
component chọn chuyến, màn theo dõi, màn Admin duyệt, test xUnit, kiểm thử chéo. **Không có dòng nào
cho API đọc danh sách.** Sheet `Sprint 5` cũng không có.

Hệ quả trực tiếp: hai task đã được đánh dấu xong là **r10 (Nguyễn Đình Băng, màn theo dõi của khách)
và r11 (Dương Thị Hạnh, màn Admin duyệt)** không có nguồn dữ liệu nào trong hệ thống. Chúng chỉ chạy
được nhờ dữ liệu giả, và **sẽ không có gì để trỏ vào khi backend xong phần ghi** — vì D1 sửa xong thì
khách gửi được yêu cầu, nhưng vẫn không màn nào đọc lại được chúng.

**Các bước tái hiện:** bật cờ dữ liệu giả của cả ba module (`ticketRequestApi.ts:128`,
`ticketChangeRequestApi.ts:133`, `ticketRequestAdminApi.ts:43`) thành `false`, rồi mở
`/my-ticket-requests` và `/admin-ticket-requests`.

**Hướng sửa — cần một quyết định của nhóm, không phải một dòng code:**

1. **Nhóm chốt ai làm** (chưa ai được giao), rồi người đó viết ba endpoint. Hình dạng đề nghị: trả
   **mảng trần** cùng khuôn `TicketRequestResponse` đã có (`Dtos/TicketRequests/TicketRequestResponse.cs`,
   19 trường), để hai màn hình đọc được cả hai loại yêu cầu bằng **một** lượt gọi. Ghi chú trong file
   đó đã dặn đúng điều này: *"Trường nào của riêng một loại thì `null` ở loại kia, KHÔNG lược bỏ khỏi
   JSON"*.
2. **Thêm ba dòng vào `api-contract.md` trước** — kèm tập giá trị `status` nhận được (đúng ba:
   `Pending`/`Approved`/`Rejected`) và luật quyền (màn của khách: `[Authorize]` trần, **không** policy
   — xem mục 4).
3. Quyết định luôn câu hỏi mà cả ba module đang **tự trả lời ở client**: mười trường hiển thị
   (`routeCode`, `routeName`, `origin`, `destination`, `departureTime`, `seatNumber`, `newRouteCode`,
   `newRouteName`, `newDepartureTime`, `newSeatNumber`) — hợp đồng dặn frontend nối thêm bằng
   `GET /tickets/{ticketId}`, nhưng **API đọc danh sách trả nhiều dòng cho một bảng** thì nối từng
   dòng là N+1. Đây là cơ hội để làm đúng một lần: cho endpoint đọc trả kèm các trường hiển thị, rồi
   bỏ mười trường đó khỏi kiểu dữ liệu giả (D4).

---

### D3 — Quy định phí huỷ chỉ tồn tại dưới dạng hàm giả ở frontend 🔴 Cao

**Ba tầng, ba câu trả lời khác nhau, và không tầng nào là nguồn sự thật:**

| Tầng | Quy định phí | Ở đâu |
|---|---|---|
| Tài liệu nghiệp vụ | trước **24h / 12h / 2h** — **không nêu phần trăm** | `docs/32-csdl-huy-doi-ve.md:5`, `:221` |
| Backend | **không có gì** | `grep "CancellationFee" backend/SmartBus.Api --include=*.cs` (trừ `Migrations`) → schema, DTO và service **đọc**; không service nào **tính** |
| Frontend | **hai ngưỡng, ba mức** — 0% / 30% / 50% | `ticketChangeRequestApi.ts:75-80` (`mockCancellationFee`) |

Con số 30% và 50% **không xuất hiện ở bất kỳ tài liệu nào trong repo** — `grep "30%"` và `grep "50%"`
trên `docs/` không ra dòng nào thuộc luồng này. Chúng chỉ tồn tại trong một hàm dữ liệu giả.

**Bốn hệ quả, theo thứ tự nghiêm trọng:**

1. **Mốc thứ ba mất tích.** Tài liệu và backlog (r6: *"Service tính phí huỷ theo quy định (trước 24h
   / 12h / 2h)"*) đều nói ba mốc; hàm chỉ có hai. Với chuyến còn 1 giờ, hàm trả **50%** — nhưng dữ
   liệu giả của chính frontend (`ticketRequestApi.ts:225`, `ticketRequestAdminApi.ts:251`) lại ghi lý
   do từ chối *"Chuyến khởi hành dưới 2 giờ nên không đủ điều kiện hủy."* Cùng một ca, hai câu trả lời
   trái ngược, nằm trong **cùng một thư mục**.
2. **Ranh giới hai ngưỡng không ổn định.** Điều kiện là `hoursUntil >= 24` và `>= 12`, còn
   `hoursUntil` là `diff(…, 'hour', true)` — số thực. Đo sát ngưỡng cho thấy chỉ cần lệch
   **một phần vạn giờ** là đổi mức: `24,0001` → 0% nhưng `23,9999` → 30%; `12,0001` → 30% nhưng
   `11,9999` → 50%. Không sai về mặt tiền — nhưng nghĩa là hai hàng "đúng ngưỡng" ở mục 2.2 **chỉ
   đúng khi phép trừ ra đúng con số tròn**, và một hàng test viết theo kiểu *"chuyến còn đúng 24
   giờ"* sẽ lúc xanh lúc đỏ tuỳ mili-giây. Đây là lý do kỹ thuật để phần tính phí **không** nên
   nằm ở client: server chụp `DepartureAt` một lần rồi tính trên con số đã chốt.
3. **Khách và Admin sẽ đọc hai con số khác nhau cho cùng một yêu cầu.** `TicketChangeRequestPage.tsx`
   tính phí ở client lúc khách bấm gửi (`:160`, và xem trước ở `:247`) và hiển thị ngay, **trước khi**
   gửi (`:328-338`); sau khi có phản hồi thì **không tính lại** (`:189-190` chỉ hiện thông báo lỗi). Còn Admin đọc
   `cancellationFee` do server trả. Khi server tính theo quy định thật (24h/12h/2h, phần trăm do
   nhóm chốt), con số khách đã nhìn thấy và con số Admin đang duyệt sẽ lệch nhau — và `docs/32` §2
   nói rõ phí được **chụp lại lúc gửi**, tức là số của khách phải là số của server, không phải số
   của trình duyệt.
4. **Phí là "quy định sửa được", nhưng đang bị chôn trong một file `.ts` của frontend.**
   `docs/32:55-56` giải thích vì sao phí phải chụp lại: nó là kết quả của *"một quy định sửa được"*
   và của *"khoảng cách tới giờ chạy tại đúng lúc khách bấm gửi"*. Một quy định sửa được mà nằm
   trong `mockCancellationFee` thì mỗi lần đổi phải phát hành lại giao diện — và Admin vẫn dùng số cũ.

**Hướng sửa — hai việc, một trong đó là quyết định kinh doanh:**

1. **Nhóm chốt phần trăm** cho từng mốc (0% / ?% / ?%), và **chốt mốc thứ ba là 2 giờ hay bỏ**.
   Ghi vào `docs/32` §2 để nó thành tài liệu, không phải một hằng số trong code.
2. **Nguyễn Duy Kiên** viết service tính phí (r6) đọc quy định đó; `POST /cancellation-requests` của
   Hiếu (r3) gọi nó và trả `cancellationFee` **trong phản hồi**. Rồi `TicketChangeRequestPage.tsx`
   **bỏ** `mockCancellationFee` khỏi đường gửi và hiển thị số server trả về. Nếu muốn vẫn xem trước
   được giá, thì ô xem trước phải gọi một endpoint tính thử — **không** chép công thức sang client,
   vì đó chính là cách lỗi này sinh ra.

---

### D4 — Hợp đồng trường đã chốt, frontend chưa theo 🟠 TB

`docs/api-contract.md` **đã chốt** dạng trường ở mục *"Hình dạng một dòng yêu cầu — chốt theo bản
nháp FE"* (bảng 19 trường ở `:3681-3700`), và có một blockquote nói thẳng những chỗ phải đổi
(`:3702-3709`):

| Việc frontend phải làm | Hợp đồng ghi ở đâu | Frontend đang làm gì |
|---|---|---|
| `fee` → tách thành `cancellationFee` (huỷ) **và** `priceDifference` (đổi) | `:3705` | vẫn một trường `fee` (`ticketRequestApi.ts:67`) |
| `reviewNote` → **`rejectReason`** | `:3703-3704` | vẫn `reviewNote` ở khắp ba module và hai trang |
| Bổ sung `price`, `newPrice`, `ticketId`, `reviewedByUserId`, `reviewedAt`, `newTicketId` | `:3707-3708` | kiểu dữ liệu giả **chỉ có `price`**; năm trường còn lại không có |
| Nối 10 trường hiển thị qua `GET /tickets/{ticketId}` | `:3682` (`ticketId` — *"FE mở thẳng `GET /tickets/{ticketId}` từ đây"*), `:3697` (vé mới) | `MyTicketRequestsPage.tsx` **chỉ** import `ticketRequestApi`, không hề gọi `ticketApi`; `ticketRequestAdminApi.ts` cũng không. Hai màn danh sách không có đường nào lấy 10 trường đó |

Câu *"Bản nháp cũng **thiếu** `price`, `newPrice`, … — đều là trường màn duyệt của Admin cần (Hạnh,
dòng 11)"* ở `:3707-3708` là hợp đồng **chỉ đích danh người phải sửa**. Đây không phải việc suy đoán
từ bảng phân công.

**Vì sao vẫn là lỗi dù cờ dữ liệu giả đang bật:** ba module này không chỉ là dữ liệu giả — chúng
định nghĩa **kiểu dữ liệu** mà hai trang đọc. Khi bỏ cờ, `selected.reviewNote`
(`AdminTicketRequestPage.tsx:461`, `MyTicketRequestsPage.tsx:209`) đọc một trường server **không
bao giờ trả** → `undefined` → khối "Ghi chú nhà xe" và khối lý do từ chối **biến mất im lặng**, kể
cả khi server có trả `rejectReason` — và nó **có**: `rejectReason` là trường thật, xuất hiện trong
mọi phản hồi `200` ở mục 2.1. Cùng cách đó, `formatVnd(selected.fee)` (`:439`) và `formatVnd(request.fee)`
(`moneySummary`, `:51`, `:54`) sẽ in ra `NaN` — vì `fee` không tồn tại.

**Một chi tiết hẹp hơn nhưng nói lên cùng vấn đề:** kiểu trả về của **lượt gửi** yêu cầu
(`SubmittedTicketChange`, `ticketChangeRequestApi.ts:35-45`) chỉ có `{ id, type, status, createdAt }`.
Thiếu đúng hai trường mà màn kết quả ngay sau đó hiển thị: `cancellationFee` và `refundAmount`. Nên
kể cả khi D1 xong, trang vẫn **không có cách nào** hiển thị số tiền server đã tính — nó buộc phải
dùng số của client (D3, hệ quả 3).

**Hướng sửa:** đổi tên ba trường trong kiểu dữ liệu và ba module; thêm năm trường còn thiếu; thêm
`cancellationFee`/`refundAmount` vào kiểu trả về của lượt gửi. **Thứ tự bắt buộc:** sửa kiểu dữ liệu
và module trước (không phụ thuộc backend), rồi mới tới hai trang. Chờ D2 chốt xong hình dạng endpoint
đọc để biết 10 trường hiển thị đến từ đâu — sửa chúng hai lần là công toi.

---

### D5 — Quy tắc "từ chối phải có lý do" bị đặt sai tầng và mô tả sai hợp đồng 🟠 TB

`AdminTicketRequestPage.tsx:110-111` khẳng định:

> *"Từ chối BẮT BUỘC có lý do — `RejectReason` là thứ gửi về cho khách, để trống là thông báo cụt
> (docs/33 §"…Rejected")."*

Và code ép đúng như vậy — `:192-195`:

```ts
const reason = rejectReason.trim();
if (!reason) {
  message.warning('Vui lòng nhập lý do từ chối.');
  return;
}
```

**Nhưng hợp đồng nói ngược lại.** `api-contract.md:3738`:

> *"Bỏ trống vẫn hợp lệ — thông báo ghi rõ **'Người duyệt không nêu lý do.'** thay vì để cụt"*

Và **đo được** (mục 2.1, dòng cuối): `PATCH /api/ticket-changes/{id}` với `{"status":"Rejected"}`
**không kèm `rejectReason` → 200**, `rejectReason: null` trong phản hồi. Backend làm đúng hợp đồng —
câu thay thế đã có sẵn ở `TicketRequestResultNotifier.cs:58`
(`NoReasonSuffix = " Người duyệt không nêu lý do."`), ghép vào chuỗi qua `ReasonSuffix` (`:134-137`) và
được dùng ở **cả hai** nhánh từ chối (`:95`, `:122`).

Ba hệ quả:

1. **Comment nói sai hợp đồng** — người sau đọc sẽ tưởng trường là NOT NULL bắt buộc, và có thể đi
   "sửa" backend cho khớp giao diện. Đây đúng loại lỗi mà `V5` của báo cáo vé tháng gọi là *"một
   comment sai không chỉ gây nhầm — nó giữ nguyên một công tắc"*.
2. **Giao diện chặn một thao tác mà server cho phép.** Quản lý muốn từ chối nhanh và để hệ thống tự
   nêu câu "không nêu lý do" thì **không làm được** từ màn hình này. Không phải lỗi bảo mật, nhưng là
   một quy tắc nghiệp vụ sống ở tầng trình duyệt thay vì ở service.
3. **Màn theo dõi của khách bỏ im lặng lý do rỗng.** `MyTicketRequestsPage.tsx:209` là
   `{request.reviewNote && (…)}` — không có lý do thì **cả khối biến mất**, không một chữ nào. Khách
   mở màn hình thấy yêu cầu "Đã từ chối" và không biết vì sao. Câu "Người duyệt không nêu lý do."
   hiện chỉ có trong **thông báo SignalR**, mà thông báo là thứ trôi qua — màn theo dõi là chỗ khách
   quay lại xem. (Và vì D4, trường đang được đọc tên `reviewNote` chứ không phải `rejectReason`, nên
   kể cả server có gửi thì khối này vẫn biến mất.)

**Hướng sửa:** sửa comment `:110-111` cho khớp hợp đồng; nới `:192-195` thành **không bắt buộc**
(nhưng vẫn giới hạn 2000 ký tự — trần mà `api-contract.md:3738` ghi và `:3796` xác nhận là lý do
duy nhất gây 400 cho trường này); và ở `MyTicketRequestsPage.tsx:209` thay `&&` bằng một nhánh
`else` in câu *"Người duyệt không nêu lý do."* — **cùng câu chữ** với `NoReasonSuffix` của backend,
để hai nơi không nói hai kiểu.

---

### D6 — Hợp đồng lỗi của lớp dữ liệu giả bị vứt 🟡 Thấp

Hai lớp dữ liệu giả ném lỗi chuẩn bị kỹ, rồi bị trang vứt đi:

| Nơi ném | Câu lỗi | Trang đọc gì |
|---|---|---|
| `ticketRequestAdminApi.ts:278` (duyệt trùng) | `new Error('Yêu cầu đã được người khác xử lý.')` | |
| `ticketRequestAdminApi.ts:295` (từ chối trùng) | `new Error('Yêu cầu đã được người khác xử lý.')` | |
| `ticketChangeRequestApi.ts:155` (vé không có) | `new Error('Không tìm thấy vé đã chọn.')` | |

Cả ba đều là `Error` **thường**, và cả ba trang đều đọc
`(err as AppError).customMessage || '<câu dự phòng>'` (`AdminTicketRequestPage.tsx:171`, `:206`;
`TicketChangeRequestPage.tsx:190`). `Error` thường **không có** `customMessage` → luôn rơi vào câu
dự phòng chung chung. Câu *"Yêu cầu đã được người khác xử lý."* — viết ra để mô tả **đúng ca 409**
mà backend cũng trả một câu gần y hệt (đo được, mục 2.1) — chưa bao giờ tới được mắt người dùng.

Trớ trêu là **nhánh API thật sẽ làm đúng**: `axiosClient.ts:105-136` (`toAppError`) ánh xạ
`{message}` của backend vào `customMessage` cho đúng 400/401/403/404/409. Nên đây là nghịch lý của
lớp giả: **đường thật xử lý lỗi tốt hơn đường giả.**

**Hướng sửa:** cho ba chỗ ném `AppError` (kèm `customMessage`) thay vì `Error`, để dữ liệu giả dạy
đúng hợp đồng lỗi mà đường thật sẽ gặp.

---

### D7 — Bốn comment/tài liệu nói sai tình trạng backend 🟡 Thấp

| # | Vị trí | Câu sai | Đúng |
|---|---|---|---|
| 1 | `TicketRequestReviewService.cs:128` | *"KHÔNG ghi dòng hoàn tiền nào: bảng `Refunds` thuộc **US 8, chưa tồn tại**"* | Migration `20261014153025_Sprint4_Refunds_RefundLogs` **đã merge**; bảng có thật, câu "chưa tồn tại" giờ sai (nửa còn lại của câu thì vẫn đúng — **chưa ai ghi dòng nào**, xem §3.2) |
| 2 | `ticketRequestApi.ts:7-8` | *"Backend **CHƯA có** bảng `TicketChanges` / `CancellationRequests` (migration Sprint 4 của Dăm)"* | Migration `20261012023745` **đã merge** từ trước báo cáo này |
| 3 | `docs/33-thong-bao-ket-qua-huy-doi-ve.md` (mở đầu) | *"Migration **CHƯA sinh**"* | `20261013143603_Sprint4_Notifications` **đã merge** |
| 4 | `docs/33` §3.4 | *"`Program.cs` **chưa nối**"*, kèm hai dòng code cần thêm | Cả hai **đã có**: `Program.cs:520` (`AddScoped<ITicketRequestResultNotifier, …>`) và `Program.cs:634` (`app.MapHub<NotificationHub>("/hubs/notifications")`) |

Dòng #4 đáng chú ý vì nó **không chỉ sai — nó đang mô tả một triệu chứng đã biến mất.** §3.4 cảnh
báo rằng cho tới khi hai dòng đó có mặt thì *"triệu chứng duy nhất là một dòng log warning mỗi lượt
duyệt"*. Hai dòng đã có, nên ai đọc tài liệu để đi tìm dòng warning đó sẽ không thấy, và mất thời
gian đi tìm một lỗi không tồn tại. Bộ test ghim chỗ nối cũng **đã merge**
(`TicketRequestResultWiringTests`, `TicketRequestResultNotifierTests`) — tức §3.5 của `docs/33`
(*"Test ghim chỗ nối — chưa viết được"*) cũng đã lạc hậu.

**Hướng sửa:** #1 Trần Trung Hiếu · #2 Dương Thị Hạnh · #3–#4 Vàng Thị Dăm (`docs/33` là tài liệu
của Dăm, cùng lối `docs/32`).

---

### D8 — `package-lock.json` lạc ở gốc repo 🟡 Thấp

File 98 byte, chưa ai theo dõi, nằm ở `C:\smart-bus-ticketing\package-lock.json` (gốc repo, **không**
phải trong `frontend/`). Ghi lại **lần thứ mười một**: `L20` (Sprint 1) → `P9` (phân công) → `N9`
(tra cứu) → `V7` (vé tháng) → `F9` (phản ánh) → `G7` (giữ chỗ) → `T3` → `H9` (thanh toán) → `I11`
(mua vé nhận QR) → `K8` (voucher) → **D8**. Ngoài luồng huỷ/đổi. Hoặc xoá, hoặc cho vào `.gitignore`
(`.gitignore` thuộc Phùng Duy Hoàng — mục E1).

---

## 3.2 Mắt xích lân cận — chỗ luồng huỷ/đổi nối sang luồng tiền

**Duyệt một yêu cầu huỷ trả về `refundAmount` nhưng KHÔNG ghi dòng `Refunds` nào.**

Đo được (mục 2.1 và bản đầy đủ trong phụ lục): `PATCH /api/cancellation-requests/{id}` duyệt xong trả
`"refundAmount": 174999.99` — tính đúng `price - cancellationFee` = `199999.99 - 25000`, chính xác
tới từng xu, không trôi số thực. Nhưng **không có dòng `Refund` nào được tạo**:

```
grep -rn "\.Refunds\b" backend/SmartBus.Api --include=*.cs | grep -v Migrations
  → RefundAttemptService.cs:56     var refund = await _db.Refunds…
  → RefundRetryService.cs:48       var dueRefundIds = await _db.Refunds…
  → (không dòng nào khác)
```

Cả hai chỗ đều là **đọc**. Không chỗ nào `_db.Refunds.Add(...)`. Bảng `Refunds` đã có (migration
`20261014153025` đã merge), `RefundSource.TicketCancellation` đã có, `IRefundAttemptService` đã được
đăng ký (`Program.cs:567`) và job `RefundRetryJob` đã chạy theo lịch — nhưng **chưa có gì tạo ra một
khoản hoàn tiền để job đó xử lý.** Chuỗi tiền dừng ở con số trong JSON.

Chính hợp đồng đã nói trước điều này, ở blockquote ngay dưới bảng trường (`api-contract.md:3713-3715`):

> *"Số tiền **đã** hoàn thật nằm ở bảng `Refunds` (US 8, sprint khác) — không phải trường này."*

**Đây KHÔNG phải lỗi mới phát hiện — nó đã được phân công và đang được theo dõi.** Sheet `Sprint 4`,
dòng **r39**: *"Service xử lý hoàn tiền tự động theo quy định"* — Nguyễn Duy Kiên, `Cao`, **"Chưa
làm"**; `docs/40-csdl-hoan-tien.md` §6 cũng giao đúng việc tạo `Refund` cho Kiên. Ghi ở đây để nối
hai báo cáo (task `r49` *"Kiểm thử chéo luồng hoàn tiền"* sẽ là chỗ đào tiếp), **không phải** để mở
một mục lỗi cho nó.

**Mức độ trùng lặp — nói thẳng:** `D1` ở báo cáo này **trùng gốc** với `V1` (báo cáo vé tháng) và
`N1` (báo cáo tra cứu) ở chỗ *giao diện đã xong trước backend*. Khác một điểm quan trọng: `V1` và
`N1` là giao diện gọi **sai endpoint có thật** (quyền 403); `D1` và `D2` là giao diện gọi **endpoint
không tồn tại**. Cái sau không sửa được bằng cách đổi một dòng ở frontend.

---

## 4. Đã kiểm và ĐẠT

### Hai endpoint `PATCH` — đúng hợp đồng, và phần khó nhất thì đúng

| Kiểm | Kết quả |
|---|---|
| `PATCH /api/cancellation-requests/{id}` duyệt, Manager | ✅ **200**, phản hồi đủ **19 trường** khớp từng tên với bảng ở `api-contract.md:3681-3700` |
| `PATCH /api/ticket-changes/{id}` duyệt, Manager | ✅ **200**, `newTicketId` được ghi, `priceDifference: 50000` = `300000 - 250000` — **đúng quy ước dấu** của `docs/32` (*"dương = khách trả thêm"*) |
| Từ chối | ✅ **200**; `newTicketId` giữ `null`, vé **không bị đụng** — đúng như comment `:187-189` giải thích ("không cấp ghế cho ai thì kiểm làm gì") |
| Đổi chuyến: **vé cũ** sau khi duyệt | ✅ `status = Cancelled`, `updatedAt` được ghi — vé chết ở bước duyệt, **không** phải lúc khách gửi |
| Đổi chuyến: **vé mới** sau khi duyệt | ✅ vé **mới** được phát hành, không sửa vé cũ: `Paid`, `price = 300000` (lấy từ `NewPrice` đã chụp, không tra lại `Fares`), đúng `trip` mới, đúng `seat` mới, `user` **là chính khách cũ** |
| Mã vé mới | ✅ sinh bằng `ITicketQrService.GenerateCode(newTicketId)` — `SBT1:{id-vé}.{chữ ký}`; id phải có **trước** khi ký và code làm đúng thứ tự đó |
| Không token | ✅ **401** *"Chưa đăng nhập. …"* |
| Passenger gọi | ✅ **403** *"Bạn không có quyền truy cập tính năng này."* — `RbacPolicies.ManagerOrAbove` |
| Id không tồn tại | ✅ **404** *"Không tìm thấy yêu cầu huỷ vé."* |
| `status` rác | ✅ **400** kèm `errors.status` — **đúng một định dạng lỗi duy nhất** của dự án |
| Duyệt **lại** cùng yêu cầu | ✅ **409** *"Yêu cầu đã được người khác xử lý. Hãy tải lại để xem kết quả mới nhất."* |
| **Ghế mới đã bị chiếm trong lúc chờ duyệt** | ✅ **409** *"Ghế khách xin đổi sang đã có người đặt trong lúc chờ duyệt. Hãy từ chối yêu cầu và mời khách chọn ghế khác."* — phép kiểm **mô tả** của `docs/32` §3.3 chạy đúng |

Ca ghế bị chiếm là ca đáng giá nhất, và nó **được đo bằng cách để nó tự sinh ra**: duyệt yêu cầu 1
phát hành một vé `Paid` trên `(chuyến mới, ghế A2)`, rồi duyệt yêu cầu 2 cũng nhắm đúng cặp đó —
backend chặn và trả 409 chứ không phải 500. `docs/32` §3.3 cảnh báo rằng ghế mới **không** được giữ
lúc khách gửi, nên phép kiểm lại ngay lúc duyệt là **bắt buộc**, và nó có mặt.

### Một chi tiết của hợp đồng mà dễ bị sửa cho sai

`api-contract.md:3740` ghi:

> ⚠️ **Gửi `rejectReason` kèm `"Approved"` KHÔNG phải lỗi 400** — service bỏ qua và **không lưu**

Đối chiếu code: `TicketRequestReviewService.cs:106` và `:191` chỉ gán `RejectReason` ở nhánh
`Rejected`, còn nhánh `Approved` không đụng tới trường này. Khớp hợp đồng. Đây là loại chi tiết mà
một lượt "dọn code" sẽ dễ biến thành 400 — ghi lại để lần sau ai đọc hợp đồng thấy ngay là **cố ý**.

### Quyền của hai màn hình mới — phân biệt đúng, không sao chép máy móc

`App.tsx` đặt hai màn hai kiểu, và **cả hai đều đúng**:

| Màn | Bọc `RouteGuard` | Vì sao đúng |
|---|---|---|
| `/my-ticket-requests` (`:220`) | ❌ **không** | Màn của hành khách; hai API nó gọi là `[Authorize]` trần, không policy |
| `/admin-ticket-requests` (`:353-359`) | ✅ `allowedRoles={['Admin','Manager']}` | Màn quản trị; API là `[Authorize(Policy = ManagerOrAbove)]` |

Đây là chỗ **hiểu đúng** ranh giới: backend ở đây đã chặn đúng (`401`/`403` đo được), `RouteGuard` chỉ
là lớp che giao diện. Cùng lối lập luận với `RoutesController` ở báo cáo vé tháng, nhưng ở đó thì
**sai** (màn hành khách gọi endpoint quản trị) còn ở đây thì **đúng**. Ghi ra để thấy `RouteGuard`
không phải thứ máy móc bật cho mọi route.

### Ba phép tính thuần ở frontend — dùng chung một nguồn, không chép công thức

- `priceDiffOf` (`newTripCompare.ts:24-30`) — một chỗ duy nhất định nghĩa quy ước dấu, khớp từng chữ
  với `docs/32` §6 và khớp với `priceDifference` backend trả (đo được `50000` cho `300000 - 250000`).
- `timeDiffLabel` (`:36-50`) và `priceDiffOf` đều **hàm thuần, không đụng React** — tách khỏi `.tsx`
  theo đúng lối `satisfactionScale.ts`, `ticketStatus.ts`. Kiểm được độc lập, không cần dựng app.
- Ba trạng thái `'Pending' | 'Approved' | 'Rejected'` khai **một chỗ** (`ticketRequestApi.ts:24`) và
  `TICKET_REQUEST_STATUS_OPTIONS` sinh ra bộ lọc của màn Admin, nên nhãn bộ lọc không thể lệch với
  nhãn thẻ trạng thái. **Đúng ba giá trị**, khớp `docs/32` §6 (*"Tập giá trị để vẽ là đúng ba ở mỗi
  enum… thêm giá trị là đổi hợp đồng với frontend"*).

### Một quan sát nhỏ, chưa đủ để thành lỗi

`docs/api-contract.md:3738` hứa rằng `rejectReason` bỏ trống thì **thông báo** ghi *"Người duyệt
không nêu lý do."* — và backend làm đúng (`TicketRequestResultNotifier.cs:58` qua `ReasonSuffix`
`:134-137`). Hợp đồng **không**
hứa màn theo dõi của khách cũng phải nói câu đó, nên việc `MyTicketRequestsPage.tsx:209` để khối
trống không phải vi phạm hợp đồng — nó chỉ là một lỗ nhỏ về trải nghiệm khi khách quay lại màn hình
sau khi thông báo đã trôi qua. Ghi vào D5 như một hệ quả, **không** tách thành một mục lỗi riêng.

---

## 5. Việc chuyển cho ai

| Việc | Người | Vì sao là của họ |
|---|---|---|
| `POST /cancellation-requests` (D1) | **Trần Trung Hiếu** | r3; `docs/32` §6 ghi rõ tên và trình tự (kiểm thời gian **trước**, rồi tính phí, rồi chụp `DepartureAt`) |
| `POST /ticket-changes` (D1) | **Nguyễn Duy Kiên** | r4; kèm `docs/32` §6 dặn: chênh lệch **KHÔNG** ghi vào CSDL |
| Service tính phí huỷ (D3) | **Nguyễn Duy Kiên** | r6; chủ `CancellationFee` |
| **Chốt phần trăm phí + mốc thứ ba** (D3) | **nhóm** | Con số là quyết định kinh doanh — `docs/32:55` gọi phí là *"một quy định sửa được"*, không phải hằng số kỹ thuật |
| **Chốt người làm ba endpoint đọc** (D2) | **nhóm** | Chưa ai được giao ở bất kỳ sprint nào — đây là việc phải quyết trước khi ai code |
| Ba endpoint đọc + ba dòng vào `api-contract.md` (D2) | **Trần Trung Hiếu** (sau khi nhóm chốt) | Chủ `api-contract.md`; quy ước 5: sửa contract **trước**, báo người còn lại, rồi mới code |
| Tên trường, 10 trường hiển thị, kiểu trả về của lượt gửi (D4) | **Dương Thị Hạnh** | r8 + r11; ba module API nằm trong luồng hai màn này |
| `App.tsx` nếu phải đổi route (D2) | **Nguyễn Đình Băng** | `App.tsx` thuộc mục E1 |
| Quy tắc "từ chối phải có lý do" + câu cho lý do rỗng (D5) | **Dương Thị Hạnh** | r8 + r11 |
| Hợp đồng lỗi của lớp dữ liệu giả (D6) | **Dương Thị Hạnh** + **Nguyễn Đình Băng** | Ba module chia nhau giữa hai người |
| Comment sai về `Refunds` (D7 #1) | **Trần Trung Hiếu** | `TicketRequestReviewService.cs` |
| Comment sai về hai bảng (D7 #2) | **Dương Thị Hạnh** | `ticketRequestApi.ts` |
| `docs/33` mở đầu + §3.4/§3.5 (D7 #3–#4) | **Vàng Thị Dăm** | Tài liệu của Dăm, cùng lối `docs/32` |
| `package-lock.json` (D8) | **Phùng Duy Hoàng** | `.gitignore` thuộc E1 |

---

## 6. Còn lại — chưa kiểm được

| Hạng mục | Vì sao chưa kiểm được |
|---|---|
| Bất kỳ request HTTP thật nào qua mạng | Không có môi trường online (mục 1) |
| API trỏ vào CSDL Supabase chung | Máy này không có `appsettings.Development.json`; chuỗi kết nối nằm ngoài repo (repo public) |
| **Phí huỷ do backend tính** | Không tồn tại ở tầng nào — không endpoint (`POST` 404), không service (D1, D3). Số ở mục 2.2 là **số của client** |
| **Ba endpoint đọc** | Route không tồn tại (D2) |
| Nhánh 409 *"Chuyến xin đổi sang đã huỷ hoặc đã chạy xong"* | Có trong code (`TicketRequestReviewService.cs:203-207`) và trong hợp đồng (`api-contract.md:3802`), **nhưng báo cáo này chưa dựng ca đo trực tiếp** — cần một chuyến `Cancelled`/`Completed` làm `NewTrip` |
| Lượt cưỡng chế bằng **chỉ mục A6** (`IX_Tickets_TripId_SeatId_Active`, nhánh `catch DbUpdateException`) | InMemory **không cưỡng chế unique index** — `docs/32` §4.4 đã cảnh báo đúng điều này. Ca đo được ở mục 4 đi qua phép kiểm **mô tả** ở `:212`, không qua chỉ mục. Muốn kiểm thật phải chạy trên PostgreSQL |
| Job `RefundRetryJob` có thật sự nhặt được khoản nào không | Không có dòng `Refund` nào để nhặt (§3.2) |
| Luồng tiền chênh lệch khi **đổi** chuyến (`Payments`) | Chưa có endpoint nào ghi; `docs/api-contract.md:3787` nói tiền chênh đi qua `Payments` — chưa có gì để đo |
| Đa trình duyệt / đa thiết bị | Không có môi trường để mở |
| Thông báo SignalR tới trình duyệt | Phần **ghi** `Notifications` đã có test ghim trên `main` (`TicketRequestResultNotifierTests`); phần đẩy tới trình duyệt cần client thật, ngoài phạm vi báo cáo này |

---

## 7. Phụ lục — lệnh chạy lại mọi bằng chứng trong báo cáo

```bash
# Mốc chuẩn
"C:/Users/ASUS/.dotnet/dotnet.exe" test backend/SmartBus.sln -c Release   # 1438/1438
npm --prefix frontend run build                                           # exit 0
npm --prefix frontend run lint                                            # 4 cảnh báo cũ
grep -rn "console\.log\|debugger\|TODO\|FIXME" frontend/src backend/SmartBus.Api | wc -l   # 0
git ls-files | grep -iE "\.env$|appsettings\.Development\.json$|\.pfx$|\.pem$|id_rsa"       # (rỗng)

# D1/D2 — endpoint nào KHÔNG tồn tại (đo bằng TestAppFactory; harness đã xoá sau khi đo)
#   404 + KHÔNG có Content-Type + body rỗng  =>  routing không khớp route nào
grep -n "HttpPost\|HttpGet\|HttpPatch\|Route(" \
  backend/SmartBus.Api/Controllers/CancellationRequestReviewController.cs \
  backend/SmartBus.Api/Controllers/TicketChangeReviewController.cs      # chỉ có PATCH
grep -n "cancellation-requests\|ticket-changes\|ticket-requests" docs/api-contract.md
grep -n "USE_MOCK_DATA" frontend/src/api/ticketRequestApi.ts \
  frontend/src/api/ticketChangeRequestApi.ts \
  frontend/src/api/ticketRequestAdminApi.ts

# D2 — chỉ mục cho truy vấn chưa ai viết (phải thấy đủ bốn tên)
grep -n "IX_CancellationRequests_UserId\|IX_TicketChanges_UserId\|IX_CancellationRequests_Status_CreatedAt\|IX_TicketChanges_Status_CreatedAt" \
  backend/SmartBus.Api/Migrations/20261012023745_Sprint4_CancellationRequests_TicketChanges.cs
grep -n "Yêu cầu huỷ - đổi của tôi\|Hàng đợi duyệt của Admin" docs/32-csdl-huy-doi-ve.md

# D3 — quy định phí: tài liệu nói ba mốc, code có hai, không ai ghi phần trăm
grep -n "24h\|12h\|2h" docs/32-csdl-huy-doi-ve.md
grep -n "hoursUntil\|0.3\|0.5" frontend/src/api/ticketChangeRequestApi.ts
grep -rln "CancellationFee" backend/SmartBus.Api --include=*.cs | grep -v Migrations   # không service nào TÍNH
grep -n "dưới 2 giờ" frontend/src/api/ticketRequestApi.ts frontend/src/api/ticketRequestAdminApi.ts

# D3 — chạy lại ĐÚNG 4 dòng thân hàm trên chính dayjs của frontend (script tạm, đã xoá sau khi đo)
#   const hoursUntil = dayjs(departureTime).diff(dayjs(), 'hour', true);
#   if (hoursUntil >= 24) return 0;
#   if (hoursUntil >= 12) return Math.round(price * 0.3);
#   return Math.round(price * 0.5);
#   -> thay dayjs bằng đường dẫn frontend/node_modules/dayjs; price = 250000
#   -> 9 mốc 30/24/13/12/11/3/2/1/0.5 giờ, cộng 4 lần dò sát ngưỡng 24.0001/23.9999/12.0001/11.9999

# D4 — tên trường hợp đồng đã chốt vs tên frontend đang dùng
grep -n "cancellationFee\|rejectReason\|reviewNote\|priceDifference" docs/api-contract.md | sed -n '1,12p'
grep -n "fee\b\|reviewNote" frontend/src/api/ticketRequestApi.ts frontend/src/api/ticketRequestAdminApi.ts
grep -rn "tickets/\${" frontend/src   # không file nào nối GET /tickets/{id} cho 10 trường hiển thị

# D5 — quy tắc "bắt buộc" ở client vs hợp đồng "bỏ trống vẫn hợp lệ"
sed -n '110,111p;192,195p' frontend/src/pages/AdminTicketRequestPage.tsx
sed -n '3736,3741p' docs/api-contract.md
grep -n "NoReasonSuffix" backend/SmartBus.Api/Services/TicketRequestResultNotifier.cs

# D6 — mock ném Error thường, trang đọc customMessage
grep -n "throw new Error" frontend/src/api/ticketRequestAdminApi.ts frontend/src/api/ticketChangeRequestApi.ts
grep -n "customMessage" frontend/src/pages/AdminTicketRequestPage.tsx frontend/src/pages/TicketChangeRequestPage.tsx
grep -n -A 6 "function toAppError" frontend/src/api/axiosClient.ts

# D7 — comment/tài liệu lạc hậu
sed -n '126,130p' backend/SmartBus.Api/Services/TicketRequestReviewService.cs
sed -n '5,10p' frontend/src/api/ticketRequestApi.ts
grep -n "CHƯA sinh\|chưa nối" docs/33-thong-bao-ket-qua-huy-doi-ve.md
grep -n "TicketRequestResultNotifier\|hubs/notifications" backend/SmartBus.Api/Program.cs
ls backend/SmartBus.Api/Migrations/ | grep Sprint4

# §3.2 — duyệt huỷ KHÔNG ghi dòng Refunds nào (chỉ đọc)
grep -rn "\.Refunds\b" backend/SmartBus.Api --include=*.cs | grep -v Migrations
grep -rn "Refunds.Add\|new Refund(" backend/SmartBus.Api --include=*.cs | grep -v Migrations   # (rỗng)

# D8
ls -la package-lock.json
```

---

*Báo cáo này **không** sửa một dòng code nghiệp vụ nào — đúng ranh giới "Người kiểm thử — Giàng A
Vàng" ở `docs/03-quy-uoc.md` mục 2.4. Harness đo ở mục 2.1 là file tạm, **đã xoá trước khi commit**.
Mọi mục trong mục 3 kèm các bước tái hiện để người sở hữu file tự sửa.*
