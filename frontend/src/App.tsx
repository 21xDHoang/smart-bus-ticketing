import { App as AntApp, ConfigProvider } from 'antd';
// Locale tiếng Việt cho các chuỗi dựng sẵn của antd (phân trang, Empty, DatePicker…).
import viVN from 'antd/locale/vi_VN';
import { Navigate, Route, Routes } from 'react-router-dom';
import AuthPage from './pages/AuthPage';
import HomePage from './pages/HomePage';
import NotFoundPage from './pages/NotFoundPage';
import BusManagePage from './pages/BusManagePage';
import StopManagePage from './pages/StopManagePage';
import RouteListPage from './pages/RouteListPage';
import RouteStopsPage from './pages/RouteStopsPage';
import RouteLookupPage from './pages/RouteLookupPage';
import TripSearchResultPage from './pages/TripSearchResultPage';
import SeatMapPage from './pages/SeatMapPage';
import PaymentMethodPage from './pages/PaymentMethodPage';
import PaymentWaitingPage from './pages/PaymentWaitingPage';
import ProfilePage from './pages/ProfilePage';
import TripListByDayPage from './pages/TripListByDayPage';
import TripSchedulePage from './pages/TripSchedulePage';
import TripAssignmentPage from './pages/TripAssignmentPage';
import TripFrequencyPage from './pages/TripFrequencyPage';
import MyFeedbackPage from './pages/MyFeedbackPage';
import FeedbackSubmitPage from './pages/FeedbackSubmitPage';
import MonthlyPassRegistrationPage from './pages/MonthlyPassRegistrationPage';
import MyMonthlyPassPage from './pages/MyMonthlyPassPage';
import MyRefundsPage from './pages/MyRefundsPage';
import MyTicketRequestsPage from './pages/MyTicketRequestsPage';
import MyTicketsPage from './pages/MyTicketsPage';
import TicketQrPage from './pages/TicketQrPage';
import TicketChangeRequestPage from './pages/TicketChangeRequestPage';
import TicketScanPage from './pages/TicketScanPage';
import TripPassengerListPage from './pages/TripPassengerListPage';
import FareConfigPage from './pages/FareConfigPage';
import SeatLayoutConfigPage from './pages/SeatLayoutConfigPage';
import VoucherManagePage from './pages/VoucherManagePage';
import VoucherAvailablePage from './pages/VoucherAvailablePage';
import AdminUserListPage from './pages/AdminUserListPage';
import AuditLogPage from './pages/AuditLogPage';
import AdminFeedbackPage from './pages/AdminFeedbackPage';
import AdminRefundPage from './pages/AdminRefundPage';
import AdminTicketRequestPage from './pages/AdminTicketRequestPage';
import NotificationTemplateManagePage from './pages/NotificationTemplateManagePage';
import FillRateReportPage from './pages/FillRateReportPage';
import RevenueDashboardPage from './pages/RevenueDashboardPage';
import OverloadAlertPage from './pages/OverloadAlertPage';
import TicketValidationStatsPage from './pages/TicketValidationStatsPage';
import RevenueByRoutePage from './pages/RevenueByRoutePage';
import NotificationsPage from './pages/NotificationsPage';
import NotificationDetailPage from './pages/NotificationDetailPage';
import NotificationSettingsPage from './pages/NotificationSettingsPage';
import ReportTemplateConfigPage from './pages/ReportTemplateConfigPage';
import RouteGuard from './components/RouteGuard';
import { AppLayout } from './components/layout';
import { buildTheme } from './components/ui/theme';
import { useColorMode } from './components/ui/colorMode';
import { useAuth } from './contexts';
import 'antd/dist/reset.css';

