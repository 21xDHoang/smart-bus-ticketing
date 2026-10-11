# Báo cáo kiểm thử chéo — luồng voucher (quản lý · nhập mã · áp vào đơn · thống kê)

| | |
|---|---|
| **Người thực hiện** | Giàng A Vàng (Kiểm thử) |
| **Task** | Sprint 3, story 18 — *Kiểm thử chéo luồng voucher trên môi trường online* (dòng r60) |
| **Ngày** | 11/10/2026 |
| **Mốc so sánh** | `main` @ `5f03357` |
| **Trạng thái** | ⚠️ Chỉ hoàn thành phần kiểm thử **không cần môi trường online** — xem mục 1 |

> Mã lỗi trong báo cáo này (**K1**…) là cục bộ cho báo cáo này, **khác** bộ `L1`–`L20` của
> `docs/bao-cao-kiem-thu-sprint-1.md`, `C` của `-cheo-lich-trinh-sinh-chuyen`, `P1`–`P9` của
> `-cheo-phan-cong`, `N` của `-cheo-tra-cuu`, `V1`–`V7` của `-cheo-ve-thang`, `F` của
> `-cheo-phan-anh`, và ba bộ `G` · `H` · `I` đang nằm ở nhánh **chưa merge**. Mười bộ không liên
> tục và không thay thế nhau.
>
> **K8 lặp lại ở lần thứ mười** (`L20 → P9 → N9 → V7 → F9 → G7 → T3 → H9 → I11` → **K8**). Ghi lại
> là bằng chứng việc đó vẫn chưa được xử lý, không phải lỗi mới.
>
> **K1 và K2 là lỗi đã báo ở nhánh chưa merge `feature/6-kiem-thu-cheo-thanh-toan-online`** (bộ `H`,
> hai mục H6 và H4). Báo cáo này không nhận là phát hiện mới; phần thêm được là **bằng chứng đo trên
> pipeline HTTP thật** và hệ quả **riêng của luồng voucher** — xem ghi chú trong từng mục.

---

## 1. Phạm vi — phần KHÔNG làm được và vì sao

Task yêu cầu kiểm thử **trên môi trường online**. Đây là **báo cáo thứ chín** liên tiếp trong chuỗi
này, và cả tám báo cáo trước đều tắc ở cùng một chỗ: nhóm **chưa có môi trường online nào**.

| Kiểm tra | Báo cáo `-cheo-ve-thang` | Nay |
|---|---|---|
| CSDL dùng chung cho cả nhóm | ✅ Supabase (`docs/25-huong-dan-csdl-chung.md`) | ✅ vẫn có |
| API chạy ngoài máy cá nhân | ❌ không | ❌ **vẫn không** |
| `render.yaml` / `fly.toml` / `Procfile` / `vercel.json` / `docker-compose.yml` / `netlify.toml` | ❌ không | ❌ **vẫn không** (`ls` cả sáu tên: *No such file*) |
| Workflow deploy | ❌ chỉ `ci.yml` | ❌ **vẫn chỉ `ci.yml`** — chỉ `build` + `test`, **không có bước deploy**; và chỉ chạy khi push/PR vào `main` |
| URL online ở đâu đó trong repo | ❌ không | ❌ **không** — grep `onrender\|vercel.app\|netlify.app\|fly.dev\|azurewebsites.net` toàn repo (trừ `node_modules`): **0 kết quả** |
| `appsettings.Production.json` | ❌ không | ❌ **vẫn không** |
| `backend/SmartBus.Api/appsettings.Development.json` trên máy này | ❌ không tồn tại | ❌ **vẫn không** — chuỗi kết nối Supabase nằm ngoài repo (cố ý, repo public) |
| Dockerfile backend | ✅ có | ✅ vẫn có — nhưng **chưa từng được deploy** |
| Task deploy trong backlog | ❌ không có dòng nào | ❌ **vẫn không** |

**Lần này có thêm hai tầng chặn độc lập với tầng trên, và chúng thuộc chính luồng đang kiểm.**

**(a) Nửa backend của luồng voucher không tồn tại.** `docs/api-contract.md:3593-3602` liệt kê **sáu**
đường dẫn trên bề mặt `api/vouchers`. Đo trên pipeline HTTP thật (mục 2.1): **hai** chạy được
(`validate`, `statistics`), **bốn** của dòng 51 trả **404 không thân, không `Content-Type`** — tức là
*đường dẫn không tồn tại*, không phải "tài nguyên không tìm thấy". Chỉ có hai file controller voucher
trong `backend/SmartBus.Api/Controllers/`. Hợp đồng đã ghi đúng trạng thái này: cả bốn dòng đều
*"Chưa làm — dòng 51 (Hiếu)"*.

**(b) Bảng CSDL của luồng chưa có trên CSDL chung.** `docs/api-contract.md:3537` ghi nguyên văn:
*"⏳ **Còn nợ đúng một bước: `dotnet ef database update`** trên CSDL chung (`docs/25` §4). Bảng chỉ
…"*; `docs/27-huong-dan-migrate-vouchers.md` xác nhận migration `Sprint3_Payments_Vouchers_Tickets`
đã sinh và đã merge, *"còn lại: PR → merge `main` → rồi mới áp lên CSDL chung"*. Kể cả có môi trường
online, `POST /vouchers/validate` sẽ **500** vì bảng chưa được tạo.

**Hệ quả:** không chạy được một request HTTP thật nào *qua mạng*. Phần đã làm thay thế — như tám báo
cáo trước — là **kiểm thử chéo có bằng chứng chạy lại được**:

1. **Đo trên pipeline HTTP thật** bằng `TestAppFactory` (`SmartBus.Tests/JwtAuthTests.cs:291` — app
   thật + EF InMemory + bốn job nền noop), cho cả ba tình huống: có token hành khách, có token
   Admin, không token. Số liệu ở mục 2.1.
