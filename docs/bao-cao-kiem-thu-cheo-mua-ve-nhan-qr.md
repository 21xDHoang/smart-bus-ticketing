# Báo cáo kiểm thử chéo — luồng mua vé → nhận QR

| | |
|---|---|
| **Người thực hiện** | Giàng A Vàng (Kiểm thử) |
| **Task** | Sprint 3, story 4 — *Kiểm thử chéo luồng mua vé → nhận QR trên môi trường online* (dòng r49) |
| **Ngày** | 11/10/2026 |
| **Mốc so sánh** | `main` @ `5f03357` |
| **Trạng thái** | ⚠️ Chỉ hoàn thành phần kiểm thử **không cần môi trường online** — xem mục 1 |

> Mã lỗi trong báo cáo này (**I1**…) là cục bộ cho báo cáo này, **khác** các bộ đã có:
> `L1`–`L20` (`docs/bao-cao-kiem-thu-sprint-1.md`), `C1`–`C6` (`-cheo-lich-trinh-sinh-chuyen`),
> `P1`–`P9` (`-cheo-phan-cong`), `N1`–`N9` (`-cheo-tra-cuu`), `V1`–`V7` (`-cheo-ve-thang`),
> `F1`–`F9` (`-cheo-phan-anh`), và — **chưa lên `main`** — `G…` (`feature/3-kiem-thu-cheo-giu-cho`),
> `T…` (`feature/6-test-api-thanh-toan-sandbox-3-cong`), `H1`–`H9`
> (`feature/6-kiem-thu-cheo-thanh-toan-online`). Các bộ không liên tục và không thay thế nhau.
>
> **`I11` lặp lại ở báo cáo thứ chín** (`L20` → `P9` → `N9` → `V7` → `F9` → `G7` → `T3` → `H9` → `I11`).
> Ghi lại là bằng chứng về việc nó vẫn chưa được xử lý, không phải lỗi mới.

---

## 1. Phạm vi — phần KHÔNG làm được và vì sao

Task yêu cầu kiểm thử **trên môi trường online**. Bảy báo cáo trước đều tắc ở cùng một chỗ: nhóm chưa
có môi trường online nào. **Báo cáo này tắc thêm ở tầng thứ hai, và tầng này mới là tầng chặn thật.**

### 1.1 Tầng chặn thứ nhất — vẫn chưa có môi trường online

| Kiểm tra | Bảy báo cáo trước | Nay |
|---|---|---|
| CSDL dùng chung cho cả nhóm | ❌ → ✅ từ 03/10/2026 | ✅ Supabase, `docs/25-huong-dan-csdl-chung.md` |
| API chạy ngoài máy cá nhân | ❌ | ❌ **vẫn không** — tài liệu vẫn ghi `http://localhost:5080` |
| `render.yaml` / `fly.toml` / `Procfile` / `docker-compose.yml` | ❌ | ❌ **vẫn không** |
| Workflow deploy | ❌ | ❌ **vẫn chỉ `ci.yml`** (build + test, không deploy) |
| URL online ở đâu đó trong repo | ❌ | ❌ **không có** |
| `appsettings.Production.json` | ❌ | ❌ **vẫn không** |
| Task deploy trong backlog | ❌ | ❌ **không có dòng nào** |

Chốt việc tạo môi trường online đã được báo cáo `-cheo-phan-cong` nêu từ Sprint 2; tới nay là **báo cáo
thứ tám** nhắc lại, và **chưa ai nhận việc này**.

### 1.2 Tầng chặn thứ hai — đầu đọc của chuỗi chưa có API

Khác bảy báo cáo trước (endpoint có thật, đo được 200/401/403/404), **lần này phần lớn chuỗi chưa có
tầng HTTP**. Đo trên 32 controller của `SmartBus.Api/Controllers/`:

- **Không có `TicketsController`** — `GET /tickets/me` và `GET /tickets/{id}` mà `docs/api-contract.md`
  liệt trong bảng endpoint (`:3118-3119`) đều ghi *"Chưa làm"*, và cả hai trả **404** trên pipeline thật.
