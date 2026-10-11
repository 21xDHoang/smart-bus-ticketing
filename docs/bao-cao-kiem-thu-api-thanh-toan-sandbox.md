# Báo cáo kiểm thử — API thanh toán với sandbox của 3 cổng (xUnit)

| | |
|---|---|
| **Người thực hiện** | Giàng A Vàng (Kiểm thử) |
| **Task** | Sprint 3, story 6 — *Test API thanh toán với sandbox của 3 cổng (xUnit)* (dòng r36) |
| **Ngày** | 11/10/2026 |
| **Mốc so sánh** | `main` @ `5f03357` |
| **Nhánh** | `feature/6-test-api-thanh-toan-sandbox-3-cong` |
| **Trạng thái** | ⚠️ **Không chạy được một request `/payments` nào** — sáu endpoint của hợp đồng chưa được dựng. Xem **T1**. Phần chạy được: 24 ca xuyên tầng adapter → CSDL, tất cả xanh |

> Mã lỗi trong báo cáo này (**T1**…) là cục bộ cho báo cáo này, **khác** các bộ đã dùng:
> `L1`–`L20` (`bao-cao-kiem-thu-sprint-1.md`), `C1`–`C6` (lich-trinh-sinh-chuyen), `P1`–`P9`
> (phan-cong), `N1`–`N9` (tra-cuu), `V1`–`V7` (ve-thang), `F1`–`F9` (phan-anh), `G1`–`G7`
> (giu-cho). Bảy bộ không liên tục và không thay thế nhau.
>
> **T3 lặp lại ở báo cáo thứ bảy** — cùng một file, cùng một nội dung:
> `L20` → `P9` → `N9` → `V7` → `F9` → `G7` → **T3**. Ghi lại là bằng chứng nó vẫn chưa được xử lý,
> không phải lỗi mới.

---

## 1. Phạm vi — phần KHÔNG làm được và vì sao

Task yêu cầu *"Test API thanh toán với sandbox của 3 cổng"*. **Phần "API" không tồn tại để test.**

`backend/SmartBus.Api/Controllers/` có **32 controller, không một controller thanh toán nào**. Sáu
endpoint mà `docs/api-contract.md` mô tả ở mục "Thanh toán" **đều còn ở trạng thái chưa dựng**:

| Endpoint (hợp đồng) | Dòng | Trạng thái ghi trong hợp đồng | Người được giao |
|---|---|---|---|
| `GET /payment-methods` | `:3223` | *Chưa làm* | chưa có chủ |
| `POST /payments` | `:3224` | *Chờ bảng* | Trần Trung Hiếu |
| `POST /payments/momo/callback` | `:3225` | *Chờ bảng* | Trần Trung Hiếu |
| `GET /payments/vnpay/callback` | `:3226` | *Chờ bảng* | Nguyễn Duy Kiên |
| `GET /payments/{paymentCode}` | `:3227` | *Chờ bảng* | Trần Trung Hiếu |
| *job đối soát* | `:3228` | *Chờ bảng* | Trần Trung Hiếu |

Dòng dẫn `:3177` vẫn còn ghi *"Endpoint dưới đây sẽ có khi migration xong"* — nhưng migration
**đã merge rồi**: bảng `Payments` có entity (`Entities/Payment.cs`), có cấu hình EF
(`Data/AppDbContext.Payment.cs`), có cột `TicketId` đã nối FK từ 10/10/2026. Câu đó hết đúng.

**Vì sao nửa dưới thì test được.** Toàn bộ tầng nghiệp vụ thanh toán **đã có sẵn**: ba client cổng,
bốn adapter (`MoMo`/`VNPay`/`ZaloPay`/`BankCard`), `PaymentGatewayResolver`,
`PaymentSettlementService`, và **11 file test** — nhưng tất cả đều test **rời từng mảnh**:

- bộ test adapter chỉ chạm adapter, không chạm CSDL;
- `PaymentSettlementServiceTests` dựng thẳng `PaymentCallbackResult` — chú thích của chính nó
  (`:432-435`) ghi rõ: *"Kết quả ĐÃ CHUẨN HOÁ mà adapter cổng sẽ đưa vào — ở đây dựng thẳng,
  **không qua adapter**"*;