2. **Đối chiếu ba phía** với từng mắt xích của chuỗi: mã nguồn frontend ↔ `docs/api-contract.md` ↔
   mã nguồn backend.

Chuỗi mà hợp đồng và chính các comment trong mã nguồn mô tả:

```
quản lý tạo voucher (r51 · r55) → khách gõ mã ở màn thanh toán (r56)
   → POST /vouchers/validate trả lời dùng được hay không (r52 · r53, KHÔNG tiêu thụ gì)
   → hiển thị số tiền giảm + giá sau giảm (r57) → khách bấm xác nhận
   → POST /payments trừ vào số phải trả → tiền về thì ghi VoucherUsage, tăng UsedCount
   → thống kê hiệu quả (r54)
```

---

## 2. Mốc chuẩn trên `main` @ `5f03357`

| Hạng mục | Lệnh | Kết quả |
|---|---|---|
| Test backend | `dotnet test backend/SmartBus.sln -c Release` | ✅ **1121/1121 pass**, 0 fail, 0 skip, 2 m 11 s — **không đổi** so với `main` (tôi không thêm test nào, đúng lệ quán loạt báo cáo này) |
| Build backend | `dotnet build backend/SmartBus.sln -c Release --no-incremental` | ✅ **0 Warning(s), 0 Error(s)** |
| Build frontend | `npm --prefix frontend run build` | ✅ exit 0 — `index.js` 1.770 kB (gzip 551 kB), chỉ còn cảnh báo chunk > 500 kB |
| Lint frontend | `npm --prefix frontend run lint` | ⚠️ **1 cảnh báo cũ** — `StopManagePage.tsx:33` (`react/set-state-in-effect`), nằm ngoài luồng voucher; **không** cảnh báo nào trong file voucher |
| Rác debug | grep `console.log\|debugger;\|TODO:\|FIXME` trên `frontend/src` + `backend/SmartBus.Api` | ✅ **0 kết quả** |
| Bí mật | `git ls-files` lọc `appsettings.Development.json` / `.env` | ✅ chỉ có `appsettings.Development.json.example` — không có file thật nào được theo dõi |
| `package-lock.json` ở gốc repo | `git status --short` | ⚠️ **`?? package-lock.json`** — vẫn untracked, xem K8 |

### 2.1 Đo trên pipeline HTTP thật — bảng số liệu

Dựng app thật qua `TestAppFactory`, seed một tuyến + một chuyến + một voucher `SUMMER10`
(`Percent`, 10%, `MinOrderValue = 0`, `Quantity = 500`, `UsedCount = 128`, `Active`), một người
dùng vai trò `Passenger` và một người vai trò `Admin`. Mỗi dòng là **một request thật**.

**Phép so sánh quyết định:** đường dẫn **có thật** mà thiếu quyền/thiếu token thì trả **401/403
kèm thân JSON** (`{ "message": … }`). Đường dẫn **không tồn tại** trả **404 không `Content-Type`,
thân rỗng** — vì không có endpoint nào sinh ra phản hồi.

| # | Request | Token | Kết quả đo | Ý nghĩa |
|---|---|---|---|---|
| A1 | `GET /api/trips/{id}/seats` | — | **404 kèm thân** *"Không tìm thấy chuyến xe"* | đối chứng: **đường dẫn có thật** (mẫu "404 kèm thân") |
| A2 | `GET /api/routes` | hành khách | **403 kèm thân** *"Bạn không có quyền truy cập tính năng này."* | đối chứng: chính sách `ManagerOrAbove` chặn đúng |
| A3 | `POST /api/vouchers/validate` | — | **401 kèm thân** *"Chưa đăng nhập. …"* | endpoint có thật, `[Authorize]` chặn đúng |
| A4 | `POST /api/vouchers/validate` (`SUMMER10`, đơn 150.000) | hành khách | **200** `valid:true, reasonCode:null, discountType:"Percent", discountAmount:15000, finalAmount:135000, maxDiscount:null` | **cả pipeline thật chạy đúng** |
| A5 | `POST /api/vouchers/validate` (`KHONG-CO`) | hành khách | **200** `valid:false, reasonCode:"NotFound", message:"Mã voucher không tồn tại", discountAmount:0, finalAmount:150000` | mã bị từ chối vẫn **200**, đúng 📌 của hợp đồng |
| B1 | `GET /api/vouchers?page=1&pageSize=10` | Admin | **404 không `Content-Type`, thân rỗng** | **không tồn tại** |
| B2 | `GET /api/vouchers?page=1&pageSize=10` | hành khách | **404 không thân** | **không tồn tại** (không phải 403) |
| B3 | `POST /api/vouchers` | Admin | **404 không thân** | **không tồn tại** |
| B4 | `GET /api/vouchers/{guid}` | Admin | **404 không thân** | **không tồn tại** |
| B5 | `PUT /api/vouchers/{guid}` | Admin | **404 không thân** | **không tồn tại** |
| B6 | `PATCH /api/vouchers/{guid}/status` | Admin | **404 không thân** | **không tồn tại** |
| B7 | `GET /api/vouchers/available` | hành khách | **404 không thân** | **không tồn tại** — màn r58 không có gì để gọi |
| C1 | `GET /api/vouchers/statistics` | Admin | **200** `totalVouchers:1, totalIssued:500, totalUsed:0, totalOrderAmount:0, totalDiscountAmount:0, neverUsedVouchers:1, items:[…]` | chạy đúng |
| C2 | `GET /api/vouchers/statistics` | hành khách | **403 kèm thân** | chính sách `ManagerOrAbove` chặn đúng |
| C3 | `GET /api/vouchers/statistics` | — | **401 kèm thân** | chặn trước cả phân quyền, đúng thứ tự |

Bảy dòng **B** là bằng chứng cơ học cho tầng chặn (a): nếu bốn endpoint của dòng 51 có thật thì
request của **Admin** đã không thể trả 404 thân rỗng — token hợp lệ cộng đường dẫn không tồn tại là
điều kiện duy nhất sinh ra đúng cặp "404 + không `Content-Type`".