- **Không có controller thanh toán nào** — năm endpoint cộng job đối soát của mục Thanh toán
  (`api-contract.md:3219-3229`) đều *"Chưa làm"* hoặc *"Chờ bảng"*.
- **`api-contract.md` chốt thẳng việc phát hành vé KHÔNG phải endpoint** (`:3122`): nó là service nội bộ
  gọi từ IPN/đối soát, và *"chưa dựng"* (`Program.cs:383`).

**Hệ quả:** kể cả có môi trường online thì cũng **không có gì để gọi**. Phần đã làm thay thế — như bảy
báo cáo trước — là **kiểm thử chéo có bằng chứng chạy lại được**:

1. **Đo trên pipeline HTTP thật** bằng `TestAppFactory` (app thật + EF InMemory): 12 request thật, phân
   biệt được **404 có thân** (routing khớp, controller tự trả) với **404 không thân** (không route nào
   khớp). Số liệu ở mục 2.1.
2. **Đo trên tầng ký mã QR thật**: đem đúng 6 mã QR mà frontend đang hiển thị qua
   `TicketQrService.TryVerify` — bộ kiểm **duy nhất** đang tồn tại. Số liệu ở mục 2.2.
3. **Đo logic thuần của frontend** bằng `node` (chỗ suy "vé còn quét được"). Số liệu ở mục 2.3.
4. Đối chiếu ba phía: mã nguồn frontend ↔ mã nguồn backend ↔ `docs/api-contract.md` + `docs/29` + `docs/31`.

### 1.3 Chuỗi thực tế đang đi tới đâu

Chuỗi mà tài liệu và chính comment trong `App.tsx` mô tả:

```
tra cứu tuyến → kết quả chuyến → sơ đồ ghế → giữ chỗ → chọn phương thức thanh toán
              → chuyển sang cổng → chờ kết quả → VÉ QR
```

Đếm trên mã nguồn: **mũi tên cuối cùng chưa có ai nối.** Riêng phần "nhận QR" **đã có ba mảnh thật ở
backend** — ký mã QR (`TicketQrService`), vẽ PDF vé có QR (`TicketPdfService`), gửi kèm email
(`TicketEmailService`) — nhưng theo đồ thị GitNexus, **cả ba đều có 0 lời gọi trong mã sản phẩm**
(chỉ test gọi). `TicketPdfService` có đúng **1** caller là `TicketPdfServiceTests.cs`; `ITicketEmailService`
chỉ có quan hệ `implements`, không có caller nào. Việc nối đã được ghi sẵn cho Hiếu ở `docs/31:142` —
đây là **xác nhận trạng thái**, không phải phát hiện mới.

---

## 2. Mốc chuẩn trên `main` @ `5f03357`

| Hạng mục | Lệnh | Kết quả |
|---|---|---|
| Test backend | `dotnet test backend/SmartBus.Tests/SmartBus.Tests.csproj -c Release` | ✅ **1121/1121 pass**, 0 fail, 0 skip (nhánh này, có thêm 13 ca của dòng r48: **1134/1134**) |
| Build backend | `dotnet build backend/SmartBus.sln -c Release` | ✅ 0 warning, 0 error |
| Build frontend | `npm --prefix frontend run build` | ✅ exit 0 — chỉ còn cảnh báo chunk > 500 kB |
| Lint frontend | `npm --prefix frontend run lint` | ⚠️ **1 cảnh báo** — `StopManagePage.tsx:33`, **ngoài** luồng vé |
| Rác debug | grep `console.log` / `debugger` / `TODO` / `FIXME` trong `frontend/src` + `backend/SmartBus.Api` | ✅ **0 dòng** (báo cáo `-cheo-ve-thang` từng thấy 1 dòng `TODO` ở `tripAssignmentApi.ts:90` — đã được dọn) |
| Bí mật bị commit | `git ls-files` lọc `.env`, `appsettings.Development.json`, `*.pfx`, `*.pem`, `id_rsa` | ✅ sạch |

### 2.1 Đo trên pipeline HTTP thật — 12 request

Dựng một hành khách có vai trò `Passenger` trong CSDL test (đúng lệ `SeatHoldCreateApiTests` — provider
InMemory không chạy `HasData` nên phải seed vai trò trước), rồi gửi request thật qua `TestAppFactory`.