- hai file `*WiringTests` dùng `TestAppFactory` nhưng **chỉ resolve DI**, không gửi dữ liệu nào.

Nghĩa là **lời hứa quan trọng nhất của cả luồng** — *"cổng gửi lại callback thì không trừ tiền lần
hai"* (`PaymentSettlementService.cs:13-16`) — chưa từng có ca nào chạy **xuyên ranh giới
adapter → CSDL**. Đó đúng là chỗ bài này phủ.

**Không có ca nào gọi mạng.** Hai lý do, cả hai đều cứng:

1. MoMo và ZaloPay cần **khoá đối tác** — nhóm chưa có. Repo **PUBLIC**, `.github/workflows/ci.yml`
   **không có secret nào** (chỉ hai job `backend` và `frontend`), nên để test gọi sandbox thật thì
   khoá phải nằm trong code → vi phạm luật 2. Chỉ VNPay có bộ khoá sandbox **công khai** (in trong
   tài liệu cổng), và VNPay **không có API server-to-server** nên cũng không có gì để chạm từ test.
2. Test gọi mạng làm CI chập chờn → trái DoD (*"CI GitHub xanh"*).

**Cách làm thay thế:** dựng **đúng con đường mà endpoint sẽ đi khi có** — dữ liệu thô của cổng →
adapter qua `IPaymentGatewayResolver` → `IPaymentSettlementService` → dòng `Payments` trong CSDL —
với **cả hai đầu lấy từ DI thật** của app (`TestAppFactory`, CSDL InMemory, job nền noop). Không
dựng tay đối tượng nào. Payload dùng **vector chữ ký tính sẵn** chép nguyên văn từ các test đã có.

---

## 2. Mốc chuẩn trên `main` @ `5f03357`

| Hạng mục | Lệnh | Kết quả |
|---|---|---|
| Build backend | `dotnet build backend/SmartBus.sln -c Release` | ✅ **0 warning, 0 error** |
| Test backend — nền | `dotnet test ... --filter "FullyQualifiedName!~PaymentSandboxCrossLayerTests"` | ✅ **1121/1121 pass**, 0 fail, 0 skip |
| Test backend — có bài mới | `dotnet test backend/SmartBus.sln -c Release` | ✅ **1145/1145 pass**, 0 fail, 0 skip, 1 m 52 s |
| Bài mới | `--filter "FullyQualifiedName~PaymentSandboxCrossLayerTests"` | ✅ **24/24 pass**, 2 s |
| Phân tích đồ thị | `node .gitnexus/run.cjs impact "SettleAsync" --direction upstream --repo .` | ⚠️ `risk: UNKNOWN` — xem T1 |
| Rác debug trong file mới | grep `console.log` / `debugger` / `TODO` / `FIXME` | ✅ không có |
| Bí mật bị commit | `git status` + đọc lại file mới | ✅ chỉ khoá giả / khoá sandbox công khai **đã có sẵn trong repo** |

`1145 = 1121 + 24` — khớp đúng số ca thêm vào, không ca cũ nào đỏ.

### 2.1 Bằng chứng đồ thị cho T1 — và chỗ `UNKNOWN` phải xác minh bằng tra cứu văn bản

`impact` trên `SettleAsync` trả **hai** kết quả trùng tên, và **cả hai đều không tự trả lời được**:

| Ứng viên | Người gọi giải được | `risk` |
|---|---|---|
| `IPaymentSettlementService.SettleAsync` (`IPaymentSettlementService.cs:22`) | 13 | `MEDIUM` |
| `PaymentSettlementService.SettleAsync` (`PaymentSettlementService.cs:28`) | **0** | **`UNKNOWN`** |

Ứng viên thứ hai kèm đúng `riskNote` mà CLAUDE.md cảnh báo: *"Absence of edges is not evidence the
symbol is unused … Confirm with a text search before treating the change as safe."* Vậy nên đã tra
văn bản — và tra văn bản **giải được** câu hỏi đó:

```
grep -rn "SettleAsync|IPaymentSettlementService" backend/
```