---

## 3. Danh sách lỗi

Mức độ: **Cao** = sai chức năng hoặc gây hiểu nhầm nghiêm trọng · **TB** = ảnh hưởng trải nghiệm
hoặc rủi ro · **Thấp** = tài liệu, vệ sinh mã.

### 3.1 Thuộc luồng voucher (việc của task này)

| # | Lỗi | Mức | Người xử lý |
|---|---|---|---|
| K1 | Nhánh API thật của voucher **chưa từng chạy**: `USE_MOCK_DATA = true` chặn cả `validate` — mắt xích **duy nhất** của luồng đã xong ở backend | 🔴 Cao | Dương Thị Hạnh |
| K2 | Voucher đã xác nhận **không được gửi** khi thanh toán: màn hiện *"Giá sau giảm 135.000đ"*, lệnh thanh toán gửi `total: 150000` | 🔴 Cao | Dương Thị Hạnh + Nguyễn Đình Băng |
| K3 | `WrongRoute` — **1 trong 7 điều kiện chặn không thể xảy ra** từ giao diện, ở **cả hai** phía | 🟠 TB | Dương Thị Hạnh + Nguyễn Đình Băng |
| K4 | **Bốn comment trỏ "dòng 58"** cho việc của **r57**; còn màn hình mà r58 giao thì **không tồn tại** | 🟠 TB | Nguyễn Đình Băng + Dương Thị Hạnh |
| K5 | Nhãn `BelowMinOrder` trên màn hình **mất con số tối thiểu** — khách biết "chưa đủ" mà không biết cần thêm bao nhiêu | 🟠 TB | Nguyễn Đình Băng + Dương Thị Hạnh |
| K6 | **Hai comment nói sai** rằng bảng `Vouchers` chưa có — bảng đã migrate và đã merge | 🟡 Thấp | Dương Thị Hạnh + Nguyễn Đình Băng |
| K7 | Hợp đồng: ghi chú 🔴 **lý luận về `GET /vouchers/{id}`** nhưng bảng Endpoints **không có dòng nào** cho nó | 🟡 Thấp | Trần Trung Hiếu |

### 3.2 Mắt xích lân cận (ghi kèm, chuyển đúng người)

| # | Lỗi | Mức | Người xử lý |
|---|---|---|---|
| K8 | `package-lock.json` lạc ở gốc repo — **lần thứ mười** | 🟡 Thấp | Phùng Duy Hoàng |
| K9 | `npm run lint` **không nằm trong CI** — cảnh báo oxlint không chặn merge (ngoài luồng voucher) | 🟡 Thấp | Phùng Duy Hoàng |

> **Về việc gán người xử lý:** các file frontend ở đây không ghi tên người sở hữu trong đầu file.
> Tên được suy từ bảng phân công Sprint 3 (`Product_Backlog_Smart_Bus.xlsx`): r51 API CRUD voucher =
> Trần Trung Hiếu, r54 API thống kê = Nguyễn Duy Kiên, r55 màn quản lý voucher = Dương Thị Hạnh,
> r56 component nhập mã = Hoàng Văn Thịnh, r57 hiển thị số tiền được giảm = Nguyễn Đình Băng, r58
> màn danh sách voucher khả dụng = Nguyễn Đình Băng. `App.tsx` và `frontend/package.json` thuộc
> Nguyễn Đình Băng; `.gitignore` và `.github/` thuộc Phùng Duy Hoàng (mục E1). Riêng
> `PaymentMethodPage.tsx` tự khai người làm ngay trong đầu file (`:22` — *"task của Dương Thị Hạnh"*).
> Cần đối chiếu lại nếu phân công đã đổi.

**Mức độ trùng lặp, nói thẳng:** trong bảy mục của 3.1, **K1 và K2 đã được báo** ở nhánh chưa merge
`feature/6-kiem-thu-cheo-thanh-toan-online` (H6 và H4). **K6 cùng lớp** với V5 của `-cheo-ve-thang`
và I1 của `-cheo-mua-ve-nhan-qr` (comment nói sai tình trạng backend). Phần **mới** của báo cáo này
là **K3, K4, K5, K7** — tất cả đều là chuyện riêng của luồng voucher.

---

### K1 — Nhánh API thật của voucher chưa từng chạy 🔴 Cao

> Lặp lại H6 của `feature/6-kiem-thu-cheo-thanh-toan-online`. Hệ quả riêng của luồng voucher ở dưới.

**Đường đi của lỗi:**

1. `frontend/src/api/voucherApi.ts:199` — `const USE_MOCK_DATA = true;`
2. `voucherApi.ts:500` — `const voucherApi: VoucherApi = USE_MOCK_DATA ? mock : api;`
3. `voucherApi.ts:206-225` — nhánh thật `api`; `:205` tự khai *"backend dòng 51 nên tạm throw"* —
   `list`/`create`/`update`/`updateStatus` **đều `throw`**, **nhưng `validate` thì gọi thật**:
   `api.validate` → `axiosClient.post('/vouchers/validate', …)`
4. Cờ là **hằng ở tầng module**, dùng chung cho cả bốn hàm CRUD **lẫn** `validate` → bật cờ để CRUD
   chạy được dữ liệu giả thì `validate` **cũng** bị chặn theo.

**Vì sao là lỗi, chứ không phải "chờ backend":** `voucherApi.ts:9` tự ghi *"validate: … hợp đồng đã
chốt"* và `:8` tách bạch hai nhánh — nghĩa là **tác giả đã biết** một nửa đã xong. Nhưng một cờ duy
nhất không tách được hai nửa. Và mục 2.1 đo được `validate` **chạy đúng** trên pipeline thật (A3 ·
A4 · A5) — đây là mắt xích **duy nhất** của toàn luồng đã xong ở backend, và giao diện **không bao
giờ** gọi nó.