| # | Request | Không token | Có token hành khách | Content-Type | Thân |
|---|---|---|---|---|---|
| A1 | `GET /api/trips/{id}/seats` | **404** | — | `application/json` | `{"message":"Không tìm thấy chuyến xe"}` |
| A2 | `GET /api/monthly-passes/me` | **401** | — | `application/json` | `{"message":"Chưa đăng nhập…"}` |
| A3 | `POST /api/seat-holds` | **401** | — | `application/json` | `{"message":"Chưa đăng nhập…"}` |
| A4 | `POST /api/vouchers/validate` | **401** | — | `application/json` | `{"message":"Chưa đăng nhập…"}` |
| B1 | `GET /api/tickets/me` | **404** | **404** | **(không có)** | **(rỗng)** |
| B2 | `GET /api/tickets/{id}` | — | **404** | **(không có)** | **(rỗng)** |
| B3 | `POST /api/tickets/validate` | — | **404** | **(không có)** | **(rỗng)** |
| B4 | `POST /api/tickets/{id}/validate` | — | **404** | **(không có)** | **(rỗng)** |
| C1 | `POST /api/payments` | — | **404** | **(không có)** | **(rỗng)** |
| C2 | `GET /api/payments/{code}` | — | **404** | **(không có)** | **(rỗng)** |
| C3 | `POST /api/payments/momo/callback` | — | **404** | **(không có)** | **(rỗng)** |

**Phép so sánh quyết định — hai loại 404 khác hẳn nhau, đây là bằng chứng cơ học chứ không phải suy luận:**

- **A1** trả 404 **có** `Content-Type` và **có** thân `{"message":…}`. Endpoint **có thật**, routing
  khớp, controller chạy rồi tự trả "không tìm thấy chuyến". 404 ở đây là *câu trả lời nghiệp vụ*.
- **B1–B4, C1–C3** trả 404 **không** `Content-Type`, thân **rỗng**, và **giống nhau dù có token hay
  không**. Đây là **routing không khớp được route nào** — endpoint không tồn tại. Nếu endpoint tồn tại
  mà chỉ thiếu quyền thì phải là **401**, như A2–A4.

Ba dòng **A2–A4** là **đối chứng dương**: cùng một app, cùng một client, ba endpoint có thật đều trả
401 kèm thân JSON đúng định dạng lỗi D3. Không có chúng thì "404 không thân" chỉ là một quan sát đơn lẻ.

### 2.2 Đo trên tầng ký mã QR thật — chéo frontend ↔ backend

Sinh một `TicketQrService` thật (khoá ký test), rồi đem **đúng 6 mã QR mà `ticketApi.ts` đang cho
hiển thị** qua `TryVerify`:

| Mã frontend đang hiển thị | Độ dài | `TryVerify` | Vì sao |
|---|---|---|---|
| `TK-01-A1B2C3` | 12 | **False** | không bắt đầu bằng `SBT1:` |
| `TK-08-D4E5F6` | 12 | **False** | nt |
| `TK-01-11A2B3` | 12 | **False** | nt |
| `TK-02-77C8D9` | 12 | **False** | nt |
| `TK-B10-3E4F5A` | 13 | **False** | nt |
| `TK-08-9B0C1D` | 12 | **False** | nt |

Đối chứng — mã do chính bộ ký thật sinh:

```
SBT1:96d07706-2758-419b-b2ba-3b16e113603e.4EbHI6aQBMhp8pIrSE59dLl6oV9wi-P6T3_A47at0y2NYb0VEl_WFRIEOc1MBUBg
→ 106 ký tự · TryVerify: True · id đọc lại KHỚP
```

**Vì sao 6/6 false:** `TryVerify` (`TicketQrService.cs:99-103`) từ chối thẳng mọi chuỗi không bắt đầu
bằng `SBT1:`. Mã giả của frontend là `TK-01-A1B2C3` — 12 ký tự, không có tiền tố phiên bản, không có
chữ ký. Hai định dạng **không cùng một họ**, nên đây không phải "chữ ký sai" mà là "sai định dạng".