→ 6 kết quả ngoài định nghĩa: **`Program.cs:386`** (một dòng `AddScoped`) và **toàn bộ phần còn
lại là test** (`PaymentSettlementServiceTests.cs`, `PaymentSettlementWiringTests.cs`).
**Không một người gọi nào trong code sản xuất.** Làm y hệt với `IPaymentGatewayResolver`: chỉ
`Program.cs:376` + test. Đây là xác nhận T1 bằng **hai nguồn độc lập**, không phải bằng một con số 0.

---

## 3. Danh sách lỗi

| Mã | Mức | Tóm tắt | Người xử lý |
|---|---|---|---|
| **T1** | 🔴 Cao | Không có tầng HTTP cho thanh toán — 6 endpoint chưa dựng; `IPaymentSettlementService` và `IPaymentGatewayResolver` không có người gọi sản xuất | Trần Trung Hiếu · Nguyễn Duy Kiên |
| **T2** | 🟠 TB | Adapter ZaloPay **bỏ mất mốc thu tiền** dù cổng có gửi — cột `PaidAt` rơi về giờ hệ thống | Nguyễn Duy Kiên |
| **T3** | 🟡 Thấp | `package-lock.json` nằm ở gốc repo — **lần thứ 7 liên tiếp** | Phùng Duy Hoàng |

### T1 — 🔴 Cao — Không có tầng HTTP cho thanh toán

**Hiện tượng.** Không tồn tại endpoint nào để gọi. Toàn bộ tầng nghiệp vụ thanh toán đã xong nhưng
**chưa được nối ra ngoài**: `PaymentSettlementService` và `PaymentGatewayResolver` chỉ có dòng DI
trong `Program.cs`, không ai gọi.

**Hệ quả.** Đúng task r36 — *"Test API thanh toán"* — **không thể hoàn thành**. Không phải vì thiếu
môi trường (khác sáu báo cáo trước), mà vì **đối tượng để test chưa được viết**. Đây là việc **ngoài
quyền** của vai trò kiểm thử (quy ước 2.4: ⛔ *sửa code nghiệp vụ để test chạy được*).

**Các bước tái hiện.**

1. `ls backend/SmartBus.Api/Controllers/ | grep -i pay` → **không kết quả**.
2. `grep -n "Chờ bảng\|Chưa làm" docs/api-contract.md` → 5 dòng `:3224`–`:3228` còn nguyên.
3. `node .gitnexus/run.cjs impact "SettleAsync" --direction upstream --repo .` → ứng viên
   `PaymentSettlementService.SettleAsync` có **0 người gọi**, `risk: UNKNOWN` (bảng ở mục 2.1).
4. `grep -rn "IPaymentSettlementService\|IPaymentGatewayResolver" backend/` → ngoài định nghĩa chỉ
   có `Program.cs:386`, `Program.cs:376` và **test**.

**Việc cần làm (không phải việc của người kiểm thử):** dựng `PaymentsController` + các endpoint
callback theo `docs/api-contract.md`; sửa dòng `:3177` vì migration đã xong.

**Ghi chú kèm theo cho người viết endpoint.** Khi gửi lại, `ShouldIssueTickets` **vẫn `true`** dù
`SettledNow` là `false` — vì `TicketId` còn null (`PaymentSettlementService.cs:107`). Đây là **hành
vi cố ý** và đã được khẳng định ở `PaymentSettlementServiceTests.cs:348-362`, lý do ghi rõ trong
`PaymentSettlementService.cs:13-16`: lần phát hành vé trước có thể đứt giữa đường. Nên **endpoint
phải rẽ nhánh theo `SettledNow`, không phải theo `ShouldIssueTickets`** — rẽ nhầm là phát hành vé
hai lần. Không tính là lỗi; ghi lại để không ai đoán.

### T2 — 🟠 TB — Adapter ZaloPay bỏ mất mốc thu tiền

**Hiện tượng.** `PaidAt` của giao dịch ZaloPay luôn là **giờ hệ thống lúc chốt**, không phải giờ
cổng ghi nhận — trong khi MoMo và VNPay đều lấy giờ cổng.

