# Báo cáo kiểm thử chéo — luồng thanh toán (chọn phương thức · chuyển cổng · chờ kết quả)

| | |
|---|---|
| **Người thực hiện** | Giàng A Vàng (Kiểm thử) |
| **Task** | Sprint 3, story 6 — *Kiểm thử chéo luồng thanh toán trên môi trường online* (dòng r37) |
| **Ngày** | 11/10/2026 |
| **Mốc so sánh** | `main` @ `5f03357` |
| **Trạng thái** | ⚠️ Chỉ hoàn thành phần kiểm thử **không cần môi trường online** — xem mục 1 |

> Mã lỗi trong báo cáo này (**H1**…) là cục bộ cho báo cáo này, **khác** bộ `L1`–`L20` của
> `docs/bao-cao-kiem-thu-sprint-1.md`, `C` của `docs/bao-cao-kiem-thu-cheo-lich-trinh-sinh-chuyen.md`,
> `P1`–`P9` của `docs/bao-cao-kiem-thu-cheo-phan-cong.md`, `N` của
> `docs/bao-cao-kiem-thu-cheo-tra-cuu.md`, `V1`–`V7` của `docs/bao-cao-kiem-thu-cheo-ve-thang.md`,
> `F` của `docs/bao-cao-kiem-thu-cheo-phan-anh.md` và `T1`–`T3` của
> `docs/bao-cao-kiem-thu-api-thanh-toan-sandbox.md`. Tám bộ không liên tục và không thay thế nhau.
>
> **H9 lặp lại ở báo cáo thứ tám** (`L20` → `P9` → `N9` → `V7` → `F9` → `G7` → `T3` → **H9**). Ghi
> lại là bằng chứng về việc nó vẫn chưa được xử lý, không phải lỗi mới.

---

## 1. Phạm vi — phần KHÔNG làm được và vì sao

Task yêu cầu kiểm thử **trên môi trường online**. Đây là **báo cáo thứ bảy liên tiếp** mở đầu bằng
đúng câu này, và cả bảy tắc ở **cùng một chỗ**: nhóm vẫn chưa có môi trường online nào.

| Kiểm tra | 6 báo cáo trước | Nay (11/10/2026) |
|---|---|---|
| `render.yaml` / `fly.toml` / `vercel.json` / `netlify.toml` / `docker-compose.yml` / `Procfile` | ❌ không | ❌ **vẫn không** — `ls` cả sáu tên: *No such file* |
| Workflow deploy | ❌ chỉ `ci.yml` | ❌ **vẫn chỉ `ci.yml`** — grep `deploy` trong `.github/workflows/`: **0 kết quả** |
| URL online ở đâu đó trong repo | ❌ không | ❌ **không** — grep `onrender\|vercel.app\|netlify.app\|fly.io\|azurewebsites\|railway.app\|herokuapp` toàn repo (trừ `node_modules`, `dist`, `bin`, `obj`): **0 kết quả thật** (2 dòng khớp là chính câu này viết trong hai báo cáo cũ) |
| `appsettings.Production.json` | ❌ không | ❌ **vẫn không** — chỉ có `appsettings.json` + `appsettings.Development.json.example` |
| `frontend/.env.example` | `localhost:5080` | ❌ **vẫn vậy** |
| Dockerfile backend | ✅ có, chưa từng deploy | ✅ **vẫn chưa từng deploy** (có từ `fb49b74`, "để chạy được trên Render") |
| Task deploy trong backlog | ❌ không có dòng nào | ❌ **không có dòng nào** |

Báo cáo `-cheo-phan-cong` (story 14) ghi *"Chốt việc tạo môi trường online — **chưa có ai nhận** sau
hai sprint"*; báo cáo `-cheo-tra-cuu` nâng thành *"sau ba báo cáo"*. Tới nay là **bảy**.

### 1.1 Tầng chặn thứ hai — lần này khác hẳn sáu báo cáo trước

Sáu báo cáo trước đều tắc ở môi trường, **nhưng vẫn đo được**: endpoint có thật, gửi request thật
trên pipeline test, ra 200/401/403/404 và ghi vào báo cáo. Các báo cáo gần đây còn chỉ ra được
**nguyên nhân gốc** của lỗi (quyền, tham số, logic ngày).

Lần này có thêm một rào **độc lập với môi trường**: **luồng thanh toán chưa có tầng HTTP.**

| Lớp | Có gì trên `main` @ `5f03357` |
|---|---|
| Bảng CSDL | ✅ `Entities/Payment.cs`, `Entities/PaymentStatus.cs`, `Data/AppDbContext.Payment.cs`, migration `20261010105248_Sprint3_Payments_Vouchers_Tickets` |
| DTO trung tính + adapter cổng | ✅ `Dtos/Payments/` — **10 file**: `PaymentProviderCodes`, `PaymentInitiationRequest/Result`, `PaymentCallbackInput/Result`, `MoMoCreatePaymentRequest/Result`, `VnPayCreatePaymentRequest`, `PaymentSettlementOutcome` |
| Service | ✅ `Services/` — `IPaymentGateway`, `IPaymentGatewayResolver`, `IPaymentSettlementService`, `PaymentGatewayResolver`, `PaymentSettlementService` + ba adapter (`MoMoGatewayAdapter`, `VnPayGatewayAdapter`, `ZaloPayGatewayAdapter`, `BankCardGatewayAdapter`) |
| **Controller (tầng HTTP)** | ❌ **KHÔNG có** — `ls Controllers/*.cs` = **32 file**, `grep -rn "payment" Controllers/` = **0 kết quả** |
| **Ba DTO hợp đồng đặt tên** | ❌ **KHÔNG tồn tại** — `CreatePaymentRequest`, `CreatePaymentResult`, `PaymentStatusResult` (grep `class ...` trong `SmartBus.Api/`: 0 kết quả) |

**Hệ quả:** *kể cả có môi trường online cũng không có gì để gọi.* Cả năm endpoint của bảng
`docs/api-contract.md:3221-3228` vẫn mang trạng thái *"Chưa làm"* / *"Chờ bảng"*.

**Nhưng phép "chéo" vẫn có nghĩa** — và vẫn tìm ra lỗi thật — vì **frontend đã dựng đủ ba màn hình**
của chuỗi, còn backend đã dựng đủ tầng service. Chuỗi mà tài liệu và chính các comment trong
`App.tsx` mô tả:

```
tra cứu tuyến → kết quả chuyến → sơ đồ ghế → giữ chỗ 10 phút → chọn phương thức thanh toán
              → chuyển sang cổng → chờ kết quả → vé QR
```

Ba phép kiểm thay thế, đều **chạy lại được**:

1. **Đo trên pipeline HTTP thật** (app thật qua `TestAppFactory`, EF InMemory) — mục 2.1.
2. **Chạy logic thuần của frontend bằng `node` 24** (type stripping) — mục 2.2.
3. **Đối chiếu ba phía** frontend ↔ `docs/api-contract.md` ↔ backend — mục 3.

---