// App giờ chỉ còn hai việc: dựng theme + locale, và khai báo bảng đường dẫn. Toàn bộ khung
// (header, menu theo vai trò, sider/drawer, tab đáy, footer, breadcrumb, cuộn-đầu-trang) nằm
// ở components/layout/* — sửa khung thì sửa ở đó, không thêm vào file này.
function App() {
  // Trạng thái đăng nhập do AuthProvider giữ; App chỉ đọc ra để chọn nhánh hiển thị.
  const { user } = useAuth();
  // Chế độ sáng/tối — store nhỏ ở components/ui/colorMode.ts, đổi là ConfigProvider đổi theme.
  const { mode } = useColorMode();

  return (
    <ConfigProvider theme={buildTheme(mode)} locale={viVN}>
      {/* App của antd, dạng component={false} — không thêm thẻ nào vào DOM, chỉ dựng sẵn
          context message/modal cho các màn chuyển dần sang App.useApp() ở đợt sau. */}
      <AntApp component={false}>
        {!user ? (
          // NẾU CHƯA ĐĂNG NHẬP: Hiển thị Màn hình Auth (Login / Register) cho mọi đường dẫn —
          // đăng nhập là cổng chung, không phải một route riêng.
          <AuthPage />
        ) : (
          // NẾU ĐÃ ĐĂNG NHẬP: khung điều hướng dùng chung (components/layout/AppLayout) bọc
          // bảng đường dẫn bên dưới.
          <AppLayout>
            <Routes>
              <Route path="/" element={<HomePage />} />
              {/* /login không có màn riêng: chưa đăng nhập thì mọi đường dẫn đều ra màn đăng
                  nhập, đã đăng nhập thì đưa về trang chủ — trước đây RouteGuard trỏ tới đây
                  như một đường dẫn chết. */}
              <Route path="/login" element={<Navigate to="/" replace />} />
              {/* /profile là trang cá nhân — ai đã đăng nhập đều vào được, không cần
                  giới hạn vai trò như các màn hình quản trị phía dưới. */}
              <Route path="/profile" element={<ProfilePage />} />
              {/* /route-lookup là màn hình tra cứu tuyến (US 1) — mọi người đã đăng nhập
                  đều dùng được, không giới hạn vai trò như các màn hình quản trị. */}
              <Route path="/route-lookup" element={<RouteLookupPage />} />
              {/* /trip-results là màn hình kết quả tìm kiếm chuyến (US 1, Sprint 2) — danh
                  sách chuyến + sắp xếp theo giờ/giá. Cũng dành cho mọi người đã đăng nhập,
                  không giới hạn vai trò như các màn hình quản trị. */}
              <Route path="/trip-results" element={<TripSearchResultPage />} />
              {/* /seat-map là màn hình sơ đồ ghế (US 2, Sprint 3 — task của Nguyễn Đình Băng):
                  vẽ dàn ghế trực quan và cho chọn nhiều ghế trống. Là bước tiếp của luồng tra
                  cứu tuyến → kết quả tìm kiếm chuyến, mọi người đã đăng nhập đều dùng được.
                  Endpoint sơ đồ ghế chưa có nên màn hình đang chạy dữ liệu giả (seatMapApi.ts). */}
              <Route path="/seat-map" element={<SeatMapPage />} />
              {/* /payment là màn hình chọn phương thức thanh toán (US 6, Sprint 4, task của
                  Dương Thị Hạnh) — bước tiếp theo của luồng tra cứu tuyến → kết quả tìm kiếm
                  chuyến → sơ đồ ghế, mọi người đã đăng nhập đều dùng được. Backend thanh toán
                  chưa có nên màn hình đang chạy dữ liệu giả (paymentApi.ts). Dòng route này do
                  Dương Thị Hạnh thêm hộ: màn hình cần đường vào, mà App.tsx là file dùng chung
                  §E1 (của Nguyễn Đình Băng) — đã ghi rõ trong PR để Băng xem. */}
              <Route path="/payment" element={<PaymentMethodPage />} />
              {/* /payment-waiting là màn hình chờ kết quả thanh toán + xử lý timeout (US 6,
                  Sprint 3 — task của Nguyễn Đình Băng): poll trạng thái giao dịch sau khi
                  khách quay về từ cổng thanh toán, hết hạn chờ thì báo timeout. Là bước tiếp
                  của luồng chọn ghế → thanh toán, mọi người đã đăng nhập đều dùng được.
                  Endpoint thanh toán chưa có nên màn hình đang chạy dữ liệu giả (paymentApi.ts). */}
              <Route path="/payment-waiting" element={<PaymentWaitingPage />} />
              {/* /my-feedback là màn hình danh sách phản ánh của tôi (US 24, Sprint 2) —
                  mọi người đã đăng nhập đều dùng được, không giới hạn vai trò như các màn
                  hình quản trị. */}
              <Route path="/my-feedback" element={<MyFeedbackPage />} />
              {/* /stops là màn hình quản trị — chỉ Admin và Manager vào được
                  (docs/api-contract.md). Vai trò khác nhận trang 403. */}
              <Route
                path="/stops"
                element={
                  <RouteGuard allowedRoles={['Admin', 'Manager']}>
                    <StopManagePage />
                  </RouteGuard>
                }
              />
              {/* /buses là màn hình quản lý đội xe (US 14) — chỉ Admin và Manager vào được
                  (docs/api-contract.md). Vai trò khác nhận trang 403. */}
              <Route
                path="/buses"
                element={
                  <RouteGuard allowedRoles={['Admin', 'Manager']}>
                    <BusManagePage />
                  </RouteGuard>
                }
              />
              {/* /routes cũng là màn hình quản trị — chỉ Admin và Manager vào được
                  (docs/api-contract.md). Vai trò khác nhận trang 403. */}
              <Route
                path="/routes"
                element={
                  <RouteGuard allowedRoles={['Admin', 'Manager']}>
                    <RouteListPage />
                  </RouteGuard>
                }
              />
              {/* /route-stops cũng là màn hình quản trị — chỉ Admin và Manager vào được
                  (docs/api-contract.md). Vai trò khác nhận trang 403. */}
              <Route
                path="/route-stops"
                element={
                  <RouteGuard allowedRoles={['Admin', 'Manager']}>
                    <RouteStopsPage />
                  </RouteGuard>
                }
              />
              {/* /trips-by-day là màn hình danh sách chuyến theo ngày (US 13) — chỉ Admin
                  và Manager vào được (docs/api-contract.md). Vai trò khác nhận trang 403. */}
              <Route
                path="/trips-by-day"
                element={
                  <RouteGuard allowedRoles={['Admin', 'Manager']}>
                    <TripListByDayPage />
                  </RouteGuard>
                }
              />
              {/* /trip-schedule là màn hình quản lý lịch trình (US 13) — chỉ Admin
                  và Manager vào được (docs/api-contract.md). Vai trò khác nhận trang 403. */}
              <Route
                path="/trip-schedule"
                element={
                  <RouteGuard allowedRoles={['Admin', 'Manager']}>
                    <TripSchedulePage />
                  </RouteGuard>
                }
              />
              {/* /trip-assignment là màn hình phân công điều xe theo chuyến (US 14) — chỉ Admin
                  và Manager vào được (docs/api-contract.md). Vai trò khác nhận trang 403. */}
              <Route
                path="/trip-assignment"
                element={
                  <RouteGuard allowedRoles={['Admin', 'Manager']}>
                    <TripAssignmentPage />
                  </RouteGuard>
                }
              />
              {/* /trip-frequency là màn hình cấu hình tần suất chạy xe theo khung giờ
                  trong ngày (US 13, task của Hoàng Văn Thịnh) — chỉ Admin và Manager vào
                  được (docs/api-contract.md). Vai trò khác nhận trang 403. */}
              <Route
                path="/trip-frequency"
                element={
                  <RouteGuard allowedRoles={['Admin', 'Manager']}>
                    <TripFrequencyPage />
                  </RouteGuard>
                }
              />
              {/* /monthly-passes là màn hình đăng ký vé tháng (US 10) — mọi người đã đăng
                  nhập đều dùng được. Backend đăng ký vé tháng chưa có nên màn hình đang
                  chạy dữ liệu giả (xem monthlyPassApi.ts). */}
              <Route path="/monthly-passes" element={<MonthlyPassRegistrationPage />} />
              {/* /my-monthly-passes là màn hình quản lý vé tháng của tôi + nút gia hạn
                  (US 16, task Sprint 2 dòng 45 của Hoàng Văn Thịnh) — mọi người đã đăng nhập
                  đều dùng được, mỗi người chỉ thấy vé của chính mình (backend lọc theo token).
                  Dòng route này do Hoàng Văn Thịnh thêm hộ: màn hình cần đường vào, mà App.tsx
                  là file dùng chung §E1 (của Nguyễn Đình Băng) — đã ghi rõ trong PR để Băng xem. */}
              <Route path="/my-monthly-passes" element={<MyMonthlyPassPage />} />
              {/* /my-tickets là màn hình "Vé của tôi" — vé điện tử (US 4, Sprint 3, task của
                  Dương Thị Hạnh), mọi người đã đăng nhập đều dùng được, mỗi người chỉ thấy vé
                  của chính mình. Backend Tickets chưa có nên màn hình đang chạy dữ liệu giả
                  (xem ticketApi.ts). Dòng route này do Dương Thị Hạnh thêm hộ: màn hình cần
                  đường vào, mà App.tsx là file dùng chung §E1 (của Nguyễn Đình Băng) — đã ghi
                  rõ trong PR để Băng xem. */}
              <Route path="/my-tickets" element={<MyTicketsPage />} />
              {/* /my-tickets/:ticketId/qr là màn hình hiển thị mã QR vé + tăng sáng màn hình
                  khi quét (US 4, Sprint 3 — task của Nguyễn Đình Băng): phóng to mã QR của một
                  vé trên nền trắng để nhân viên soát vé quét khi khách lên xe, kèm chế độ toàn
                  màn hình + giữ màn hình sáng trong lúc quét. Vào từ nút "Xem mã QR" trên màn
                  "Vé của tôi", mọi người đã đăng nhập đều dùng được. Backend Tickets chưa có
                  nên màn hình đang chạy dữ liệu giả (xem ticketApi.ts). */}
              <Route path="/my-tickets/:ticketId/qr" element={<TicketQrPage />} />
              {/* /my-ticket-requests là màn hình theo dõi trạng thái yêu cầu hủy/đổi vé
                  (US 5, Sprint 4, task của Nguyễn Đình Băng) — danh sách yêu cầu hủy vé /
                  đổi chuyến của chính người dùng kèm trạng thái xử lý của nhà xe. Mọi người
                  đã đăng nhập đều dùng được, mỗi người chỉ thấy yêu cầu của chính mình.
                  Backend TicketChanges / CancellationRequests chưa có nên màn hình đang chạy
                  dữ liệu giả (ticketRequestApi.ts). */}
              <Route path="/my-ticket-requests" element={<MyTicketRequestsPage />} />
              {/* /my-refunds là màn hình theo dõi trạng thái hoàn tiền của hành khách (US 8
                  "Hoàn tiền tự động", Sprint 4, task của Dương Thị Hạnh) — danh sách các khoản
                  hoàn tiền của chính người dùng kèm trạng thái xử lý. Chỉ XEM, không có nút thử
                  lại (việc thử lại khoản lỗi thuộc màn quản trị /admin-refunds của Băng). Mọi
                  người đã đăng nhập đều dùng được, mỗi người chỉ thấy khoản hoàn của chính mình.
                  Backend Refunds chưa có nên màn hình đang chạy dữ liệu giả (myRefundApi.ts).
                  Dòng route này do Dương Thị Hạnh thêm hộ: màn hình cần đường vào, mà App.tsx
                  là file dùng chung §E1 (của Nguyễn Đình Băng) — đã ghi rõ trong PR để Băng xem. */}
              <Route path="/my-refunds" element={<MyRefundsPage />} />
              {/* /ticket-scan là màn hình soát vé bằng camera (US 15, Sprint 4 — task "Màn hình
                  quét QR bằng camera thiết bị di động" của Nguyễn Đình Băng): dành cho TÀI XẾ,
                  bật camera sau quét mã QR trên vé để kiểm tra hợp lệ. Vai trò khác nhận 403.
                  Backend soát vé chưa có nên phần kiểm tra chạy dữ liệu giả (ticketValidationApi.ts),
                  phần quét camera là thật (useQrScanner + jsQR). */}
              <Route
                path="/ticket-scan"
                element={
                  <RouteGuard allowedRoles={['Driver']}>
                    <TicketScanPage />
                  </RouteGuard>
                }
              />
              {/* /trip-passengers là màn hình danh sách hành khách đã lên xe theo chuyến
                  (US 15, Sprint 4, dòng 22, task của Dương Thị Hạnh): tài xế chọn chuyến đang
                  chạy rồi xem ai đã được soát vé hợp lệ và lên xe — màn đi cùng "Soát vé".
                  Backend endpoint này chưa có nên màn hình đang chạy dữ liệu giả
                  (tripPassengerListApi.ts). Dòng route này do Dương Thị Hạnh thêm hộ: màn hình
                  cần đường vào, mà App.tsx là file dùng chung §E1 (của Nguyễn Đình Băng) — đã
                  ghi rõ trong PR để Băng xem. */}
              <Route
                path="/trip-passengers"
                element={
                  <RouteGuard allowedRoles={['Driver']}>
                    <TripPassengerListPage />
                  </RouteGuard>
                }
              />
              {/* /my-tickets/:ticketId/request là màn hình gửi yêu cầu huỷ/đổi vé (US 5, Sprint 4 —
                  task của Dương Thị Hạnh): hành khách chọn huỷ vé hay đổi chuyến, viết lý do rồi
                  gửi yêu cầu để nhà xe xem xét. Vào từ nút "Huỷ / Đổi vé" trên màn "Vé của tôi",
                  mọi người đã đăng nhập đều dùng được. Backend yêu cầu huỷ/đổi chưa có nên màn
                  hình đang chạy dữ liệu giả (xem ticketChangeRequestApi.ts). Dòng route này do Dương
                  Thị Hạnh thêm hộ: màn hình cần đường vào, mà App.tsx là file dùng chung §E1
                  (của Nguyễn Đình Băng) — đã ghi rõ trong PR để Băng xem. */}
              <Route path="/my-tickets/:ticketId/request" element={<TicketChangeRequestPage />} />
              {/* /feedback-submit là màn hình gửi phản ánh (US 24, task của Dương Thị Hạnh) —
                  mọi người đã đăng nhập đều dùng được. Backend gửi phản ánh chưa có nên màn
                  hình đang chạy dữ liệu giả (xem feedbackSubmitApi.ts). */}
              <Route path="/feedback-submit" element={<FeedbackSubmitPage />} />
              {/* /fares là màn hình cấu hình giá vé theo tuyến — chỉ Admin và Manager vào
                  được (docs/api-contract.md). Vai trò khác nhận trang 403. */}
              <Route
                path="/fares"
                element={
                  <RouteGuard allowedRoles={['Admin', 'Manager']}>
                    <FareConfigPage />
                  </RouteGuard>
                }
              />
              {/* /seat-layouts là màn hình Admin cấu hình sơ đồ ghế theo loại xe (US 2,
                  Sprint 3, task của Dương Thị Hạnh) — chỉ Admin và Manager vào được (cùng
                  nhóm các màn cấu hình dữ liệu). Backend SeatLayouts chưa có nên màn hình
                  đang chạy dữ liệu giả (xem seatLayoutApi.ts). Dòng route này do Dương Thị
                  Hạnh thêm hộ: màn hình cần đường vào, mà App.tsx là file dùng chung §E1
                  (của Nguyễn Đình Băng) — đã ghi rõ trong PR để Băng xem. */}
              <Route
                path="/seat-layouts"
                element={
                  <RouteGuard allowedRoles={['Admin', 'Manager']}>
                    <SeatLayoutConfigPage />
                  </RouteGuard>
                }
              />
              {/* /vouchers/available là màn hình danh sách voucher đang khả dụng cho hành khách
                  (US 18, Sprint 3 — task của Nguyễn Đình Băng): giới thiệu các mã giảm giá mà
                  khách dùng được ngay lúc này. Mọi người đã đăng nhập đều vào được — khách xem
                  khuyến mãi không phải việc chỉ của riêng một vai trò, cùng lối /route-lookup.
                  Backend chưa có endpoint danh sách voucher khả dụng nên màn hình chạy dữ liệu
                  giả (voucherApi.ts). */}
              <Route path="/vouchers/available" element={<VoucherAvailablePage />} />
              {/* /vouchers là màn hình quản lý voucher (US 18, task của Dương Thị Hạnh) —
                  chỉ Admin và Manager vào được (cùng nhóm các màn cấu hình dữ liệu). Backend
                  Vouchers chưa có nên màn hình đang chạy dữ liệu giả (voucherApi.ts). Dòng
                  route này do Dương Thị Hạnh thêm hộ: màn hình cần đường vào, mà App.tsx là
                  file dùng chung §E1 (của Nguyễn Đình Băng) — đã ghi rõ trong PR để Băng xem. */}
              <Route
                path="/vouchers"
                element={
                  <RouteGuard allowedRoles={['Admin', 'Manager']}>
                    <VoucherManagePage />
                  </RouteGuard>
                }
              />
              {/* /admin-users là màn hình quản lý người dùng — chỉ Admin vào được
                  (docs/api-contract.md). Vai trò khác nhận trang 403. */}
              <Route
                path="/admin-users"
                element={
                  <RouteGuard allowedRoles={['Admin']}>
                    <AdminUserListPage />
                  </RouteGuard>
                }
              />
              {/* /audit-logs là màn hình nhật ký kiểm toán — chỉ Admin vào được
                  (docs/api-contract.md). Vai trò khác nhận trang 403. */}
              <Route
                path="/audit-logs"
                element={
                  <RouteGuard allowedRoles={['Admin']}>
                    <AuditLogPage />
                  </RouteGuard>
                }
              />
              {/* /admin-feedbacks là màn hình xử lý phản ánh (US 24, task Sprint 2 dòng 58
                  của Hoàng Văn Thịnh) — nhóm endpoint /admin/feedbacks yêu cầu vai trò Manager
                  hoặc Admin (docs/api-contract.md). Vai trò khác nhận trang 403.
                  Dòng route này do Hoàng Văn Thịnh thêm hộ: màn hình cần đường vào, mà App.tsx
                  là file dùng chung §E1 (của Nguyễn Đình Băng) — đã ghi rõ trong PR để Băng xem. */}
              <Route
                path="/admin-feedbacks"
                element={
                  <RouteGuard allowedRoles={['Admin', 'Manager']}>
                    <AdminFeedbackPage />
                  </RouteGuard>
                }
              />
              {/* /admin-refunds là màn hình Admin quản lý yêu cầu hoàn tiền (US 8 "Hoàn tiền tự
                  động", Sprint 4 — task của Nguyễn Đình Băng): theo dõi mọi khoản hoàn tiền của
                  hệ thống, thử lại lượt thất bại và xuất danh sách để đối soát. Nhóm endpoint
                  /admin/refunds dự kiến yêu cầu vai trò Manager hoặc Admin (chưa chốt trong
                  api-contract.md); vai trò khác nhận 403. Backend Refunds chưa có nên màn hình
                  đang chạy dữ liệu giả (refundAdminApi.ts). */}
              <Route
                path="/admin-refunds"
                element={
                  <RouteGuard allowedRoles={['Admin', 'Manager']}>
                    <AdminRefundPage />
                  </RouteGuard>
                }
              />
              {/* /admin-ticket-requests là màn hình duyệt yêu cầu huỷ/đổi vé (US 5, Sprint 4, task
                  của Dương Thị Hạnh) — hàng đợi các yêu cầu huỷ vé / đổi chuyến cho Admin và
                  Manager duyệt hoặc từ chối (khớp nhóm endpoint PATCH của Hoàng trong docs 32).
                  Backend huỷ/đổi chưa có nên màn hình đang chạy dữ liệu giả
                  (ticketRequestAdminApi.ts). Dòng route này do Dương Thị Hạnh thêm hộ: màn hình
                  cần đường vào, mà App.tsx là file dùng chung §E1 (của Nguyễn Đình Băng) — đã
                  ghi rõ trong PR để Băng xem. */}
              <Route
                path="/admin-ticket-requests"
                element={
                  <RouteGuard allowedRoles={['Admin', 'Manager']}>
                    <AdminTicketRequestPage />
                  </RouteGuard>
                }
              />
              {/* /fill-rate là màn hình thống kê tỷ lệ lấp đầy theo chuyến (US 20 "Tỷ lệ lấp đầy",
                  task của Dương Thị Hạnh) — tỷ lệ khách thực tế trên sức chứa ghế của từng
                  chuyến trong ngày. Báo cáo vận hành nên chỉ Admin và Manager vào được. Backend
                  thống kê tỷ lệ lấp đầy chưa có (dòng 66 của Kiên) nên màn hình đang chạy dữ
                  liệu giả (fillRateReportApi.ts). Dòng route này do Dương Thị Hạnh thêm hộ: màn
                  hình cần đường vào, mà App.tsx là file dùng chung §E1 (của Nguyễn Đình Băng) —
                  đã ghi rõ trong PR để Băng xem. */}
              <Route
                path="/fill-rate"
                element={
                  <RouteGuard allowedRoles={['Admin', 'Manager']}>
                    <FillRateReportPage />
                  </RouteGuard>
                }
              />
              {/* /revenue-dashboard là màn hình dashboard doanh thu (US 19 "Doanh thu", Sprint 5,
                  task của Dương Thị Hạnh): tổng quan doanh thu theo khoảng thời gian — dải thẻ
                  số liệu + biểu đồ theo ngày và theo tuyến. Báo cáo tài chính nên chỉ Admin và
                  Manager vào được. Backend thống kê doanh thu chưa có nên màn hình đang chạy dữ
                  liệu giả (revenueDashboardApi.ts). Dòng route này do Dương Thị Hạnh thêm hộ:
                  màn hình cần đường vào, mà App.tsx là file dùng chung §E1 (của Nguyễn Đình
                  Băng) — đã ghi rõ trong PR để Băng xem. */}
              <Route
                path="/revenue-dashboard"
                element={
                  <RouteGuard allowedRoles={['Admin', 'Manager']}>
                    <RevenueDashboardPage />
                  </RouteGuard>
                }
              />
              {/* /overload-alerts là màn hình cảnh báo các chuyến định kỳ bị quá tải lặp lại
                  (US 20 "Tỷ lệ lấp đầy", Sprint 4, task của Nguyễn Đình Băng). Khác với
                  /fill-rate chỉ xem một ngày: màn này gộp nhiều ngày để bắt chuyến "thường
                  xuyên" quá tải. Báo cáo vận hành nên chỉ Admin và Manager vào được. Backend
                  cảnh báo quá tải chưa có nên màn hình đang chạy dữ liệu giả
                  (overloadAlertApi.ts). */}
              <Route
                path="/overload-alerts"
                element={
                  <RouteGuard allowedRoles={['Admin', 'Manager']}>
                    <OverloadAlertPage />
                  </RouteGuard>
                }
              />
              {/* /ticket-validation-stats là màn hình thống kê lượt soát theo chuyến (US 15,
                  Sprint 4, dòng 24, task của Dương Thị Hạnh): chọn một chuyến rồi xem lượt soát
                  vé phân bố thế nào — tổng / hợp lệ / từng lý do từ chối. Báo cáo vận hành nên
                  chỉ Admin và Manager vào được. Backend endpoint này chưa có nên màn hình đang
                  chạy dữ liệu giả (ticketValidationStatsApi.ts). Dòng route này do Dương Thị
                  Hạnh thêm hộ: màn hình cần đường vào, mà App.tsx là file dùng chung §E1 (của
                  Nguyễn Đình Băng) — đã ghi rõ trong PR để Băng xem. */}
              <Route
                path="/ticket-validation-stats"
                element={
                  <RouteGuard allowedRoles={['Admin', 'Manager']}>
                    <TicketValidationStatsPage />
                  </RouteGuard>
                }
              />
              {/* /revenue-by-route là màn hình bảng chi tiết doanh thu theo tuyến (US 19
                  "Doanh thu", Sprint 4, task của Nguyễn Đình Băng): doanh thu bán vé gom theo
                  từng tuyến, sắp xếp và phân trang. Báo cáo tài chính nên chỉ Admin và Manager
                  vào được. Backend "theo tuyến" chưa có nên màn hình đang chạy dữ liệu giả
                  (revenueByRouteApi.ts). */}
              <Route
                path="/revenue-by-route"
                element={
                  <RouteGuard allowedRoles={['Admin', 'Manager']}>
                    <RevenueByRoutePage />
                  </RouteGuard>
                }
              />
              {/* /notifications là màn hình danh sách thông báo (US 10 "Thông báo trạm", Sprint 5,
                  task của Dương Thị Hạnh): lịch sử thông báo của chính người dùng, bấm dòng mở
                  màn chi tiết. Mọi người đã đăng nhập đều dùng được, không giới hạn vai trò.
                  Dòng route này do Dương Thị Hạnh thêm hộ: màn hình cần đường vào, mà App.tsx là
                  file dùng chung §E1 (của Nguyễn Đình Băng) — đã ghi rõ trong PR để Băng xem. */}
              <Route path="/notifications" element={<NotificationsPage />} />
              {/* /notifications/:id là màn hình chi tiết thông báo — đọc thẳng từ dòng đã tải về,
                  không có endpoint GET /notifications/{id} (api-contract.md mục /notifications). */}
              <Route path="/notifications/:id" element={<NotificationDetailPage />} />
              {/* /notification-settings là màn Cài đặt thông báo Web Push (US 10, Sprint 4 dòng 42,
                  task của Trần Trung Hiếu): bật/tắt nhận thông báo trên trình duyệt, xem các thiết
                  bị đã đăng ký và gửi tin thử. Mọi người đã đăng nhập đều dùng được. Dòng route
                  này do Trần Trung Hiếu thêm hộ: màn hình cần đường vào, mà App.tsx là file dùng
                  chung §E1 (của Nguyễn Đình Băng) — đã ghi rõ trong PR để Băng xem. */}
              <Route path="/notification-settings" element={<NotificationSettingsPage />} />
              {/* /notification-templates là màn hình Admin quản lý mẫu thông báo (US 10, Sprint 5,
                  dòng 24, task của Dương Thị Hạnh) — soạn câu chữ cho từng loại thông báo; mẫu là
                  phần GHI ĐÈ câu mặc định (docs/37 §2.1), loại chưa soạn hoặc đang tắt vẫn gửi bằng
                  câu mặc định. Chỉ Admin và Manager vào được (cùng nhóm các màn cấu hình dữ liệu).
                  Dòng route này do Dương Thị Hạnh thêm hộ: màn hình cần đường vào, mà App.tsx là file
                  dùng chung §E1 (của Nguyễn Đình Băng) — đã ghi rõ trong PR để Băng xem. */}
              <Route
                path="/notification-templates"
                element={
                  <RouteGuard allowedRoles={['Admin', 'Manager']}>
                    <NotificationTemplateManagePage />
                  </RouteGuard>
                }
              />
              {/* /report-templates là màn hình cấu hình mẫu báo cáo (US 21 "Xuất báo cáo Excel/PDF",
                  Sprint 5, task của Dương Thị Hạnh) — soạn sẵn mẫu báo cáo: chọn loại, cột hiển thị
                  và khoảng thời gian để dùng lại khi xuất file. Cấu hình lưu trong trình duyệt vì
                  chưa có bảng CSDL (reportTemplateApi.ts). Chỉ Admin và Manager vào được. Dòng route
                  này do Dương Thị Hạnh thêm hộ: màn hình cần đường vào, mà App.tsx là file dùng chung
                  §E1 (của Nguyễn Đình Băng) — đã ghi rõ trong PR để Băng xem. */}
              <Route
                path="/report-templates"
                element={
                  <RouteGuard allowedRoles={['Admin', 'Manager']}>
                    <ReportTemplateConfigPage />
                  </RouteGuard>
                }
              />
              {/* Đường dẫn không khớp route nào: hiện màn 404 thật thay vì tự đá về trang chủ. */}
              <Route path="*" element={<NotFoundPage />} />
            </Routes>
          </AppLayout>
        )}
      </AntApp>
    </ConfigProvider>
  );
}

export default App;