| Cổng | Nguồn mốc trong callback | Adapter có đọc? |
|---|---|---|
| MoMo | `responseTime` (ms epoch) | ✅ `MoMoGatewayAdapter.cs:93-95` |
| VNPay | `vnp_PayDate` (GMT+7 → UTC) | ✅ `VnPayGatewayAdapter.cs:58` |
| **ZaloPay** | **`server_time` (ms epoch)** | ❌ **`ZaloPayGatewayAdapter.cs:119-138` không gán `PaidAt`** |

**Cổng CÓ gửi mốc, và ta CÓ đọc nó ra — chỉ là không dùng.** `ZaloPayCallbackData.cs:54-56` khai
báo `ServerTime` với chú thích *"Thời điểm cổng ghi nhận kết quả (epoch mili giây)"*;
`grep -rn "ServerTime" backend/SmartBus.Api` → **đúng một kết quả, chính dòng khai báo. Không nơi
nào đọc.** Khi ấy `PaymentSettlementService.cs:70` rơi về nhánh dự phòng:

```csharp
payment.PaidAt = callback.PaidAt ?? DateTime.UtcNow;
```

**Hệ quả.** Cột `PaidAt` là thứ **job đối soát đem so với sao kê cổng**. Ghi giờ hệ thống thay vì
giờ cổng làm lệch mốc đối chiếu, và mốc ấy **không tái lập được** — chạy lại job trên dữ liệu khác
cho ra kết quả khác. Không mất tiền, không vỡ luồng; nặng hơn mức "hiển thị" vì đụng dữ liệu đối soát.

**Các bước tái hiện** — có ca test chạy được, không phải chỉ đọc code:

```
dotnet test backend/SmartBus.Tests -c Release \
  --filter "FullyQualifiedName~ZaloPay_co_gui_moc_thu_tien_ma_adapter_khong_doc"
```

Ca test ghim **cả hai đầu**: mốc cổng gửi trong vector (`server_time = 1791621120000`) **đúng bằng**
`2026-10-10T08:32:00Z` — chính mốc mà VNPay đưa vào `PaidAt` — mà `tuAdapter.PaidAt` vẫn `null`, và
dòng trong CSDL mang mốc **lớn hơn** mốc cổng. Ba dòng chốt lại điều đó:

- `ZaloPayGatewayAdapter.cs:119-138` — không có dòng gán `PaidAt`;
- `ZaloPayCallbackData.cs:54-56` — `ServerTime` có sẵn;
- `grep -rn "ServerTime" backend/SmartBus.Api` → 1 kết quả (khai báo), 0 chỗ đọc.

**Việc cần làm:** sửa `ZaloPayGatewayAdapter.VerifyCallback` để đọc `server_time`, cùng cách
`MoMoGatewayAdapter` đang làm. Sửa xong thì ca test trên **đỏ** — cập nhật ca đó cùng lúc.

### T3 — 🟡 Thấp — `package-lock.json` ở gốc repo (lần thứ 7)

Y nguyên như sáu báo cáo trước, không có gì mới:

```
$ ls -la package-lock.json
-rw-r--r-- 1 ASUS 197121 98 Sep 26 16:42 package-lock.json
$ git check-ignore -v package-lock.json
(KHONG bi .gitignore chan)
```

98 byte, ngày 26/09/2026, `git status` xếp vào nhóm chưa theo dõi. File `package.json` thật của dự
án nằm ở `frontend/`, nên file ở gốc là **rác còn lại của một lần chạy npm nhầm chỗ**. `.gitignore`
**không chặn** nó, nên `git add .` là nó vào commit. Không phải file của người kiểm thử →
không tự xoá, không tự thêm dòng vào `.gitignore`.

---

## 4. Đã kiểm và ĐẠT

24 ca trong `backend/SmartBus.Tests/PaymentSandboxCrossLayerTests.cs`, tất cả xanh. Không ca nào
gọi mạng; không ca nào sửa code nghiệp vụ.