## 2. Mốc chuẩn trên `main` @ `5f03357`

| Hạng mục | Lệnh | Kết quả |
|---|---|---|
| Build backend | `dotnet build backend/SmartBus.sln -c Release` | ✅ **0 Warning, 0 Error** |
| Test backend | `dotnet test backend/SmartBus.sln -c Release` | ✅ **1121/1121 pass**, 0 fail, 0 skip, 2 m 3 s |
| Build frontend | `npm --prefix frontend run build` | ✅ exit 0 — chỉ còn cảnh báo chunk > 500 kB (có sẵn từ trước) |
| Lint frontend | `npm --prefix frontend run lint` | ⚠️ **1 cảnh báo** — `StopManagePage.tsx:33:10` `react(set-state-in-effect)`, **ngoài** luồng thanh toán |
| Rác debug | grep `console.log` / `debugger` / `TODO` / `FIXME` trong `frontend/src` | ✅ **sạch, 0 kết quả** |
| Bí mật bị commit | `git ls-files` lọc `.env`, `appsettings.Development.json`, `secrets` | ✅ sạch — chỉ `frontend/.env.example` + `backend/SmartBus.Api/appsettings.Development.json.example`; `appsettings.json` không có chuỗi kết nối |

> **Vì sao nền là 1121 và không phải 1145:** 24 ca sandbox của task r36 nằm ở nhánh
> `feature/6-test-api-thanh-toan-sandbox-3-cong` (**chưa merge**). Báo cáo đó ở
> `docs/bao-cao-kiem-thu-api-thanh-toan-sandbox.md`. Báo cáo **này** không thêm ca test nào, nên con
> số phải **không đổi**.

### 2.1 Đo trên pipeline HTTP thật — bảng số liệu

Dựng một hành khách + tuyến + xe + sơ đồ + ba ghế + chuyến trong CSDL test, rồi gửi **request thật**
vào **đúng những đường dẫn** mà ba màn hình thanh toán của frontend sẽ gọi khi bỏ cờ dữ liệu giả.

**Phép so sánh quyết định là 401 với 404.** Endpoint *có thật* mà thiếu token trả **401** (middleware
xác thực chạy trước routing). Đường dẫn *không tồn tại* trả **404 kể cả khi có token hợp lệ**.
Đường dẫn đối chứng `/api/khong-ton-tai-xyz` cho chữ ký 404 "route không tồn tại" để so.

| Request | Kết quả đo được | Đọc ra |
|---|---|---|
| `GET /api/khong-ton-tai-xyz` (**đối chứng**) | **404**, `Content-Type` **không có**, body **rỗng** | chữ ký của "route không tồn tại" |
| `GET /api/trips/{id}/seats` | **200**, JSON, có `seats` | ✅ endpoint công khai, chạy thật |
| `POST /api/vouchers/validate` — không token | **401** `{"message":"Chưa đăng nhập. …"}` | ✅ endpoint có thật |
| `POST /api/vouchers/validate` — có token | **200** `{"valid":false,"reasonCode":"NotFound",…}` | ✅ chạy thật, đúng hợp đồng |
| `POST /api/seat-holds` — không token | **401** `{"message":"Chưa đăng nhập. …"}` | ✅ endpoint có thật |
| `POST /api/seat-holds` — có token | **201** `{"sessionCode":"PHIEN-f389881c","status":"Holding","canExtend":true, …}` | ✅ **tạo được phiên giữ chỗ thật** |
| `GET /api/payment-methods` — không token | **404**, `Content-Type` không có, body rỗng | ❌ **không tồn tại** |
| `GET /api/payment-methods` — có token | **404**, chữ ký y hệt đối chứng | ❌ **không tồn tại** |
| `POST /api/payments` — không token | **404**, chữ ký y hệt đối chứng | ❌ **không tồn tại** |
| `POST /api/payments` — có token | **404**, chữ ký y hệt đối chứng | ❌ **không tồn tại** |
| `GET /api/payments/{paymentCode}` — có token | **404**, chữ ký y hệt đối chứng | ❌ **không tồn tại** |
| `GET /api/payments/{paymentCode}` — không token | **404**, chữ ký y hệt đối chứng | ❌ **không tồn tại** |
| `POST /api/payments/momo/callback` | **404**, chữ ký y hệt đối chứng | ❌ **không tồn tại** |
| `GET /api/payments/vnpay/callback` | **404**, chữ ký y hệt đối chứng | ❌ **không tồn tại** |
| `POST /api/payments/zalopay/callback` | **404**, chữ ký y hệt đối chứng | ❌ **không tồn tại** |

Hai chi tiết đáng chú ý trong bảng:

- **Cả sáu đường dẫn `/payments*` trả về chữ ký 404 giống hệt đường dẫn đối chứng** — cùng mã, cùng
  việc `Content-Type` không có và body rỗng. Nếu route tồn tại mà chỉ thiếu quyền thì đã ra **401**
  (như `POST /api/seat-holds`), hoặc 404 **kèm** `{"message":"…"}` của tầng service (như
  `GET /api/routes/{id}` với id lạ). Đây là **bằng chứng cơ học**: routing không khớp được route nào.
- `POST /api/seat-holds` với token trả **201 kèm `sessionCode` thật**. Nghĩa là **mắt xích giữ chỗ
  đã xong ở backend** — điều này quan trọng cho **H7** bên dưới: backend tạo phiên được, chỉ có
  frontend **không bao giờ gọi**.

### 2.2 Chạy logic thuần của frontend bằng `node` 24

`frontend/src/components/paymentWaiting.ts` chỉ có `import type` nên **nạp thẳng được** bằng node 24
(type stripping) — không cần dựng React, không cần hạ tầng test (frontend **không có**
vitest/jest; `frontend/package.json` chỉ có `dev` / `build` / `lint` / `preview`, và CI chỉ build).
**13/13 ca ĐẠT, 0 hỏng:**

| Ca | Mong đợi | Kết quả |
|---|---|---|
| `PAYMENT_TIMEOUT_SECONDS` | `60` | ✅ 60 |
| `PAYMENT_POLL_INTERVAL_SECONDS` | `3` | ✅ 3 |
| `null` (chưa poll lần nào), giây 0 | `Pending`, còn 60s | ✅ |
| `Pending`, giây 30 | `Pending`, còn 30s | ✅ |
| `Pending`, giây **đúng** 60 | `Timeout` | ✅ |
| `Pending`, giây 61 | `Timeout` | ✅ |
| `Success`, giây 5 | `Success` ngay, bỏ qua đồng hồ | ✅ |
| `Failed`, giây 5 | `Failed` ngay | ✅ |
| `Success`, giây **90** (đã quá hạn) | **`Success`**, KHÔNG `Timeout` | ✅ — thứ tự ưu tiên đúng |
| `formatWaitCountdown(60 / 9 / 0 / −5)` | `"1:00"` / `"0:09"` / `"0:00"` / `"0:00"` | ✅ cả bốn |