**Mã của vé đã huỷ / đã dùng vẫn qua được bộ kiểm.** Sinh mã ký thật cho hai vé có thật trong CSDL
test, một `Status = 'Cancelled'`, một `Status = 'Used'`:

| Vé trong CSDL | `TryVerify` |
|---|---|
| `Status = 'Cancelled'` | **True** |
| `Status = 'Used'` | **True** |

`TryVerify` là hàm thuần trên chuỗi (`TicketQrService.cs:37-38` tự ghi *"không CSDL, không mạng, không
phụ thuộc thời gian"*), nên nó **không biết và không thể biết** vé đã bị huỷ. Đây đúng theo thiết kế của
service, nhưng nghĩa là: **hiện không có tầng nào trong hệ thống từ chối mã QR của một vé đã huỷ.**

### 2.3 Đo logic thuần của frontend bằng `node`

`frontend/src/components/ticketStatus.ts` chỉ `import type` nên nạp thẳng được bằng `node` 24 (type
stripping) — không cần dựng React, không cần hạ tầng test (frontend **không có** vitest/jest;
`frontend/package.json` chỉ có `dev/build/lint/preview`). Chạy 7 ca với `now` cố định
`2026-10-11T10:00:00.000Z`:

| Ca | Trạng thái hiển thị | Quét được? |
|---|---|---|
| `Paid` + chuyến tương lai | `Valid` | ✅ true |
| `Paid` + chuyến quá khứ | `Expired` | ❌ false |
| `Paid` + **đúng** mốc `now` | `Valid` | ✅ true |
| `Paid` + sát mốc `now` (1 ms sau) | `Valid` | ✅ true |
| `Used` + chuyến tương lai | `Used` | ❌ false |
| `Cancelled` + chuyến tương lai | `Cancelled` | ❌ false |
| `Cancelled` + chuyến quá khứ | `Cancelled` | ❌ false |

Cả 7 ca đúng như thiết kế ghi trong doc-comment: huỷ/đã-dùng **thắng** việc chuyến đã đi hay chưa, và
`Expired` là trạng thái **suy ra** từ `departureTime` chứ không phải giá trị lưu trong cột `Status` —
khớp bảng ba giá trị `Paid`/`Used`/`Cancelled` của `Entities/TicketStatus.cs` và của `api-contract.md`.

---

## 3. Danh sách lỗi

Mức độ: **Cao** = sai chức năng hoặc gây hiểu nhầm nghiêm trọng · **TB** = ảnh hưởng trải nghiệm hoặc
rủi ro · **Thấp** = tài liệu, vệ sinh mã.

### 3.1 Lỗi thuộc luồng mua vé → nhận QR (việc của báo cáo này)

| Mã | Mức | Lỗi | Chỗ | Người xử lý |
|---|---|---|---|---|
| **I1** | 🔴 Cao | **Đầu ra của chuỗi là dữ liệu giả, và nó tự nhận sai về backend.** `ticketApi.ts:65` `USE_MOCK_DATA = true`; nhánh API thật (`:72-79`) chỉ `throw new Error('Backend Tickets chưa có…')`. Comment `:5-8` khai *"Backend CHƯA có bảng Tickets (migration Sprint 3 của Dăm) và chưa có endpoint nào trong docs/api-contract.md"* — **sai cả hai vế**: entity + `AppDbContext.Ticket.cs` đã merge, migration `Sprint3_Payments_Vouchers_Tickets` đã có, và `api-contract.md:3084-3156` là một mục Vé điện tử đầy đủ. Hệ quả: màn "Vé của tôi" luôn hiện **6 vé bịa cố định**, và **vé khách vừa mua không bao giờ xuất hiện**. | `frontend/src/api/ticketApi.ts:5-8,65,72-79` | Nguyễn Đình Băng + Dương Thị Hạnh |
| **I2** | 🔴 Cao | **Mã QR đang hiển thị không qua được bộ kiểm duy nhất đang có.** 6/6 mã giả trả `TryVerify = False` (mục 2.2). Mã giả 12–13 ký tự, mã thật 106 ký tự có tiền tố `SBT1:` và chữ ký HMAC — không cùng một họ định dạng. Nghĩa là **mọi buổi demo "quét thử mã QR" trên màn hình hiện tại đều không chứng minh được gì**, và nếu ai đó tin mã giả là đúng khuôn thì sẽ dựng bộ kiểm sai. | `frontend/src/api/ticketApi.ts:100,116,132,148,164,180` | Nguyễn Đình Băng + Dương Thị Hạnh |
| **I3** | 🟠 TB | **Không tầng nào từ chối mã QR của vé đã huỷ / đã dùng.** Đo được: mã ký thật của vé `Status='Cancelled'` và vé `Status='Used'` đều trả `TryVerify = True` (mục 2.2). Bộ kiểm mới chỉ chống **làm giả**, chưa chống **dùng lại** — phần "vé còn hiệu lực không" phải do bên gọi tra CSDL, và **bên gọi đó chưa tồn tại**. Hiện chỉ có CSS `opacity: 0.35` (`TicketQr.tsx:30`) ngăn hiển thị, mà bôi mờ không phải là biện pháp chặn. | `Services/TicketQrService.cs:87-139` · màn `MyTicketsPage.tsx` / `TicketQrPage.tsx` | Trần Trung Hiếu (endpoint dòng 43) |
| **I4** | 🟠 TB | **Lối đi thật của người dùng kết thúc ở Trang chủ, không bao giờ tới được vé QR.** `SeatMapPage.tsx:108-119` đi thẳng `/payment-waiting` (tự ghi là *"lối tắt DEMO"*, mã thanh toán tự chế `demo-<tripId>`); màn chờ khi thành công (`PaymentWaitingPage.tsx:183-201`) chỉ có nút về `/` và `/route-lookup`. **Màn "Vé của tôi" không nằm trong luồng** — chỉ vào được từ mục menu. Nguyên nhân gốc (`/payment` không có lối vào) đã ghi ở `H1` của báo cáo luồng thanh toán; ở đây ghi **hệ quả cho đầu ra**: mua xong → về Trang chủ → QR không xuất hiện ở đâu. | `SeatMapPage.tsx:108-119` · `PaymentWaitingPage.tsx:183-201` | Nguyễn Đình Băng |
| **I5** | 🟡 Thấp | **Hai chỗ cùng nhãn "Vé của tôi", hai đích khác nhau.** Menu chính (`navItems.tsx:56`) → `/my-tickets` (vé điện tử, story 4); menu avatar (`App.tsx:110-116`) → **`/my-monthly-passes`** (vé tháng, story 16). Comment tại chỗ tự ghi *"nối vào màn vé tháng"*. Khách bấm "Vé của tôi" ở hai nơi ra hai màn khác nhau. | `App.tsx:110-116` | Nguyễn Đình Băng |

### 3.2 Lỗi ở mắt xích lân cận (ghi kèm — chuyển đúng người, không lấn sang)

| Mã | Mức | Lỗi | Chỗ | Người xử lý |
|---|---|---|---|---|
| **I6** | 🔴 Cao | **`GET /tickets/me` và `GET /tickets/{id}` chưa tồn tại** — 404 không thân, giống nhau dù có token hay không (mục 2.1). Hợp đồng đã chốt hình dạng response (`api-contract.md:3097-3112`, *"chốt theo bản nháp FE"*) nhưng chưa có controller nào. Đây là **tầng chặn chính** khiến I1/I2 chưa gỡ được cờ mock. Lưu ý cho người dựng: response cần `routeCode`/`routeName`/`origin`/`destination`/`seatNumber`/`departureTime`/`boardingStopName`/`alightingStopName` mà bảng `Tickets` chỉ có `TripId`/`SeatId`/`BoardingStopId`/`AlightingStopId` — tức phải join `Trips`→`Routes`→`Seats`→`Stops`, không phải đọc một bảng. Hợp đồng **không hứa thứ tự mảng**; FE đã tự sắp (`MyTicketsPage.tsx:48-65`) nên đừng thêm ràng buộc ngầm. | `api-contract.md:3118-3119` | Trần Trung Hiếu |
| **I7** | 🟠 TB | **Ba định dạng mã QR khác nhau đang được viết ở ba nơi.** Thật: `SBT1:{guid}.{Base64Url}` 106 ký tự (`docs/30 §1`). FE giả: `TK-01-A1B2C3`. `docs/29 §3` ví dụ body: `"TV-01-8F3A2C..."`. Chỉ cái đầu đúng. Ai code bộ kiểm theo hai tài liệu sau sẽ ra sản phẩm từ chối **mọi** mã thật. | `docs/29:61` · `frontend/src/api/ticketApi.ts:100+` | Vàng Thị Dăm (docs/29) |
| **I8** | 🟠 TB | **Đề xuất `POST /api/tickets/validate` đòi `tripId`, nhưng hình dạng `Ticket` đã chốt không có trường đó.** `docs/29 §3` để body `{code, tripId}` và `tripId` là trường *"Chuyến đang chạy của phụ xe"*. Bảng trường `Ticket` ở `api-contract.md:3099-3112` **không có `tripId`** — nên màn soát vé không lấy được giá trị đó từ dữ liệu FE đang có. Hai lối: thêm `tripId` vào hình dạng `Ticket`, hoặc bỏ `tripId` khỏi request vì mã QR đã mang sẵn `ticketId` (`SBT1:{ticketId}`) và bên kiểm tự tra ra chuyến. Chọn lối nào cũng phải sửa `api-contract.md` **trước** (luật 5). | `docs/29 §3` · `api-contract.md:3099-3112` | Trần Trung Hiếu (chốt) + Vàng Thị Dăm |
| **I9** | 🟡 Thấp | **`docs/29` đã lỗi thời.** §1 cảnh báo `docs/28`, `Entities/Ticket.cs`, `Data/AppDbContext.Ticket.cs` *"chưa có trên `main` cho tới khi PR của nhánh đó được merge"* — PR #144 **đã merge**, cả ba đều đã trên `main`. `docs/31:145` cũng đã ghi *"`docs/29 §5` ghi 'Chưa có file nào trong `Services/`' — đã lỗi thời"* cho Dăm. | `docs/29:9-11` | Vàng Thị Dăm (nếu muốn) |
| **I10** | 🟡 Thấp | **Ba mảnh backend của khâu "nhận QR" có thật nhưng chưa ai gọi.** `TicketPdfService` (1 caller duy nhất là test), `ITicketEmailService` (0 caller), `ShouldIssueTickets` (`PaymentSettlementService.cs:107` tính ra, **không nơi nào đọc**). Việc nối đã ghi sẵn ở `docs/31:142`. | `Program.cs:383,432-433` | Trần Trung Hiếu |
| **I11** | 🟡 Thấp | **`package-lock.json` ở gốc repo — lần thứ chín liên tiếp.** 98 byte, không bị `.gitignore` chặn. Chuỗi: `L20 → P9 → N9 → V7 → F9 → G7 → T3 → H9 → I11`. | gốc repo | Phùng Duy Hoàng |

---

## 4. Đã kiểm và ĐẠT

Không liệt kê là lỗi — ghi lại để lần sau khỏi kiểm lại.

| Hạng mục | Bằng chứng |
|---|---|
| **Định dạng mã QR thật đúng ngân sách cột** | Mã sinh ra 106 ký tự (`SBT1:` + Guid "D" 36 + `.` + blob Base64Url 64), cột `Code` khai `varchar(200)` — dư mà vẫn chặn được payload phình. |
| **Bộ kiểm đọc lại đúng id vé và chống làm giả** | `TryVerify` trên mã thật → `True` và id **KHỚP**; so sánh chữ ký bằng `CryptographicOperations.FixedTimeEquals`, không phải `==`. |
| **Sinh mã là tất định theo nonce, ngẫu nhiên theo lượt** | Nonce 16 byte mới mỗi lượt gọi — hai lượt cùng một vé ra hai mã khác nhau, đúng đường lùi cho lượt đâm chỉ mục UNIQUE `IX_Tickets_Code`. |
| **Ràng buộc CSDL của bảng `Tickets`** | Đã đo ở dòng r48 (`TicketQrIssuanceChainTests.cs`, 13 ca): `Code` `character varying(200) NOT NULL` + `IX_Tickets_Code` UNIQUE toàn bảng; `IX_Tickets_TripId_SeatId_Active` UNIQUE **có điều kiện** `WHERE "Status" IN ('Held','Paid')` đúng nguyên văn A6; `Price numeric(12,2)`; `CreatedAt`/`UsedAt`/`UpdatedAt` đều `timestamp with time zone`; cả **5** khoá ngoại `ON DELETE RESTRICT`. |
| **Frontend suy trạng thái vé đúng thiết kế** | 7/7 ca ở mục 2.3. `Expired` là trạng thái **suy ra**, không ghi vào cột `Status` — khớp ba giá trị của `Entities/TicketStatus.cs`. |
| **QR trên màn hình và QR trong PDF cùng một nguồn** | `TicketPdfService.cs:32` ghi *"Mã QR in từ `model.Code` đã lưu — KHÔNG sinh lại"*, và `:114-115` chỉ vẽ từ `model.Code`. Không có nguy cơ vé giấy và vé trên màn hình mang hai mã khác nhau. |
| **Vé đã dùng/hết hạn bị chặn ở tầng giao diện** | `MyTicketsPage.tsx:233-242` chỉ hiện nút "Xem mã QR" khi `isTicketUsable`; `TicketQrPage.tsx:243-254` đổi nút "Tăng sáng" thành `Alert` cảnh báo. `TicketStatusTag` có tooltip nói rõ lý do từng trạng thái. |
| **Phân biệt được "endpoint không tồn tại" với "endpoint chặn quyền"** | 3 dòng đối chứng dương A2–A4 trả 401 kèm thân JSON; B/C trả 404 không `Content-Type`, thân rỗng (mục 2.1). |
| **Rác debug và bí mật** | 0 dòng `console.log`/`debugger`/`TODO`/`FIXME` trong `frontend/src` + `backend/SmartBus.Api`; `git ls-files` không có `.env` / `appsettings.Development.json` / `*.pfx` / `*.pem` / `id_rsa`. |

---

## 5. Việc chuyển cho ai

| Người | Việc | Vì sao là của họ |
|---|---|---|
| **Trần Trung Hiếu** (Chủ API) | Dựng `GET /tickets/me` + `GET /tickets/{id}` (I6); chốt `tripId` của endpoint soát vé rồi sửa `api-contract.md` trước (I8); nối `TicketQrService` + `TicketPdfService` + `TicketEmailService` vào service phát hành vé (I3, I10) | Mọi dòng *"API …"* của story 4 thuộc Hiếu; `docs/29 §1` và `docs/31:142` đã đề nghị chuyển dòng 43 cho Hiếu |
| **Nguyễn Đình Băng** + **Dương Thị Hạnh** | Lật cờ `USE_MOCK_DATA` sau khi I6 xong và **viết nhánh `api` thật** (I1); sửa comment đang khai sai về backend; nối màn chờ thanh toán tới màn vé (I4); sửa nhãn menu avatar (I5) | `ticketApi.ts` là bản nháp mock của Băng/Hạnh; `App.tsx` §E1 của Băng |
| **Vàng Thị Dăm** (Chủ CSDL) | Sửa ví dụ định dạng mã ở `docs/29 §3` từ `TV-01-…` sang `SBT1:…` (I7); cập nhật cảnh báo lỗi thời ở `docs/29 §1` (I9) | `docs/29` là tài liệu của Dăm |
| **Phùng Duy Hoàng** (Scrum Master) | Xoá `package-lock.json` khỏi gốc repo **và** thêm vào `.gitignore` — lần thứ chín (I11) | `.gitignore` là file của Hoàng |
| **Cả nhóm** | Chốt việc tạo môi trường online — **báo cáo thứ tám** nhắc lại, chưa ai nhận | Không có môi trường online thì mọi báo cáo dạng này mãi chỉ chạy được nửa |

---

## 6. Còn lại — chưa kiểm được

1. **Không request nào đi qua mạng thật.** Toàn bộ số liệu mục 2.1 đo trong tiến trình test. Chưa đo
   được hành vi trên PostgreSQL Supabase dùng chung (partial index có thật, kiểu `timestamptz` có thật).
2. **Chưa quét được mã QR bằng máy quét.** Chỉ kiểm được **chuỗi ↔ bộ kiểm**. Chưa kiểm được ảnh QR do
   `qrcode.react` (màn hình) và ZXing (PDF) vẽ ra có đọc được bằng thiết bị thật hay không — cần môi
   trường online + điện thoại.
3. **Chưa kiểm được luồng gửi email vé thật.** Cần SMTP; `EmailOptions` không có mặc định trong code
   (luật 2). Mới kiểm được bằng đồ giả ở tầng test có sẵn.
4. **Chưa kiểm được `GET /tickets/me` sau khi Hiếu dựng.** Khi có, phải kiểm riêng: tên trạm lên/xuống
   (join `Stops`), `departureTime` (join `Trips`), `seatNumber` (join `Seats`), và việc thiếu `tripId`
   trong hình dạng response có ảnh hưởng endpoint soát vé hay không.
5. **Chưa kiểm được cờ `ShouldIssueTickets` có được dùng đúng không** — chưa nơi nào đọc nó.
6. **Chưa kiểm được đối soát với cổng thanh toán** (IPN thật) — thuộc luồng thanh toán, đã ghi ở `H…`.
7. **Chưa kiểm được vé tháng có QR không** — theo A8.2 vé tháng **không kèm ghế** và không có mã QR;
   phần này không thuộc luồng đang kiểm, chỉ ghi để khỏi nhầm.

---

## 7. Phụ lục — lệnh chạy lại

```bash
# 1. Mốc chuẩn
"C:/Users/ASUS/.dotnet/dotnet.exe" build backend/SmartBus.sln -c Release     # 0W / 0E
"C:/Users/ASUS/.dotnet/dotnet.exe" test  backend/SmartBus.Tests/SmartBus.Tests.csproj -c Release
npm --prefix frontend run build
npm --prefix frontend run lint

# 2. Số liệu mục 2.1 và 2.2 — harness TẠM, đã XOÁ trước khi commit (không có file test nào được thêm)
#    Dựng lại: một class test trong backend/SmartBus.Tests/ dùng TestAppFactory với
#    WithWebHostBuilder(b => b.UseSetting("TicketQr:SigningKey", "<khoá test ≥32 ký tự>")),
#    gửi 12 request ở bảng 2.1 rồi ghi mã HTTP + Content-Type + thân ra file tạm;
#    và new TicketQrService(Options.Create(new TicketQrOptions { SigningKey = "…" }))
#    để chạy TryVerify trên 6 mã ở bảng 2.2.
"C:/Users/ASUS/.dotnet/dotnet.exe" test backend/SmartBus.Tests/SmartBus.Tests.csproj \
    --filter "FullyQualifiedName~<tên class tạm>"

# 3. Số liệu mục 2.3 — cũng là harness TẠM, đã XOÁ. Node 24 nạp thẳng .ts (type stripping):
#    import { getTicketStatus, isTicketUsable } from './src/components/ticketStatus.ts'
#    rồi gọi với mốc `now` cố định. Chạy từ thư mục frontend/ để `dayjs` phân giải được.
node <script tạm>.mjs

# 4. Kiểm rác và bí mật
grep -rn "console\.log\|debugger\|TODO\|FIXME" frontend/src backend/SmartBus.Api
git ls-files | grep -iE "\.env$|appsettings\.Development\.json$|\.pfx$|\.pem$|id_rsa"
```

---

> **Ghi chú vai trò (quy ước 2.4 — Người kiểm thử).** Báo cáo này là **bản báo cáo**, không kèm file
> test: hai harness đo ở mục 2.1–2.3 đều là file **tạm đã xoá trước khi commit**, nên `git status` sau
> commit chỉ có đúng một file docs. Tôi **không sửa code nghiệp vụ, không sửa frontend, không viết
> controller** cho bất kỳ lỗi nào ở mục 3 — theo luật cứng của vai trò, test đỏ thì báo người viết code
> sửa, không tự sửa cho xanh. Cả 11 lỗi đều kèm người xử lý ở mục 3 và 5.