| # | Nhóm | Nội dung | Số ca |
|---|---|---|---|
| 1 | Mặc định sandbox | Chỉ bơm khoá, **không** bơm địa chỉ → cả ba cổng rơi về hằng sandbox (`test-payment.momo.vn`, `sb-openapi.zalopay.vn`, `sandbox.vnpayment.vn`) | 1 |
| 2 | Mặc định sandbox | VNPay ký URL tại chỗ nên chứng minh được tới chuỗi trả về: URL **nằm trên host sandbox** và **không** ghim kênh | 1 |
| 3 | Mặc định sandbox | `BankCard` ghim `vnp_BankCode=VNBANK` còn `VNPay` để trống — khác biệt **duy nhất** giữa hai mã, chứng minh được offline | 1 |
| 4 | **Xuyên tầng** | Payload sandbox → adapter → **chốt thẳng vào CSDL**: `SettledNow=true`, `Status=Success`, `ShouldIssueTickets=true`, `GatewayTransactionId` khớp, `PaidAt` **đúng mốc cổng gửi** | 3 |
| 5 | **Chống trừ hai lần** | Gửi lại y nguyên payload → `SettledNow=false`, `PaidAt` và `GatewayTransactionId` **không đổi** | 3 |
| 6 | Mốc thu tiền | ZaloPay: mốc cổng gửi có trong payload mà adapter không đọc (bằng chứng cho **T2**) | 1 |
| 7 | Chữ ký hỏng | Sửa số tiền, giữ chữ ký → adapter từ chối → bước chốt trả `Invalid`, dòng **còn nguyên `Pending`** | 3 |
| 8 | Chữ ký hỏng | Băm một ký tự hex của chữ ký (vẫn đúng hình dạng, đúng độ dài) → adapter từ chối | 3 |
| 9 | Khoá sai | ZaloPay ký bằng `Key1` (khoá chiều **ta gửi đi**) thay vì `Key2` → bị từ chối | 1 |
| 10 | Lệch tiền | Chữ ký **hợp lệ** nhưng bản ghi sai số tiền → `NotFound`, dòng còn `Pending` | 1 |
| 11 | **Cổng không lẫn nhau** | Vector VNPay và ZaloPay **trùng nhau cả mã đơn lẫn số tiền** — callback ZaloPay hợp lệ vẫn không chốt được giao dịch VNPay | 1 |
| 12 | **Cổng không lẫn nhau** | Callback VNPay không chốt được giao dịch `BankCard` (hai mã dùng **chung** hình dạng callback) | 1 |
| 13 | Khuôn seam | Cả **4** mã cổng: `ProviderCode` khớp; 10 loại dữ liệu rác **không làm ném** và **không bao giờ** ra `IsValid=true`, cũng không bao giờ `Succeeded=true` khi `IsValid=false` | 4 |

**Vì sao ca 11 không dựng tình huống giả:** vector VNPay và ZaloPay trong repo **trùng nhau thật** —
cùng `PaymentCode = PM-8f3a2c1d`, cùng `Amount = 175000`. Ca test khẳng định lại chính điều đó
(`Assert.Equal(vnPay.PaymentCode, zaloPay.PaymentCode)`) trước khi dùng, nên nếu sau này ai đổi
vector thì ca test gãy ngay chứ không âm thầm mất giá trị.

**Vì sao bước chốt phải đi qua scope DI riêng cho mỗi lượt:** dùng lại một scope cho hai lượt là đi
đường tắt qua change tracker của `AppDbContext` — lượt hai sẽ thấy entity **đã tracked** rồi kết luận
`SettledNow=false` mà chưa từng đọc lại CSDL. Mỗi lượt chốt ở đây là một `CreateScope()` mới, đúng
như mỗi lần cổng gọi về là một request thật.

---

## 5. Việc chuyển cho ai

| Việc | Người nhận | Căn cứ |
|---|---|---|
| Dựng `PaymentsController` + 6 endpoint của hợp đồng (**T1**) | Trần Trung Hiếu (chủ API) | `docs/api-contract.md:3223-3228` |
| Endpoint `GET /payments/vnpay/callback` (tách riêng, GET + query string) | Nguyễn Duy Kiên | `docs/api-contract.md:3226` |
| Sửa dòng `:3177` — migration đã xong, câu *"sẽ có khi migration xong"* hết đúng | Trần Trung Hiếu | `docs/api-contract.md:3177` |
| Đọc `server_time` trong adapter ZaloPay (**T2**) | Nguyễn Duy Kiên (tác giả adapter) | `ZaloPayGatewayAdapter.cs:119-138` |
| Xoá / chặn `package-lock.json` ở gốc repo (**T3**) | Phùng Duy Hoàng (chủ `.gitignore`) | `git check-ignore` không khớp |
| Endpoint phải rẽ nhánh theo `SettledNow`, **không** theo `ShouldIssueTickets` | Trần Trung Hiếu | ghi chú cuối **T1** |