Số liệu này khớp **từng con số** với `docs/api-contract.md:3358-3359` (*"Màn chờ poll mỗi 3 giây,
tự hết thời gian chờ sau 60 giây — timeout là khái niệm PHÍA FRONTEND"*). Chi tiết ở mục 4.

---

## 3. Danh sách lỗi

Mức độ: **🔴 Cao** = sai chức năng hoặc gây hiểu nhầm nghiêm trọng · **🟠 TB** = ảnh hưởng trải
nghiệm hoặc rủi ro · **🟡 Thấp** = tài liệu, vệ sinh mã.

**Nhóm A — thuộc luồng thanh toán** (việc của task r37 này):

| # | Lỗi | Mức | Người xử lý |
|---|---|---|---|
| H1 | Ba màn hình **đứt ở giữa** — `/payment` không có lối vào nào, chỉ vào được bằng cách gõ URL tay | 🔴 Cao | Nguyễn Đình Băng (+ Dương Thị Hạnh) |
| H2 | Mã phương thức trên màn chọn **trái hợp đồng đã chốt**, và **thiếu `BankCard`** — đúng phương thức US 6 hứa | 🔴 Cao | Dương Thị Hạnh |
| H3 | Màn hình báo **"thanh toán thành công"** mà **không hề chuyển khách sang cổng nào** | 🔴 Cao | Dương Thị Hạnh |
| H4 | Voucher khách đã bấm xác nhận **không được gửi kèm** khi tạo giao dịch | 🟠 TB | Dương Thị Hạnh |
| H5 | Màn chờ **luôn hiển thị MoMo** và một **số tiền cứng**, bất kể khách chọn cổng nào | 🟠 TB | Nguyễn Đình Băng |
| H8 | **Bốn comment nói sai** tình trạng backend — và chính chúng là lý do cờ dữ liệu giả còn bật | 🟡 Thấp | Dương Thị Hạnh · Băng |

**Nhóm B — mắt xích lân cận** (ghi kèm để không mất dấu, chuyển đúng người; **không** phải phạm vi
task r37):

| # | Lỗi | Mức | Người xử lý |
|---|---|---|---|
| H6 | **Bốn cờ dữ liệu giả** chặn đúng bốn mắt xích đã có backend thật | 🟠 TB | Băng · Hạnh · Thịnh |
| H7 | **Không UI nào TẠO phiên giữ chỗ** → toàn bộ phần giữ chỗ ở frontend là code chết | 🟠 TB | Nguyễn Đình Băng |
| H9 | `package-lock.json` lạc ở gốc repo (**lần thứ tám**) | 🟡 Thấp | Phùng Duy Hoàng |

> **Về việc gán người xử lý:** tên người suy từ `Product_Backlog_Smart_Bus.xlsx` sheet Sprint 3 —
> r34 màn chọn phương thức = **Dương Thị Hạnh**, r35 màn chờ kết quả = **Nguyễn Đình Băng**,
> `App.tsx` thuộc Nguyễn Đình Băng (mục E1) — và từ chính comment trong mã nguồn
> (`paymentApi.ts:12,15`; `PaymentMethodPage.tsx:22-23`; `PaymentWaitingPage.tsx:27-28`). Cần đối
> chiếu lại nếu phân công đã đổi.

---

### H1 — Chuỗi ba màn hình đứt ở giữa: `/payment` không có lối vào 🔴 Cao

**Đường đi của lỗi:**

1. `SeatMapPage.tsx:109-121` — `handleContinue` dựng query string `paymentCode: \`demo-${tripId}\``
   (`:114`) rồi
2. `SeatMapPage.tsx:119` — `navigate(\`/payment-waiting?${params.toString()}\`)` → **nhảy thẳng** tới
   màn chờ, **bỏ qua** `/payment`.
3. `App.tsx:222` — `<Route path="/payment" element={<PaymentMethodPage />} />` là **nơi duy nhất**
   trong toàn bộ `frontend/src` nhắc tới `/payment`.

**Bằng chứng cơ học:** `grep -rn "'/payment'\|\`/payment?\|\"/payment\"" frontend/src` → **đúng một
kết quả**, chính là dòng khai báo route `App.tsx:222`. Không `navigate()`, không `<Link>`, không mục
menu nào trỏ tới đó. Màn "chọn phương thức thanh toán" (r34, Dương Thị Hạnh) **chỉ vào được bằng
cách gõ URL tay**.

**Vì sao đây là lỗi chứ không phải "chờ nối":** chính mã nguồn đã tự khai cả hai đầu.

- `App.tsx:216-221` viết về route `/payment`: *"Dòng route này do Dương Thị Hạnh thêm hộ: màn hình
  **cần đường vào**, mà App.tsx là file dùng chung §E1 (của Nguyễn Đình Băng) — đã ghi rõ trong PR
  để Băng xem."* Ai đó đã biết và đã ghi lại, nhưng chưa ai làm.
- `SeatMapPage.tsx:110-113` tự nhận: *"Đây là **lối tắt DEMO** để chạm được màn hình chờ ngay khi
  các bước trung gian (giữ chỗ của …) chưa nối xong."*

**Hệ quả nghiệp vụ:** hai việc **chỉ** màn `/payment` làm được — chọn cổng thanh toán và áp voucher
— **không nằm trong luồng**. Khách bấm "Tiếp tục" ở sơ đồ ghế là **không bao giờ** tới được bước
chọn phương thức. Đồng thời `paymentCode` mà màn chờ nhận là `demo-{tripId}` — **không phải** mã
giao dịch nào cả, nên mọi lượt poll đều hỏi về một giao dịch không tồn tại.

**Các bước tái hiện (không cần môi trường online):**

1. `npm --prefix frontend run dev`, mở `/seat-map`, chọn ghế, bấm "Tiếp tục".
2. Quan sát thanh địa chỉ: ra `/payment-waiting?paymentCode=demo-…` — **không** đi qua `/payment`.
3. `grep -rn "/payment'" frontend/src` → chỉ ra dòng `App.tsx:222`.

---

### H2 — Mã phương thức trái hợp đồng đã chốt, và thiếu đúng phương thức US 6 hứa 🔴 Cao

**Ba phía, hai phía đã chốt và đã xây xong, một phía chưa đổi:**

| Phía | Nơi | Giá trị |
|---|---|---|
| **Hợp đồng** | `docs/api-contract.md:3192` (bảng "Enum chốt") | **`'MoMo'\|'VNPay'\|'ZaloPay'\|'BankCard'`** |
| **Backend** | `Dtos/Payments/PaymentProviderCodes.cs` | ✅ **đã có đủ bốn hằng**: `MoMo`, `VnPay = "VNPay"`, `ZaloPay`, `BankCard` — kèm chú thích *"gõ sai mã là lỗi biên dịch, không phải lỗi âm thầm lúc chạy"* |
| **Frontend** | `frontend/src/api/paymentApi.ts:68` | ❌ `'VnPay' \| 'Momo' \| 'ZaloPay' \| 'BankTransfer' \| 'Cash'` |

`MOCK_METHODS` (`paymentApi.ts:144-175`) vẽ **đúng năm mã đó**, nên màn hình liệt kê đúng năm dòng
sai. Sai **cả hai vế**:

- **Chữ hoa:** `'VnPay'` (phải là `'VNPay'`), `'Momo'` (phải là `'MoMo'`).
- **Tập giá trị:** có thêm `'BankTransfer'` và `'Cash'`; **thiếu `'BankCard'`**.

**Bên nào đúng, và vì sao không thể hoà giải:** hợp đồng đã **chốt** và ghi rõ lý do
(`api-contract.md:3192`):

> *"Đúng danh sách US 6; **`BankTransfer`/`Cash` không có cổng nào triển khai trong dự án**; viết
> hoa CamelCase đúng A3"* — và cột **Chốt** mở đầu bằng ***"FE đổi theo mục này"***.

Bảng đó còn ghi thẳng **hai bản nháp FE đang lệch nhau**, và cột "Bản nháp Hạnh (màn chọn)" ghi
**nguyên văn** chuỗi `'VnPay'|'Momo'|'ZaloPay'|'BankTransfer'|'Cash'` — tức hợp đồng **đã biết** và
**đã loại** đúng chuỗi đang nằm ở `paymentApi.ts:68`. Backend thì đã xây theo cột Chốt
(`PaymentProviderCodes.cs`).

**Hệ quả nếu lật cờ:** `methodCode` là trường **bắt buộc** của `POST /payments`
(`api-contract.md:3245`), và cùng dòng đó chốt *"Mã ngoài bốn mã này → **400** kèm danh sách
cổng đã mở"*. Nên **mọi lựa chọn trên màn hình hiện tại đều sẽ bị từ chối** — kể cả khi Hiếu dựng
xong endpoint.

Và hệ quả nặng hơn: **`BankCard` — mã duy nhất cho "thẻ ngân hàng" — không có trên màn hình.** User
Story 6 phát biểu nguyên văn *"thanh toán qua MoMo, VNPay, ZaloPay **hoặc thẻ ngân hàng**"*. Màn
chọn phương thức **không bày ra** lựa chọn đó.

**Chính mã nguồn FE đã biết mình lệch:** `paymentApi.ts:24-27` viết

> *"màn 'chọn phương thức thanh toán' đang dùng một kiểu khác (`PaymentMethodCode`) vì hai task viết
> song song trước khi backend có enum — … **Khi Dăm chốt enum backend thì gộp `PaymentMethod` và
> `PaymentMethodCode` thành một, đừng để lệch lâu**."*

Backend **đã** chốt (`PaymentProviderCodes.cs`), và hợp đồng **đã** chốt (`:3190-3194`). Điều kiện
"khi nào thì gộp" đã thoả; việc gộp chưa làm.

**Các bước tái hiện:**

1. `sed -n '68p;144,175p' frontend/src/api/paymentApi.ts` — năm mã sai.
2. `sed -n '3190,3194p' docs/api-contract.md` — bảng Chốt.
3. `grep -n "public const" backend/SmartBus.Api/Dtos/Payments/PaymentProviderCodes.cs` — bốn hằng đúng.

---

### H3 — Màn hình báo "thanh toán thành công" mà không hề chuyển khách sang cổng 🔴 Cao

**Đường đi của lỗi:**

1. `PaymentMethodPage.tsx:95-114` — `handlePay`.
2. `:100-105` — `await paymentApi.pay({ methodCode: selectedCode, tripId, seatNumbers, total: … })`
   → **giá trị trả về bị vứt bỏ**, không gán vào biến nào.
3. `:106` — `message.success('Thanh toán thành công. Vé của bạn đã được ghi nhận.')`
4. `:107` — `navigate('/my-tickets')` → đẩy khách sang "Vé của tôi".

**Hợp đồng chốt ngược lại** (`api-contract.md:3262`):

> `// 200 — CreatePaymentResult: **FE dùng `payUrl` để chuyển khách sang MoMo, rồi poll GET
> /payments/{paymentCode}`}``