**Hệ quả riêng của luồng voucher (phần mới):**

- Con số *"Số tiền được giảm"* mà khách nhìn thấy do **`computeMockDiscount` của chính client** tính,
  không phải backend. Nên không có gì bảo đảm nó bằng con số mà `POST /payments` sẽ dùng.
- `mock.validate` (`:421-497`) **không thể** trả `WrongRoute` — xem K3.

**Hướng sửa:** tách cờ làm hai, hoặc bỏ hẳn cờ và để bốn hàm CRUD `throw` cho tới khi r51 xong —
`voucherApi.ts:15` đã tự đề xuất đúng điều này (*"Còn lại (GET/POST/PUT/PATCH /vouchers) là dòng 51,
chưa chốt — vẫn để cờ USE_MOCK_DATA"*). Không cần chờ backend để nối `validate`.

---

### K2 — Voucher đã xác nhận không được gửi khi thanh toán 🔴 Cao

> Lặp lại H4 của `feature/6-kiem-thu-cheo-thanh-toan-online`. Ghi lại vì đây là lỗi mà chính hợp
> đồng gọi là nặng nhất của luồng.

**Đường đi của lỗi:**

1. `PaymentMethodPage.tsx:129` — `voucherApi.validate({ code, tripId, orderAmount: totalPrice })`
   → `setValidation(result)`, hiện *"Giá sau giảm 135.000đ"*.
2. `PaymentMethodPage.tsx:100-105` — `handlePay` gọi
   `paymentApi.pay({ methodCode, tripId, seatNumbers, total: hasTotal ? totalPrice : 0 })`.
3. `frontend/src/api/paymentApi.ts:98-108` — `interface PaymentRequest` **không có** `voucherCode`.
4. Giá trị trả về của `pay()` bị **vứt bỏ**; `:106` hiện `message.success('Thanh toán thành công…')`
   rồi `:107` `navigate('/my-tickets')`.

**Vì sao là lỗi:** `totalPrice` (`:52` — `Number(totalParam)`) là con số **do màn trước truyền qua
query string**, **không trừ** phần giảm giá. Nên màn hình hiển thị *"Giá sau giảm 135.000đ"* trong
khi lệnh thanh toán mang **150.000** — khách tưởng đã được giảm, tiền thu là tiền đủ.
`docs/api-contract.md` mục *Thanh toán* gọi đúng tình huống này là *"lỗi nặng nhất của luồng này"*
(*"âm thầm thu đủ tiền trong khi khách tưởng đã được giảm"*). Chính màn hình tự nhận ở `:117-118`:
*"không gửi kèm khi thanh toán — việc chốt voucher vào giao dịch là phần của luồng thanh toán"*.

**Ghi chú riêng của luồng voucher:** hợp đồng chốt **hai** chỗ khác nhau cho cùng một sự kiện — ở
`POST /vouchers/validate` mã không hợp lệ là **200** kèm `valid:false`, còn ở `POST /payments` mã
không hợp lệ là **409**. Nghĩa là không thể "vá" bằng cách để màn hình tự gửi lại `validate` rồi trừ
tiền ở client; việc chốt voucher **phải** nằm trong `POST /payments`, nếu không thì hai lớp chống
tiêu thụ quá `quantity` mà hợp đồng mô tả sẽ hụt mất lớp thứ hai.

**Hướng sửa:** thêm `voucherCode?: string | null` vào `PaymentRequest`, truyền `validation?.code` từ
`handlePay`, và để backend trả số phải trả thật.

---

### K3 — `WrongRoute` không thể xảy ra từ giao diện 🟠 TB

**Bảy** điều kiện chặn được hợp đồng chốt (`docs/api-contract.md:3674-3685`), nhưng **điều kiện thứ
sáu** không có đường nào để khách gặp.

**Phía frontend:**

- `voucherApi.ts:105-111` — union `VoucherReasonCode` **có** `WrongRoute` (`:110`).
- `voucherApi.ts:178-183` — `VOUCHER_REASON_META` **có** nhãn cho nó (`:182`).
- `voucherApi.ts:25-63` — `interface Voucher` **không có** `routeId`; `:40` và `:71` cũng vậy.
- `voucherApi.ts:470` — tự khai: *"WrongRoute bỏ qua ở nhánh giả: bản nháp FE chưa có trường routeId
  (việc của màn quản lý)."*
- `frontend/src/components/VoucherFormModal.tsx` — grep `routeId\|Route\|routes\|Form.Item name`:
  **không kết quả nào** liên quan tuyến → màn quản lý **không nhập được** điều kiện tuyến.

**Phía backend:** luật đúng và đã có test. `VoucherValidationService.cs:141` trả
`VoucherReasonCodes.WrongRoute` khi `voucher.RouteId != null && voucher.RouteId != trip.RouteId`.
`Entities/Voucher.cs` ghi chú đúng khoảng trống này ngay trên thuộc tính `RouteId`.

**Đây là lỗi đã biết và đã được gán việc, không phải phát hiện mới.**
`docs/api-contract.md:3561` ghi nguyên văn: *"Hình dạng này khớp bản nháp FE `frontend/src/api/
voucherApi.ts` (Hạnh) **trừ `routeId`** — bản nháp đó chưa có trường tuyến, nên màn quản lý voucher
hiện không nhập được điều kiện tuyến. **FE cần bổ sung; đây là việc của màn quản lý (dòng 51/56–58),
không phải của endpoint kiểm tra.**"* Nghĩa là hợp đồng **đã ghi đúng, đã gán đúng người** — và tới
nay **chưa ai làm**. Ghi lại ở đây là bằng chứng về khoảng trống còn nguyên, không phải để tính là
lỗi của người đã ghi đúng.

**Hướng sửa:** thêm `routeId?: string | null` vào `Voucher`, `VoucherPayload` và một ô chọn tuyến
(hoặc "mọi tuyến") vào `VoucherFormModal`; gỡ nhánh bỏ qua ở `:470`.

---

### K4 — Bốn comment trỏ "dòng 58" cho việc của r57, và màn của r58 không tồn tại 🟠 TB

Bảng phân công Sprint 3 (`Product_Backlog_Smart_Bus.xlsx`) ghi:

| Dòng | Task | Assign |
|---|---|---|
| **r57** | *Hiển thị số tiền được giảm và giá sau giảm* | Nguyễn Đình Băng |
| **r58** | *Màn hình danh sách voucher đang khả dụng cho hành khách* | Nguyễn Đình Băng |

Cả hai đều của cùng một người — nên **"dòng 58" trong bốn comment là sai một dòng**, việc được nhắc
tới thực ra là **r57**:

1. `voucherApi.ts:9` — *"validate: phục vụ màn thanh toán hiển thị số tiền được giảm (dòng 58 — Băng)"*
2. `voucherApi.ts:99` — *"(dòng 58 — Băng). Đây là XEM TRƯỚC: …"*
3. `components/VoucherDiscountSummary.tsx:9` — *"…(US 18 "Quản lý Voucher", Sprint 3 — task dòng 58
   của Nguyễn Đình Băng)"*
4. `pages/PaymentMethodPage.tsx:60` — *"Phần áp dụng voucher (dòng 58 — hiển thị số tiền được giảm và
   giá sau giảm)."*

**Vì sao đây là lỗi chứ không phải chuyện nhỏ:** con số bị gán sai (**r58**) là màn hình **không hề
tồn tại**. Kiểm ba đường, cả ba đều không:

- **Không có file:** `ls frontend/src/pages/ | grep -i voucher` → chỉ `VoucherManagePage.tsx`.
- **Không có route:** grep `voucher` trong `App.tsx` → đúng **một** route, `path="/vouchers"` (`:372`)
  — màn quản trị.
- **Không có endpoint:** mục 2.1 dòng **B7** — `GET /api/vouchers/available` → **404 không thân**.

Nghĩa là **không comment nào trong mã nguồn nhắc tới r58**, và cũng không có dấu vết nào khác của nó.
Số dòng bị gán sai đang **che mất một màn hình chưa ai làm**. Đây là kiểu nhầm nguy hiểm hơn một lỗi
chính tả: đọc lướt qua thì thấy "dòng 58 đã có người làm".

**Hướng sửa:** đổi bốn chỗ thành **r57**, và ghi rõ trong backlog rằng r58 chưa có gì (hoặc làm nó).
Nếu muốn dựng r58 thì phải có endpoint danh sách khả dụng — chưa có dòng nào trong bảng Endpoints.

---

### K5 — Nhãn `BelowMinOrder` trên màn hình mất con số tối thiểu 🟠 TB

**Đường đi của lỗi:**

1. `components/VoucherDiscountSummary.tsx:54` —
   `VOUCHER_REASON_META[validation.reasonCode ?? 'NotFound']?.label ?? validation.message ?? 'Mã giảm giá không dùng được.'`
   → **nhãn theo `reasonCode` thắng trước**, `validation.message` chỉ là phương án dự phòng.
2. `voucherApi.ts:183` — nhãn đó là: *"Đơn hàng chưa đạt giá trị tối thiểu để dùng mã này."*
3. `VoucherValidationService.cs:199` — câu backend gửi là *"Đơn hàng chưa đạt giá trị tối thiểu
   **{minOrderValue}**đ"*, ví dụ *"…tối thiểu 100.000đ"*.
4. `voucherApi.ts:474` — **chính mock của FE cũng dựng câu có con số**
   (`` `Đơn hàng chưa đạt giá trị tối thiểu ${voucher.minOrderValue.toLocaleString('vi-VN')}đ` ``).

**Vì sao là lỗi:** hợp đồng giải thích lý do xếp `BelowMinOrder` **cuối cùng** trong bảy bậc là vì
nó là *"gợi ý khách làm được gì đó"* (`docs/api-contract.md:3694-3700`). Một lời nhắc "chưa đủ" mà
**không kèm số** không gợi ý được gì — khách không biết cần thêm bao nhiêu. Và nghịch lý nằm trong
cùng một file: `voucherApi.ts` (**:474**) đã có sẵn câu đúng, nhưng câu đó **không bao giờ tới được
màn hình** vì `VoucherDiscountSummary` ưu tiên nhãn tĩnh ở `:183`.

**Ghi chú:** việc FE dịch theo `reasonCode` **là đúng hợp đồng** (`:3674`: *"FE dịch theo
`reasonCode`, KHÔNG so chuỗi `message`"*). Lỗi không nằm ở cơ chế, mà ở chỗ **chuỗi nhãn tĩnh không
mang được tham số** `{minOrderValue}`.

**Hướng sửa:** cho `VOUCHER_REASON_META` một hàm dựng nhãn cho riêng `BelowMinOrder` (nhận
`minOrderValue`), hoặc để component ưu tiên `validation.message` khi `reasonCode === 'BelowMinOrder'`.
Rẻ nhất: dùng con số mà component đã có sẵn — prop `orderAmount`.

---

### K6 — Hai comment nói sai rằng bảng `Vouchers` chưa có 🟡 Thấp

1. `pages/VoucherManagePage.tsx:34` — *"Backend chưa có bảng Vouchers nên màn hình chạy dữ liệu giả
   (voucherApi.ts)."*
2. `App.tsx:366-368` — *"…Vouchers chưa có nên màn hình đang chạy dữ liệu giả (voucherApi.ts)."*

**Sai:** bảng `Vouchers` **đã có** entity (`Entities/Voucher.cs`), đã có migration
(`Sprint3_Payments_Vouchers_Tickets`, PR #144 — Vàng Thị Dăm) và **đã merge `main`**. Chính
`Entities/Voucher.cs` ghi rõ trong doc-comment: *"✅ Bảng `Vouchers` ĐÃ có migration và đã merge …
Còn nợ đúng một bước: `dotnet ef database update` trên CSDL chung"*. Vế **đúng** của hai comment là
"chưa có **endpoint** CRUD" (mục 2.1 dòng B1–B6) — nhưng comment lại đi khẳng định về **bảng**.

Cùng lớp với V5 của `-cheo-ve-thang` và I1 của `-cheo-mua-ve-nhan-qr`. Và đây không chỉ là lỗi chữ:
**chính comment sai là thứ đang biện minh cho cờ `USE_MOCK_DATA` còn bật** — dẫn thẳng tới K1.
`App.tsx` thuộc Nguyễn Đình Băng (mục E1), `VoucherManagePage.tsx` thuộc r55 (Dương Thị Hạnh).

---

### K7 — Hợp đồng lý luận về `GET /vouchers/{id}` nhưng bảng Endpoints không có nó 🟡 Thấp

`docs/api-contract.md:3598-3602` liệt kê **bốn** đường dẫn cho dòng 51: `GET /vouchers`,
`POST /vouchers`, `PUT /vouchers/{id}`, `PATCH /vouchers/{id}/status`. **Không có `GET /vouchers/{id}`.**

Nhưng ngay bên dưới, ghi chú 🔴 ở `:3610` mở đầu bằng: *"**`GET /vouchers/{id}` của dòng 51 PHẢI giữ
ràng buộc `{id:guid}`**"*, rồi lý luận tiếp về cái bẫy mà nó phải tránh (*"nới `:guid` thành `{id}`
trần là `statistics` bị nuốt"*). Một tài liệu đang dành một ghi chú 🔴 để ràng buộc một endpoint mà
bảng của chính nó không có.

Phía FE cũng **không** có `getById` trong object `api` (`voucherApi.ts:206-225` chỉ có `validate`,
`list`, `create`, `update`, `updateStatus`) — tức là FE khớp **bảng**, còn ghi chú 🔴 là chỗ lệch.
Không ai bị chặn vì lỗi này, nhưng lần sau ai đọc ghi chú đó sẽ đi tìm một endpoint không tồn tại.

**Hướng sửa:** hoặc thêm một dòng `GET /vouchers/{id}` vào bảng (nếu nó thuộc dòng 51), hoặc sửa
ghi chú 🔴 thành `PUT /vouchers/{id}` / `PATCH /vouchers/{id}/status` — hai đường dẫn **đang có**
trong bảng và cũng mang tham số `{id}`. Người sửa: Trần Trung Hiếu (chủ `api-contract.md`), và theo
luật 5 thì sửa contract **trước**, rồi mới báo người còn lại.

---

### K8 — `package-lock.json` lạc ở gốc repo (lần thứ mười) 🟡 Thấp

`C:\smart-bus-ticketing\package-lock.json` — 98 byte, 26/09/2026, **gốc repo** (không phải trong
`frontend/`), vẫn `??` trong `git status`, `git check-ignore` không chặn. Không phải file của tôi và
**không** nằm trong commit nào của báo cáo này.

Chuỗi mười lần: `L20` (Sprint 1) → `P9` (phân công) → `N9` (tra cứu) → `V7` (vé tháng) → `F9`
(phản ánh) → `G7` (giữ chỗ) → `T3` → `H9` (thanh toán) → `I11` (mua vé → nhận QR) → **K8**.
Ngoài luồng voucher. Hoặc xoá, hoặc thêm một dòng vào `.gitignore` (thuộc Phùng Duy Hoàng — mục E1).

---

### K9 — `npm run lint` không nằm trong CI 🟡 Thấp *(ghi kèm)*

`.github/workflows/ci.yml` — job `frontend` chạy `npm ci` rồi `npm run build`. `frontend/package.json:8`
là `"build": "tsc -b && vite build"` nên **kiểm tra kiểu có** trong CI; nhưng `"lint": "oxlint"`
(`:9`) **không** được gọi ở đâu. Nghĩa là cảnh báo oxlint (hiện có đúng một cái, `StopManagePage.tsx:33`)
không chặn được merge nào.

Ngoài luồng voucher — ghi kèm vì báo cáo nào cũng phải chạy `npm run lint` bằng tay rồi tự đọc kết
quả. Người xử lý: Phùng Duy Hoàng (`.github/` thuộc mục E1, và CI là phần hạ tầng của Scrum Master).

---

## 4. Đã kiểm và ĐẠT

### `POST /api/vouchers/validate` — đúng hợp đồng, đo trên pipeline thật

| Kiểm | Kỳ vọng (`api-contract.md`) | Kết quả |
|---|---|---|
| Không token | 401 | ✅ **401**, thân *"Chưa đăng nhập. Vui lòng gửi kèm access token trong header Authorization."* |
| Mã dùng được | 200, `valid:true`, `discountAmount`/`finalAmount` đúng | ✅ **200** — đơn 150.000, `Percent` 10% → `discountAmount:15000`, `finalAmount:135000` |
| Mã không tồn tại | 200 (**không** phải 404), `reasonCode:"NotFound"` | ✅ **200**, `valid:false`, `finalAmount` = `orderAmount`, `discountAmount:0` |
| Mã bị từ chối vẫn 200 | *"trả 4xx sẽ buộc FE phân nhánh theo HTTP status cho một luồng hoàn toàn bình thường"* (`:3664-3672`) | ✅ đúng — không nhánh nào trả 4xx |

### `GET /api/vouchers/statistics` — đúng hợp đồng và đúng phân quyền

| Kiểm | Kỳ vọng | Kết quả |
|---|---|---|
| Admin | 200 + hình dạng `StatisticsResponse` | ✅ **200**, đủ `totalVouchers/totalIssued/totalUsed/totalOrderAmount/totalDiscountAmount/neverUsedVouchers/items` |
| Hành khách | 403 (`ManagerOrAbove` — `VoucherStatisticsController.cs:28`) | ✅ **403** |
| Không token | 401 | ✅ **401** thân JSON |

### Ba tầng khai báo bảy mã từ chối — **khớp nhau từng mã một**

Đây là chỗ luồng voucher làm đúng nhất, và đáng ghi lại vì nó hiếm:

| Nguồn | Chỗ khai | Đủ 7 mã? |
|---|---|---|
| Backend | `Dtos/Vouchers/VoucherReasonCodes.cs` (7 hằng, `:16`…`:34`) | ✅ |
| Hợp đồng | `docs/api-contract.md:3674-3685` (bảng 7 dòng) | ✅ |
| Frontend | `voucherApi.ts:105-111` (union `VoucherReasonCode`) + `:178-183` (nhãn tiếng Việt) | ✅ — kể cả `WrongRoute` |

Và **thứ tự bảy bậc** cũng khớp: `VoucherValidationService.cs:114-146` trả `NotFound → Inactive →
NotStarted → Expired → OutOfStock → WrongRoute → BelowMinOrder` — đúng nguyên văn chuỗi ở
`api-contract.md:3691`. Mock của FE (`voucherApi.ts:447-473`) đi **cùng thứ tự đó**, chỉ thiếu bậc
`WrongRoute` (K3) — nghĩa là không có chỗ nào hai phía kể cùng một chuyện theo hai thứ tự khác nhau.

### Một quan sát nhỏ, chưa đủ để thành lỗi

`PaymentMethodPage.tsx:22` ghi màn hình này là *"User Story 6 "Cổng thanh toán", **Sprint 4**"*,
trong khi epic thanh toán (r27–r37) nằm ở sheet **Sprint 3** của `Product_Backlog_Smart_Bus.xlsx`.
Và `:24-25` vẫn khẳng định *"Backend chưa có bảng Payments và chưa có endpoint thanh toán"* — sai cả
hai vế (bảng `Payments` có trong migration đã merge; `api-contract.md` có mục thanh toán đầy đủ).
Cùng lớp với K6 nên không tách thành mục riêng; thuộc luồng thanh toán, đã ghi ở bộ `H`.

---

## 5. Việc chuyển cho ai

| Việc | Người | Vì sao là của họ |
|---|---|---|
| Tách/bỏ cờ `USE_MOCK_DATA` để nối `validate` thật (K1) | **Dương Thị Hạnh** | r55 — chủ `voucherApi.ts` |
| Thêm `routeId` vào `Voucher`/`VoucherPayload` + ô chọn tuyến ở `VoucherFormModal`, gỡ nhánh bỏ qua `:470` (K3) | **Dương Thị Hạnh** | r55 — màn quản lý voucher; hợp đồng `:3561` đã gán đúng việc này |
| Nhãn `BelowMinOrder` mang con số tối thiểu (K5) | **Nguyễn Đình Băng** (component, r57) + **Dương Thị Hạnh** (`VOUCHER_REASON_META`) | r57 là component hiển thị; nhãn nằm trong `voucherApi.ts` |
| Truyền `voucherCode` khi thanh toán, `POST /payments` trả số phải trả thật (K2) | **Dương Thị Hạnh** + **Nguyễn Đình Băng** | `PaymentMethodPage.tsx:22` tự khai task của Hạnh; `paymentApi.ts` thuộc luồng thanh toán của Băng |
| Sửa bốn comment "dòng 58" → **r57**; sửa hai comment ở `App.tsx` (K4, K6) | **Nguyễn Đình Băng** + **Dương Thị Hạnh** | `App.tsx` thuộc Băng (E1); hai file còn lại thuộc Hạnh (r55, r57) |
| Làm **bốn endpoint CRUD** của dòng 51 (điều kiện để màn quản lý thôi chạy dữ liệu giả) | **Trần Trung Hiếu** | r51 — chủ API; hợp đồng `:3598-3601` đã gán tên |
| Ghi rõ r58 (màn danh sách voucher khả dụng) chưa có gì, hoặc bổ sung dòng endpoint cho nó (K4) | **Trần Trung Hiếu** + **nhóm** | chủ `api-contract.md`; bảng Endpoints chưa có đường dẫn nào cho màn này |
| Sửa chỗ lệch ở ghi chú 🔴 `{id:guid}` (K7) — sửa `api-contract.md` **trước**, rồi báo người còn lại | **Trần Trung Hiếu** | luật 5 — chủ `api-contract.md` |
| `dotnet ef database update` cho `Vouchers`/`VoucherUsages` trên CSDL chung | **Vàng Thị Dăm** | `Migrations/*` và CSDL thuộc Dăm (luật 3); `docs/api-contract.md:3537` và `docs/27` đã ghi việc này |
| `package-lock.json` (K8) + đưa `npm run lint` vào CI (K9) | **Phùng Duy Hoàng** | `.gitignore` và `.github/` thuộc E1; CI là hạ tầng của Scrum Master |

---

## 6. Còn lại — chưa kiểm được

| Hạng mục | Vì sao chưa kiểm được |
|---|---|
| Bất kỳ request HTTP thật nào **qua mạng** | Không có môi trường online (mục 1) |
| Bốn endpoint CRUD của dòng 51 (`GET`/`POST /vouchers`, `PUT /vouchers/{id}`, `PATCH …/status`) | **Không tồn tại** — đo được 404 không thân (mục 2.1 dòng B1–B6). r51 (Trần Trung Hiếu) còn "Chưa làm" |
| Màn quản lý voucher chạy trên dữ liệu thật | Không có backend để nối (dòng trên) |
| Bảng `Vouchers` / `VoucherUsages` trên CSDL chung | Chưa `dotnet ef database update` (`api-contract.md:3537`); không kết nối được để xác nhận |
| Màn hình *"danh sách voucher đang khả dụng cho hành khách"* (r58) | **Chưa tồn tại** — không file, không route (mục 3, K4); và `GET /api/vouchers/available` đo được **404** |
| Component *"nhập mã giảm giá ở trang thanh toán"* (r56 — Hoàng Văn Thịnh) | Chưa có file: `frontend/src/components/` chỉ có `VoucherDiscountSummary.tsx` và `VoucherFormModal.tsx`. Ô nhập hiện là **lối tắt tạm** nằm trong `PaymentMethodPage.tsx:61-62` và chính nó tự khai như vậy |
| `GET /vouchers/statistics` gọi từ giao diện | FE **chưa nối** — `voucherApi.ts:12` tự khai *"(chưa nối FE ở file này)"* |
| `WrongRoute` chạy thật từ đầu tới cuối (tạo voucher có tuyến → gõ mã ở chuyến khác) | Cả hai phía đều thiếu đường vào (K3); luật backend đã có test ở tầng service |
| Số phải trả thật sau khi áp voucher | `POST /payments` không tồn tại (`api-contract.md` — sáu endpoint còn "Chờ bảng"/"Chưa làm"), nên không có gì để đối chiếu với `finalAmount` |
| Đa trình duyệt / đa thiết bị | Không có môi trường để mở |
| Ghi `VoucherUsage` + tăng `UsedCount` sau khi tiền về | Thuộc `POST /payments` — chưa có endpoint (dòng trên) |

---

## 7. Phụ lục — lệnh chạy lại mọi bằng chứng trong báo cáo

```bash
# ---- Mốc chuẩn (mục 2) ----
dotnet build backend/SmartBus.sln -c Release --no-incremental   # 0 Warning / 0 Error
dotnet test  backend/SmartBus.sln -c Release                     # 1121/1121
npm --prefix frontend run build                                  # exit 0
npm --prefix frontend run lint                                   # 1 cảnh báo cũ: StopManagePage.tsx:33

# ---- Mục 1: không có môi trường online ----
ls render.yaml fly.toml vercel.json docker-compose.yml netlify.toml   # No such file (×5)
cat .github/workflows/ci.yml | grep -n "deploy" || echo "(không có bước deploy)"
grep -rn -iE "onrender|vercel\.app|netlify\.app|fly\.dev|azurewebsites\.net" . \
  --include=*.md --include=*.yml --include=*.json --include=*.ts | grep -v node_modules   # 0 kết quả

# ---- Mục 2.1: đo trên pipeline HTTP thật ----
# Dựng HARNESS TẠM trong backend/SmartBus.Tests/ (class dùng TestAppFactory ở JwtAuthTests.cs:291),
# seed 1 tuyến + 1 chuyến + voucher SUMMER10 + 1 user Passenger + 1 user Admin,
# gửi 15 request ở bảng 2.1, ghi kết quả ra file, RỒI XOÁ.
#   dotnet test backend/SmartBus.Tests/SmartBus.Tests.csproj -c Release --filter "FullyQualifiedName~ZZDoTam"
#   rm backend/SmartBus.Tests/ZZDoTam*.cs        # ← bắt buộc trước khi commit
# Đọc kết quả: 404 KHÔNG Content-Type + thân rỗng = đường dẫn không tồn tại;
#              404/403/401 CÓ thân JSON      = đường dẫn có thật.

# ---- K1 · K3 — cờ dữ liệu giả và phần CRUD bị chặn theo ----
grep -n "USE_MOCK_DATA" frontend/src/api/voucherApi.ts            # :199 = true, :500 = chọn nhánh
grep -n "routeId" frontend/src/api/voucherApi.ts                   # không có trong interface Voucher
grep -rn "routeId\|routes" frontend/src/components/VoucherFormModal.tsx || echo "(không có ô chọn tuyến)"

# ---- K2 — voucher không được gửi khi thanh toán ----
grep -n "voucherCode" frontend/src/api/paymentApi.ts || echo "(PaymentRequest không có voucherCode)"
grep -n "total:\|totalPrice =" frontend/src/pages/PaymentMethodPage.tsx   # :104 gửi totalPrice, :52 lấy từ query string

# ---- K4 · K6 — comment sai số dòng và sai tình trạng backend ----
grep -rn "dòng 58" frontend/src                                       # 4 kết quả — phải là r57
ls frontend/src/pages/ | grep -i voucher                               # chỉ VoucherManagePage (r58 không có)
grep -n "voucher\|Voucher" frontend/src/App.tsx                        # đúng 1 route /vouchers
grep -rn "chưa có bảng Vouchers\|Vouchers chưa có" frontend/src        # VoucherManagePage.tsx:34, App.tsx:368

# ---- K5 — nhãn BelowMinOrder mất con số ----
grep -n "VOUCHER_REASON_META\|BelowMinOrder" frontend/src/components/VoucherDiscountSummary.tsx frontend/src/api/voucherApi.ts
grep -n "BelowMinOrder" backend/SmartBus.Api/Services/VoucherValidationService.cs   # :199 — câu có {minOrderValue}

# ---- K7 — chỗ lệch trong hợp đồng ----
sed -n '3593,3612p' docs/api-contract.md    # bảng 4 dòng cho dòng 51 … rồi ghi chú 🔴 nói về GET /{id}

# ---- K8 · K9 — ngoài luồng ----
git status --short | grep package-lock
grep -n "package-lock" .gitignore || echo "(không có dòng nào chặn)"
grep -n '"build"\|"lint"' frontend/package.json
grep -n "npm run" .github/workflows/ci.yml    # chỉ 'build', không có 'lint'
```

---

*Báo cáo này **không** sửa một dòng code nghiệp vụ nào — đúng ranh giới "Người kiểm thử — Giàng A
Vàng" ở `docs/03-quy-uoc.md` mục 2.4. File harness dùng để đo ở mục 2.1 đã được **xoá trước khi
commit**; báo cáo chỉ có một file docs, không kèm file test nào. Mọi mục trong mục 3 kèm đường đi
của lỗi và các bước tái hiện để người sở hữu file tự sửa.*