---

## 6. Còn lại — chưa kiểm được

| Hạng mục | Vì sao |
|---|---|
| **Sáu endpoint thanh toán** | Chưa tồn tại (**T1**). Không có gì để gọi. |
| **Chạm sandbox MoMo và ZaloPay thật** | Nhóm **chưa có khoá đối tác**; repo PUBLIC, CI không secret (luật 2). |
| **Chạm sandbox VNPay** | VNPay không có API server-to-server — không có gì để chạm. Bộ khoá sandbox công khai chỉ đủ để ký/kiểm chữ ký, và việc đó đã kiểm. |
| **`InitiateAsync` của MoMo và ZaloPay** | Có gọi HTTP thật (bộ test cũ dùng `FakeHandler` chặn lại). Đã kiểm mặc định địa chỉ ở tầng cấu hình, chưa kiểm tới đầu dây bên kia. |
| **Nhánh đua thật (`xmin`)** | Chỉ tồn tại ở tầng PostgreSQL; InMemory **không cưỡng chế** concurrency token — `PaymentSettlementServiceTests` đã ghi rõ ở đầu file. |
| **Phát hành vé sau khi chốt** | Chưa có người viết (`ShouldIssueTickets` mới chỉ là cờ gợi ý). |

---

## 7. Phụ lục — lệnh chạy lại mọi bằng chứng

```bash
# Build sạch
dotnet build backend/SmartBus.sln -c Release

# Toàn bộ bài mới (24 ca)
dotnet test backend/SmartBus.sln -c Release \
  --filter "FullyQualifiedName~PaymentSandboxCrossLayerTests"

# Toàn bộ test — kỳ vọng 1145/1145
dotnet test backend/SmartBus.sln -c Release

# Nền không có bài mới — kỳ vọng 1121/1121
dotnet test backend/SmartBus.sln -c Release \
  --filter "FullyQualifiedName!~PaymentSandboxCrossLayerTests"

# Bằng chứng đồ thị cho T1 (chú ý risk: UNKNOWN ở ứng viên thứ hai)
node .gitnexus/run.cjs impact "SettleAsync" --direction upstream --repo .

# Xác minh chỗ đồ thị trả UNKNOWN — phải thấy chỉ có Program.cs và test
grep -rn "IPaymentSettlementService\|IPaymentGatewayResolver" backend/

# T1 — không có controller thanh toán
ls backend/SmartBus.Api/Controllers/ | grep -i pay

# T1 — 6 endpoint còn nguyên trạng thái chưa dựng
grep -n "Chờ bảng\|Chưa làm" docs/api-contract.md | sed -n '1,12p'

# T2 — ca test chứng minh adapter ZaloPay bỏ mất mốc thu tiền
dotnet test backend/SmartBus.sln -c Release \
  --filter "FullyQualifiedName~ZaloPay_co_gui_moc_thu_tien_ma_adapter_khong_doc"

# T2 — ServerTime khai báo nhưng không nơi nào đọc
grep -rn "ServerTime" backend/SmartBus.Api

# T3 — package-lock.json ở gốc repo, .gitignore không chặn
ls -la package-lock.json && git check-ignore -v package-lock.json
```

---

> **Về vai trò.** Báo cáo này do **người kiểm thử** viết (quy ước 2.4 — Giàng A Vàng). Không dòng
> code nghiệp vụ nào bị sửa để test xanh: T1, T2, T3 đều là **báo cáo cho người viết code**, không
> phải chỗ người kiểm thử tự sửa. Ca test ở mục 4 ghim **hành vi đang có**; ca số 6 cố ý ghim cả
> một hành vi **chưa đúng** để T2 có bằng chứng chạy lại được thay vì một câu khẳng định suông.