Tức bước đúng là: tạo giao dịch → nhận `payUrl` → **chuyển khách sang cổng** → quay về màn chờ →
poll. Ba màn hình của FE có đủ hai đầu (chọn phương thức, chờ kết quả) nhưng **thiếu hẳn bước giữa**.

**Vì sao chắc chắn đây là lỗi, không phải "chờ backend":**

- Kiểu FE khai cho phản hồi — `PaymentResult` (`paymentApi.ts:85-95`) — **không có trường `payUrl`**.
  Có `transactionId`, `methodCode`, `status`, `paidAt`. Nên kể cả khi backend trả `payUrl`, màn hình
  **không có chỗ để đọc nó**.
- Mock `pay()` (`paymentApi.ts:211-223`) **luôn** trả `status: 'Paid'` sau 900 ms **bất kể đầu vào**
  — không có nhánh thất bại, không có `Pending`, không có `payUrl`.
- `PaymentResult.status: 'Paid'` (`:91`) đúng là giá trị mà **cả hai** tầng đã loại:
  `Entities/PaymentStatus.cs` cảnh báo *"KHÔNG có giá trị 'Paid' — đó là trạng thái của VÉ, không
  phải của giao dịch"*, và `api-contract.md:3193` chốt `'Pending'|'Success'|'Failed'` kèm lý do
  *"`Paid` là trạng thái của **vé**, không phải giao dịch"*.

**Kịch bản hỏng cụ thể khi lật cờ:** backend thật trả giao dịch `status: 'Pending'` kèm `payUrl`
(chưa ai trả tiền). Màn hình vẫn hiện *"Thanh toán thành công. Vé của bạn đã được ghi nhận."* và
đẩy khách sang "Vé của tôi" — **nơi không có vé nào**, vì vé chỉ phát hành sau khi cổng xác nhận
(r40, "API phát hành vé điện tử sau khi thanh toán thành công"). Khách bị báo thành công cho một
giao dịch chưa hề xảy ra.

**Các bước tái hiện:**

1. `sed -n '95,114p' frontend/src/pages/PaymentMethodPage.tsx`.
2. `sed -n '85,95p;205,225p' frontend/src/api/paymentApi.ts` — kiểu không có `payUrl`, mock luôn
   `'Paid'`.
3. `sed -n '3260,3262p' docs/api-contract.md` — hợp đồng nói phải dùng `payUrl`.

---

### H4 — Voucher khách đã xác nhận không được gửi kèm khi tạo giao dịch 🟠 TB

**Đường đi của lỗi:**

1. `PaymentMethodPage.tsx:119-137` — `handleApplyVoucher` gọi `voucherApi.validate(...)` (`:129`) và
   lưu kết quả vào state **chỉ để hiển thị** (`VoucherDiscountSummary`).
2. `PaymentMethodPage.tsx:100-105` — `handlePay` gửi **chỉ bốn trường**:
   `methodCode`, `tripId`, `seatNumbers`, `total`. **Không có `voucherCode`.**
3. `paymentApi.ts:98-108` — `PaymentRequest` khai **không có** trường `voucherCode`.

**Hợp đồng bắt buộc** (`api-contract.md:3249`): `voucherCode` là *"Mã voucher khách **đã bấm xác
nhận** ở màn thanh toán (khác `POST /vouchers/validate` — đó là xem trước)"*.

Và hợp đồng `:3251-3255` gọi đúng tình huống này là **lỗi nặng nhất của luồng**:

> 🔴 *"ở `/vouchers/validate` khách còn đang gõ mã, 'mã không dùng được' là **câu trả lời**; còn ở
> đây khách đã bấm xác nhận, nên **âm thầm thu đủ tiền trong khi khách tưởng đã được giảm** là lỗi
> nặng nhất của luồng này."*

**Chính màn hình tự nhận** — comment `PaymentMethodPage.tsx:116-118` nói mã đã xác nhận *chưa* được
gửi sang bước thanh toán. Ghi nhận rồi nhưng chưa nối.

**Hệ quả:** khách dán mã, thấy "đã giảm 4.800đ", bấm thanh toán — và bị thu **đủ** tiền. Hợp đồng
`amount` là *"số tiền PHẢI TRẢ, đã trừ giảm giá"* (`:3275`), nên sai ở đây là sai **con số gửi lên
cổng**, không phải sai hiển thị.

**Các bước tái hiện:**

1. `grep -n "voucherCode" frontend/src/api/paymentApi.ts frontend/src/pages/PaymentMethodPage.tsx`
   → **0 kết quả** trong cả hai file.
2. `sed -n '3247,3258p' docs/api-contract.md` — ràng buộc và lý do.

---

### H5 — Màn chờ luôn hiển thị MoMo và một số tiền cứng 🟠 TB

**Đường đi của lỗi:**

1. `paymentApi.ts:225-238` — mock `getStatus(paymentCode)` trả về **hằng số**:
   `amount: 24000` (`:231`) và `method: 'MoMo'` (`:232`), **không phụ thuộc `paymentCode`** (chỉ
   `status` là suy từ mã, qua `mockStatus(paymentCode)`).
2. `PaymentWaitingPage.tsx:134` — `const method = result?.method ?? null;` → lấy cổng **từ kết quả
   poll**.
3. `PaymentWaitingPage.tsx:137-143` — `subtitle` ghép `methodLabel` + số tiền + số ghế, in ngay
   dưới tiêu đề.

**Hệ quả:** khách chọn VNPay vẫn thấy tiêu đề ghi **"MoMo"**; khách trả 48.000đ vẫn thấy
**"24.000 ₫"**. Đây là dữ liệu sai hiển thị cho khách về **cổng** và **số tiền** — hai thứ nhạy cảm
nhất của một màn thanh toán.

**Hợp đồng chốt `GET /payments/{paymentCode}` trả về số thật** (`api-contract.md:3339-3351`): phản
hồi mẫu có `"amount": 48000`, `"method": "MoMo"` — lấy từ bản ghi giao dịch, không hằng số.

**Điểm cần nói rõ:** chỗ này **không** phải lỗi của `PaymentWaitingPage.tsx`. Màn chờ làm **đúng**:
lấy cổng từ kết quả poll, đúng như comment `:32-33` giải thích (*"Cổng (`method`) lấy từ kết quả
poll — đó là dữ liệu do backend trả về khi kiểm tra giao dịch"*). Lỗi nằm ở **dữ liệu giả** mà
`paymentApi.ts` trả cho nó. Sửa ở `paymentApi.ts`, **không** sửa màn chờ.

**Các bước tái hiện:**

1. `sed -n '226,239p' frontend/src/api/paymentApi.ts` — hai hằng số.
2. `sed -n '130,143p' frontend/src/pages/PaymentWaitingPage.tsx` — lấy và in ra.
3. Mở `/payment-waiting?paymentCode=bat-ky` → tiêu đề luôn "MoMo · 24.000 ₫".

---

### H8 — Bốn comment nói sai tình trạng backend — và chính chúng là lý do cờ dữ liệu giả còn bật 🟡 Thấp

Đây là **cùng một dạng lỗi** đã ghi ở báo cáo `-cheo-ve-thang` (V5) và `-cheo-phan-anh` (F5). Mức
Thấp về mặt kỹ thuật, nhưng là **nguyên nhân trực tiếp** của H2/H3/H4: cờ `USE_MOCK_DATA` chỉ được
lật khi người ta tin rằng backend đã xong.

| # | Nơi | Câu sai | Sự thật |
|---|---|---|---|
| 1 | `paymentApi.ts:6-7` | *"Backend **CHƯA có bảng Payments** và **CHƯA có endpoint thanh toán nào** trong docs/api-contract.md"* | **Sai cả hai vế**: bảng `Payments` đã merge (`Data/AppDbContext.Payment.cs:13` + migration `20261010105248_Sprint3_Payments_Vouchers_Tickets`); `api-contract.md:3158-3511` là **một mục thanh toán đầy đủ** với bảng enum chốt + bảng endpoint + hình dạng request/response |
| 2 | `paymentApi.ts:18` | *"Hợp đồng **DỰ KIẾN — chưa chốt**, đặt kebab-case theo quy ước D1"* | Hợp đồng **đã chốt** — `:3190-3194` mở đầu bằng *"chốt như sau (FE đổi theo mục này)"* |
| 3 | `paymentApi.ts:122-123` | *"Đang để `true` vì **endpoint chưa có** … sau khi Hiếu chốt hợp đồng thì đổi xuống `false`"* | Hợp đồng chốt xong; cái **còn thiếu** là controller — và cờ phải lật theo **tầng**, không theo cả file (xem H6) |
| 4 | `PaymentMethodPage.tsx:24-25` và `App.tsx:218-219` | *"Backend **chưa có bảng Payments** và chưa có endpoint thanh toán nên màn hình chạy dữ liệu giả"* | Như #1 |

Thêm hai comment cùng nhóm, **đúng phần chẩn đoán nhưng đã quá hạn**:

- `paymentApi.ts:24-27` — *"hai task viết song song trước khi backend có enum … **Khi Dăm chốt enum
  backend thì gộp** `PaymentMethod` và `PaymentMethodCode` thành một"*. Backend **đã** chốt
  (`PaymentProviderCodes.cs`) → việc gộp đã tới hạn (xem **H2**).
- `paymentApi.ts:144-146` — *"Tập mã cố ý đặt PascalCase giống enum backend **sẽ khai báo**"*. Sẽ =
  đã rồi.

**Các bước tái hiện:** `grep -rn "CHƯA có\|chưa có\|chưa chốt\|DỰ KIẾN\|sẽ khai báo"
frontend/src/api/paymentApi.ts frontend/src/pages/PaymentMethodPage.tsx frontend/src/App.tsx`.

---

### H6 — Bốn cờ dữ liệu giả chặn đúng bốn mắt xích đã có backend thật 🟠 TB

> **Mắt xích lân cận** — chuyển cho người sở hữu, không sửa trong task này.

Bốn module frontend đang `USE_MOCK_DATA = true`, trong khi backend **đã có** endpoint thật:

| File | Dòng | Backend đã có |
|---|---|---|
| `frontend/src/api/seatMapApi.ts` | `:79` | ✅ `GET /api/trips/{id}/seats` — **công khai**, đo được **200** (mục 2.1) |
| `frontend/src/api/seatHoldApi.ts` | `:69` | ✅ `GET /api/seat-holds/{sessionCode}` (`:76`), `POST …/extend` (`:79`) |
| `frontend/src/api/seatHoldReleaseApi.ts` | `:32` | ✅ `POST /api/seat-holds/{sessionCode}/release` (`:39`) |
| `frontend/src/api/voucherApi.ts` | `:199` | ✅ `POST /api/vouchers/validate` (`:207-209`) — đo được **200** (mục 2.1) |

**Hai hệ quả trực tiếp lên luồng thanh toán:**

1. **`total` mà màn thanh toán gửi lên là con số do client bịa.** Màn chọn phương thức nhận `total`
   qua query string từ màn trước, mà màn trước (`SeatMapPage`) đọc sơ đồ ghế từ **dữ liệu giả**. Hợp
   đồng `:3248` nói `total` phải *"khớp tổng giá các ghế đã chọn"* và backend **không tin** con số FE
   gửi (nó kiểm lại voucher và giá). Nhưng khi `total` sai từ gốc, mọi ràng buộc đơn (sàn MoMo
   10.000đ, `minOrderValue` của voucher) đều bị so với một số bịa.
2. **Mắt xích duy nhất đã xong ở backend thì màn hình không bao giờ gọi.**
   `voucherApi.ts:204` tự ghi *"`validate` đã có endpoint thật trong api-contract.md nên viết thẳng"*
   — nhưng cờ **dùng chung** cho cả `validate` **lẫn** CRUD (interface `VoucherApi` ở `:188-195`:
   `list` / `create` / `update` / `updateStatus` **và** `validate`), mà **CRUD chưa có endpoint**:
   hai controller trên bề mặt `api/vouchers` chỉ mở đúng hai route — `POST validate`
   (`VoucherValidationController.cs:45`) và `GET statistics`
   (`VoucherStatisticsController.cs:43`) — **không** có `GET /vouchers`, `POST /vouchers`,
   `PUT /vouchers/{id}`. Nên bật cờ là cách duy nhất hiện có — tức **một tính năng chưa làm kéo
   theo một tính năng đã xong nằm im**.

> *Đếm cho chính xác:* `grep -rn "^const USE_MOCK_DATA = true" frontend/src/api/` ra **bảy** module.
> Bốn module ở bảng trên có backend thật; ba module còn lại — `paymentApi.ts:124` (chính H2/H3/H4),
> `seatLayoutApi.ts:173`, `ticketApi.ts:65` — thì **đúng là** chưa có endpoint, cờ bật là hợp lý.

**Các bước tái hiện:** `grep -rn "^const USE_MOCK_DATA = true" frontend/src/api/`.

---

### H7 — Không UI nào TẠO phiên giữ chỗ: toàn bộ phần giữ chỗ ở frontend là code chết 🟠 TB

> **Mắt xích lân cận** — nhưng là mắt xích **ngay trước** bước thanh toán, nên ghi rõ ở đây.

**Bằng chứng cơ học — frontend chỉ có ba trong bốn thao tác của vòng đời một phiên giữ chỗ:**

| Thao tác | Endpoint | Có trong `frontend/src`? |
|---|---|---|
| **Tạo phiên** | `POST /api/seat-holds` | ❌ **KHÔNG** — `grep -rn "seat-holds" frontend/src` chỉ ra `GET` (`seatHoldApi.ts:76`), `extend` (`:79`), `release` (`seatHoldReleaseApi.ts:39`). Không có lời gọi POST nào. |
| Đọc phiên | `GET /api/seat-holds/{sessionCode}` | ✅ `seatHoldApi.ts:76` |
| Gia hạn | `POST …/extend` | ✅ `seatHoldApi.ts:79` |
| Nhả ghế | `POST …/release` | ✅ `seatHoldReleaseApi.ts:39` |

Không có nơi nào sinh `sessionCode` → mọi component đọc `GET`/`extend`/`release` **không bao giờ có
đầu vào để chạy**. `SeatHoldExpiryModal.tsx` và `useSeatHoldAutoRelease.ts` là **code chết**.

**Backend thì đã xong** — đo được ở mục 2.1: `POST /api/seat-holds` với token trả **201** kèm
`{"sessionCode":"PHIEN-f389881c","status":"Holding","canExtend":true}`. Nghĩa là **vấn đề hoàn toàn
nằm ở phía frontend**.

**Hệ quả nghiệp vụ:** lời hứa của story *"tự động giữ chỗ trong 10 phút khi khách đang thao tác
thanh toán để tránh trùng lặp vé"* **chưa từng bắt đầu** ở phía khách. Và sơ đồ ghế (`SeatMapPage`)
vì thế phải nhảy thẳng sang màn chờ bằng `paymentCode: demo-${tripId}` (xem H1) — hai lỗi này là
**cùng một gốc**: chưa có phiên giữ chỗ thật thì không có gì để nối vào bước thanh toán.

**Các bước tái hiện:** `grep -rn "PostAsync\|axiosClient.post" frontend/src/api/seatHold*.ts` →
không có `/seat-holds` trần.

---

### H9 — `package-lock.json` lạc ở gốc repo (lần thứ tám) 🟡 Thấp

> **Không phải file của tôi**, và **không** được đưa vào commit của báo cáo này.

File `package-lock.json` ở **gốc repo** (98 byte, 26/09/2026) vẫn `??` trong `git status` và
`git check-ignore` **không** chặn. Nó **không** phải lockfile của `frontend/` — gốc repo không có
`package.json`.

Chuỗi tám lần ghi nhận, không lần nào được xử lý:

`L20` (sprint 1) → `P9` (phân công) → `N9` (tra cứu) → `V7` (vé tháng) → `F9` (phản ánh) →
`G7` (giữ chỗ) → `T3` (sandbox thanh toán) → **`H9`**.

Cách sửa nằm ở `.gitignore` — thuộc **Phùng Duy Hoàng** (mục E1).

---

## 4. Đã kiểm và ĐẠT

### 4.1 Trạng thái giao dịch của frontend — **khớp chính xác** hợp đồng và backend

`components/paymentWaiting.ts` là chỗ **hai tầng hiểu đúng nhau** trong luồng này:

- Tập pha `'Pending' | 'Success' | 'Failed'` (`:32`) + pha `Timeout` **chỉ ở FE** — khớp
  `Entities/PaymentStatus.cs` (`Pending | Success | Failed`, kèm cảnh báo *"KHÔNG có giá trị
  'Paid'"*) và khớp `api-contract.md:3193`. Đây chính là tập mà `PaymentResult.status: 'Paid'`
  (H3) đã vi phạm.
- `PAYMENT_TIMEOUT_SECONDS = 60` và `PAYMENT_POLL_INTERVAL_SECONDS = 3` khớp **từng con số** với
  `api-contract.md:3358-3359`: *"Màn chờ poll mỗi 3 giây, tự hết thời gian chờ sau 60 giây — timeout
  là khái niệm PHÍA FRONTEND, không có trạng thái 'Timeout' trong CSDL"*.
- Màn chờ **không tự đoán** kết quả khi hết hạn: pha `Timeout` mời khách *"kiểm tra lại"*
  (`PaymentWaitingPage.tsx:229-253`) chứ không gọi là thành công hay thất bại — đúng tinh thần
  `api-contract.md:3359-3360`.
- Phân biệt **lỗi thoáng qua** (mất mạng → tiếp tục poll) và **lỗi dứt khoát** (4xx/5xx → dừng và
  hiển thị riêng) ở `PaymentWaitingPage.tsx:85-94` là thiết kế đúng, không mạo nhận "đang chờ".
- **13/13 ca logic chạy thật bằng node** — mục 2.2.

### 4.2 Endpoint của backend — chạy thật, đúng hợp đồng

| Endpoint | Kiểm | Kết quả |
|---|---|---|
| `GET /api/trips/{id}/seats` | Không đăng nhập, chuyến có thật | ✅ **200**, trả `tripId`/`busType`/`floors`/`pricePerSeat`/`seats` |
| `POST /api/vouchers/validate` | Không token | ✅ **401** — chặn đúng |
| `POST /api/vouchers/validate` | Có token, mã không tồn tại | ✅ **200** `{"valid":false,"reasonCode":"NotFound","message":"Mã voucher không tồn tại"}` — đúng chốt *"mã sai trả 200 kèm `valid:false`, KHÔNG phải 4xx"* |
| `POST /api/seat-holds` | Không token | ✅ **401** — chặn đúng |
| `POST /api/seat-holds` | Có token, chuyến + ghế có thật | ✅ **201**, `sessionCode` đúng khuôn `PHIEN-xxxxxxxx`, `status: "Holding"`, `canExtend: true` — khớp `SeatHoldCreateController.cs:34-36` |

### 4.3 Mã phương thức ở tầng backend — **đã đúng hợp đồng**

`Dtos/Payments/PaymentProviderCodes.cs` khai đủ bốn hằng `MoMo` / `VnPay = "VNPay"` / `ZaloPay` /
`BankCard`, kèm chú thích cho từng giá trị (đặc biệt `BankCard` — *"KHÔNG phải tên một cổng: mã này
chạy qua kênh thẻ của VNPay"*). Đây là **nguồn đúng** để H2 đối chiếu, và là lý do H2 chỉ còn là lỗi
**một phía**.

### 4.4 Một quan sát nhỏ, chưa đủ để thành lỗi

`PaymentMethodPage.tsx:22` và `App.tsx:216` đều ghi màn chọn phương thức là **"Sprint 4"**, trong
khi `App.tsx:223` ghi màn chờ là **"Sprint 3"** — và `Product_Backlog_Smart_Bus.xlsx` đặt **cả hai**
(r34, r35) trong **sheet Sprint 3**. Cùng một file `App.tsx` gán hai sprint khác nhau cho hai màn
liền nhau của cùng một story. Chỉ là nhãn trong comment, không ảnh hưởng chạy — ghi lại để lần sau
ai đọc comment không bị lệch.

*(Cũng xin lưu ý: cột `Status` của sheet Sprint 3 vẫn ghi **"Chưa làm"** cho r34/r35 dù hai màn hình
**đã có trong mã nguồn**. Không tính là lỗi — cột đó vốn cập nhật chậm.)*

---

## 5. Việc chuyển cho ai

| Việc | Người | Vì sao là của họ |
|---|---|---|
| Nối đường vào `/payment` từ sơ đồ ghế, bỏ `paymentCode: demo-…` (H1) | **Nguyễn Đình Băng** | `App.tsx` + `SeatMapPage.tsx` thuộc Băng (mục E1); chính `App.tsx:219` đã ghi việc này đang chờ Băng |
| Đổi `PaymentMethodCode` thành bốn mã hợp đồng, thêm `BankCard`, gộp hai kiểu (H2) | **Dương Thị Hạnh** | r34 + `paymentApi.ts` — và `api-contract.md:3192` đã chỉ đích danh *"FE đổi theo mục này"* |
| Dùng `payUrl` để chuyển khách sang cổng, bỏ `message.success` giả (H3) | **Dương Thị Hạnh** | r34 — nút "Thanh toán" nằm ở màn của Hạnh; cần `payUrl` trong `PaymentResult` |
| Gửi `voucherCode` khi tạo giao dịch (H4) | **Dương Thị Hạnh** | r34 + `PaymentRequest` |
| Bỏ `amount: 24000` / `method: 'MoMo'` cứng (H5) | **Dương Thị Hạnh** | Lỗi ở `paymentApi.ts`, **không** ở màn chờ (màn chờ của Băng làm đúng) |
| Sửa bốn comment + hai comment quá hạn (H8) | **Dương Thị Hạnh** (chính) · **Băng** (`App.tsx`) | File của ai người nấy sửa |
| Lật bốn cờ dữ liệu giả (H6) | **Nguyễn Đình Băng** (`seatMapApi`, `seatHoldApi`) · **Dương Thị Hạnh** (`seatHoldReleaseApi`) · **Hoàng Văn Thịnh** (`voucherApi`) | File của ai người nấy sửa — nhưng **phải tách cờ `validate` khỏi cờ CRUD** trước, xem H6 |
| Nối `POST /api/seat-holds` vào UI (H7) | **Nguyễn Đình Băng** | r21/r22 (component đếm ngược + modal hết hạn) là của Băng; đây là mắt xích còn thiếu để chúng chạy |
| **Dựng controller thanh toán** (`POST /payments`, `GET /payments/{paymentCode}`, ba callback, `GET /payment-methods`) | **Trần Trung Hiếu** (chính) + **Nguyễn Duy Kiên** (callback VNPay) | `api-contract.md:3221-3228` ghi rõ từng dòng; ba DTO `CreatePaymentRequest`/`CreatePaymentResult`/`PaymentStatusResult` chưa tồn tại. **Tầng service đã sẵn sàng** (mục 1.1) — chỉ còn tầng HTTP |
| `package-lock.json` (H9) | **Phùng Duy Hoàng** | `.gitignore` thuộc E1 |
| **Tạo môi trường online** | **Phùng Duy Hoàng** (Scrum Master, hạ tầng/CI) | Bảy báo cáo liên tiếp nêu việc này; `ci.yml` vẫn không có bước deploy, `render.yaml` vẫn không có |

---

## 6. Còn lại — chưa kiểm được

| Hạng mục | Vì sao chưa kiểm được |
|---|---|
| Bất kỳ request HTTP thật nào **qua mạng** | Không có môi trường online (mục 1) |
| `POST /payments` | Endpoint **không tồn tại** — đo được 404 cùng chữ ký với route không tồn tại (mục 2.1) |
| `GET /payments/{paymentCode}` | Endpoint **không tồn tại** — đo được 404 (mục 2.1) |
| Ba callback cổng (`momo`, `vnpay`, `zalopay`) | **Không tồn tại** — đo được 404 (mục 2.1) |
| `GET /payment-methods` | **Không tồn tại** — đo được 404 (mục 2.1) |
| Chuyển khách sang trang cổng thật (MoMo/VNPay sandbox) | Cần `POST /payments` trả `payUrl` **và** môi trường có tên miền công khai để cổng gọi IPN về |
| Trạng thái giao dịch lật từ `Pending` sang `Success` | Cần callback hoặc job đối soát chạy thật — cả hai đều chưa có tầng HTTP |
| Đối soát số tiền cuối cùng (khách trả bao nhiêu vs. ghi nhận bao nhiêu) | Cần CSDL chung + cổng thật; hơn nữa `total` hiện do client bịa (H6) |
| Job đối soát tự động | `api-contract.md:3228` ghi *"Chờ bảng — chốt dưới"*, chưa có dòng backlog nào |
| Giao diện trên trình duyệt thật | Không có môi trường để mở; và `/payment` còn chưa có lối vào (H1) |

---

## 7. Phụ lục — lệnh chạy lại mọi bằng chứng trong báo cáo

```bash
# --- Mốc chuẩn -------------------------------------------------------------
dotnet build backend/SmartBus.sln -c Release            # 0 Warning, 0 Error
dotnet test  backend/SmartBus.sln -c Release            # 1121/1121 pass
npm --prefix frontend run build                          # exit 0
npm --prefix frontend run lint                           # 1 cảnh báo cũ StopManagePage.tsx:33
grep -rn "console\.log\|debugger\|TODO\|FIXME" frontend/src   # 0 kết quả

# --- Mục 2.1 — đo trên pipeline HTTP thật ---------------------------------
# Dựng file tạm backend/SmartBus.Tests/ZZProbeTamTests.cs (app thật qua TestAppFactory),
# chạy rồi XOÁ:
dotnet test backend/SmartBus.Tests/SmartBus.Tests.csproj -c Release \
  --filter "FullyQualifiedName~ZZProbeTamTests"
# Kết quả ghi ra tệp, chép vào báo cáo, rồi: rm backend/SmartBus.Tests/ZZProbeTamTests.cs
# Bảng số liệu ở mục 2.1. Đối chứng /api/khong-ton-tai-xyz cho chữ ký "route không tồn tại".

# --- Mục 2.2 — logic thuần của frontend bằng node 24 ----------------------
node -e "import('file:///C:/smart-bus-ticketing/frontend/src/components/paymentWaiting.ts').then(m=>console.log(m.getPaymentWaitInfo({status:'Success'},90),m.formatWaitCountdown(60)))"
# => { phase: 'Success', secondsElapsed: 90, secondsLeft: 0 } 1:00

# --- H1 — /payment không có lối vào ---------------------------------------
grep -rn "'/payment'\|\`/payment?\|\"/payment\"" frontend/src      # đúng 1 kết quả: App.tsx:222
sed -n '109,121p' frontend/src/pages/SeatMapPage.tsx               # lối tắt DEMO
sed -n '216,222p' frontend/src/App.tsx                             # "màn hình cần đường vào"

# --- H2 — mã phương thức -----------------------------------------------
sed -n '68p'    frontend/src/api/paymentApi.ts                     # năm mã SAI
sed -n '3190,3194p' docs/api-contract.md                           # bảng Chốt
grep -n "public const" backend/SmartBus.Api/Dtos/Payments/PaymentProviderCodes.cs   # bốn hằng ĐÚNG

# --- H3 — báo thành công mà không chuyển cổng ----------------------------
sed -n '95,114p' frontend/src/pages/PaymentMethodPage.tsx          # vứt giá trị trả về
sed -n '85,95p'  frontend/src/api/paymentApi.ts                    # PaymentResult KHÔNG có payUrl
sed -n '3260,3262p' docs/api-contract.md                           # "FE dùng payUrl để chuyển khách"

# --- H4 — voucher không được gửi kèm ------------------------------------
grep -n "voucherCode" frontend/src/api/paymentApi.ts frontend/src/pages/PaymentMethodPage.tsx  # 0
sed -n '3247,3258p' docs/api-contract.md

# --- H5 — MoMo + số tiền cứng -------------------------------------------
sed -n '226,239p' frontend/src/api/paymentApi.ts                   # amount: 24000, method: 'MoMo'
sed -n '130,143p' frontend/src/pages/PaymentWaitingPage.tsx        # lấy từ kết quả poll

# --- H6 — bốn cờ dữ liệu giả --------------------------------------------
grep -rn "^const USE_MOCK_DATA = true" frontend/src/api/               # bảy module
grep -rn "HttpPost\|HttpGet" backend/SmartBus.Api/Controllers/VoucherValidationController.cs \
  backend/SmartBus.Api/Controllers/VoucherStatisticsController.cs      # chỉ validate + statistics

# --- H7 — không UI nào tạo phiên giữ chỗ --------------------------------
grep -rn "seat-holds" frontend/src/api/seatHoldApi.ts frontend/src/api/seatHoldReleaseApi.ts
grep -n "class SeatHoldCreateController" -A3 backend/SmartBus.Api/Controllers/SeatHoldCreateController.cs

# --- H8 — comment nói sai ----------------------------------------------
grep -rn "CHƯA có\|chưa chốt\|DỰ KIẾN\|sẽ khai báo" frontend/src/api/paymentApi.ts \
  frontend/src/pages/PaymentMethodPage.tsx frontend/src/App.tsx
ls backend/SmartBus.Api/Data/AppDbContext.Payment.cs
ls backend/SmartBus.Api/Migrations/ | grep Sprint3_Payments

# --- H9 — package-lock.json --------------------------------------------
ls -l package-lock.json && git check-ignore -v package-lock.json

# --- Mục 1 — không có môi trường online ---------------------------------
ls render.yaml fly.toml vercel.json netlify.toml docker-compose.yml Procfile   # đều không có
ls .github/workflows/ && grep -rn "deploy" .github/workflows/                   # chỉ ci.yml, 0 deploy
grep -rniE "onrender|vercel\.app|netlify\.app|fly\.io|azurewebsites|railway\.app|herokuapp" . \
  --include=* | grep -vE "node_modules|/dist/|/bin/|/obj/"
```

---

*Báo cáo này **không** sửa một dòng code nghiệp vụ nào — đúng ranh giới "Người kiểm thử — Giàng A
Vàng" ở `docs/03-quy-uoc.md` mục 2.4. File tạm dùng để đo ở mục 2.1 đã được **xoá trước khi commit**,
nên báo cáo này chỉ có một tệp tài liệu. Mọi mục trong mục 3 kèm các bước tái hiện để người sở hữu
file tự sửa.*
