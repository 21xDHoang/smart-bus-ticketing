import {
  App as AntApp,
  Avatar,
  Button,
  ConfigProvider,
  Dropdown,
  Layout,
  Menu,
  Space,
  Tag,
  message,
} from 'antd';
// Locale tiếng Việt cho các chuỗi dựng sẵn của antd (phân trang, Empty, DatePicker…).
import viVN from 'antd/locale/vi_VN';
import { UserOutlined, LogoutOutlined, SafetyCertificateOutlined } from '@ant-design/icons';
import { Navigate, Route, Routes, useLocation, useNavigate } from 'react-router-dom';
import AuthPage from './pages/AuthPage';
import BusManagePage from './pages/BusManagePage';
import StopManagePage from './pages/StopManagePage';
import RouteListPage from './pages/RouteListPage';
import RouteStopsPage from './pages/RouteStopsPage';
import RouteLookupPage from './pages/RouteLookupPage';
import TripSearchResultPage from './pages/TripSearchResultPage';
import SeatMapPage from './pages/SeatMapPage';
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
import MyTicketsPage from './pages/MyTicketsPage';
import FareConfigPage from './pages/FareConfigPage';
import SeatLayoutConfigPage from './pages/SeatLayoutConfigPage';
import AdminUserListPage from './pages/AdminUserListPage';
import AuditLogPage from './pages/AuditLogPage';
import AdminFeedbackPage from './pages/AdminFeedbackPage';
import RouteGuard from './components/RouteGuard';
import { PageCard, PageHeader } from './components/ui';
import { buildNavItems, navKeyOf, navLeafLinks } from './components/ui/navItems';
import { appTheme, BRAND, SLATE } from './components/ui/theme';
import { useAuth } from './contexts';
import 'antd/dist/reset.css';

const { Header, Content, Footer } = Layout;

// Màn hình chào sau khi đăng nhập — lời chào + lối vào nhanh các màn hình. Chỉ là điều
// hướng tới các màn đã có, không gọi API và không thêm chức năng nào.
function HomeContent() {
  const { user } = useAuth();
  const navigate = useNavigate();
  const quickLinks = navLeafLinks(user?.role ?? null);

  return (
    <div>
      <PageHeader
        title={`Xin chào, ${user?.fullName || 'Bạn'}! 👋`}
        subtitle={
          <Space size={8}>
            <span>Bạn đang đăng nhập với vai trò</span>
            <Tag color="blue" style={{ marginInlineEnd: 0 }}>
              {user?.role ?? '—'}
            </Tag>
          </Space>
        }
      />

      <PageCard title="Lối vào nhanh">
        <Space wrap size={12}>
          {quickLinks.map((link) => (
            <Button key={link.to} onClick={() => navigate(link.to)} style={{ height: 40 }}>
              {link.label}
            </Button>
          ))}
        </Space>
      </PageCard>
    </div>
  );
}

function App() {
  // Trạng thái đăng nhập do AuthProvider giữ; App chỉ đọc ra để hiển thị.
  const { user, logout } = useAuth();
  const navigate = useNavigate();
  const { pathname } = useLocation();

  // Xử lý Đăng xuất
  const handleLogout = async () => {
    await logout();
    message.info('Đã đăng xuất tài khoản.');
  };

  // Menu Dropdown cho Avatar người dùng khi đã đăng nhập
  const userMenuItems = [
    { key: '1', label: 'Hồ sơ cá nhân', icon: <UserOutlined />, onClick: () => navigate('/profile') },
    {
      key: '2',
      label: 'Vé của tôi',
      icon: <SafetyCertificateOutlined />,
      // Mục này trước không có onClick nên bấm không đi đâu cả — nối vào màn vé tháng.
      onClick: () => navigate('/my-monthly-passes'),
    },
    { type: 'divider' as const },
    { key: '3', label: 'Đăng xuất', icon: <LogoutOutlined />, danger: true, onClick: handleLogout },
  ];

  // Các mục điều hướng theo vai trò — cấu hình nhóm đặt ở components/ui/navItems.tsx.
  const menuItems = buildNavItems(user?.role ?? null);
  // Mục cần tô sáng theo URL hiện tại — vào thẳng đường dẫn vẫn sáng đúng mục.
  const selectedNavKey = navKeyOf(pathname);

  return (
    <ConfigProvider theme={appTheme} locale={viVN}>
      {/* App của antd, dạng component={false} — không thêm thẻ nào vào DOM, chỉ dựng sẵn
          context message/modal cho các màn chuyển dần sang App.useApp() ở đợt sau. */}
      <AntApp component={false}>
        {!user ? (
          // NẾU CHƯA ĐĂNG NHẬP: Hiển thị Màn hình Auth (Login / Register)
          <AuthPage />
        ) : (
          // NẾU ĐÃ ĐĂNG NHẬP: Hiển thị khung chính + điều hướng các màn hình
          <Layout style={{ minHeight: '100vh' }}>
            {/* Nền trắng, cao 64px, lề ngang lấy từ component token của Layout trong
                components/ui/theme.ts. Menu ngang tự gom mục thừa vào nút "…" khi hẹp nên
                không cần cho header xuống dòng như bản cũ. */}
            <Header
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: 16,
                boxShadow: '0 2px 8px rgba(0,0,0,0.06)',
                position: 'sticky',
                top: 0,
                zIndex: 10,
              }}
            >
              <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexShrink: 0 }}>
                <span style={{ fontSize: 28 }}>🚌</span>
                <span
                  style={{
                    fontSize: 20,
                    fontWeight: 'bold',
                    whiteSpace: 'nowrap',
                    background: BRAND.gradient,
                    WebkitBackgroundClip: 'text',
                    WebkitTextFillColor: 'transparent',
                  }}
                >
                  Smart Bus Ticket
                </span>
              </div>

              {/* minWidth: 0 là điều kiện để Menu ngang gom mục tràn vào nút "…" —
                  thiếu nó là header lại xuống dòng như bản cũ. */}
              <Menu
                mode="horizontal"
                selectedKeys={[selectedNavKey]}
                items={menuItems}
                onClick={({ key }) => navigate(key)}
                style={{ flex: 1, minWidth: 0, borderBottom: 'none', background: 'transparent' }}
              />

              <Dropdown menu={{ items: userMenuItems }} placement="bottomRight">
                <div
                  style={{
                    cursor: 'pointer',
                    display: 'flex',
                    alignItems: 'center',
                    gap: 10,
                    flexShrink: 0,
                  }}
                >
                  <Avatar style={{ backgroundColor: BRAND.primary }} icon={<UserOutlined />} />
                  <span className="app-user-name" style={{ fontWeight: 600 }}>
                    {user.fullName || user.phoneNumber || 'Người dùng'}
                  </span>
                </div>
              </Dropdown>
            </Header>

            {/* Bỏ lớp thẻ trắng bọc ngoài — mỗi màn tự dựng PageCard của mình, hết cảnh
                thẻ lồng trong thẻ. */}
            <Content style={{ padding: 24, maxWidth: 1440, margin: '0 auto', width: '100%' }}>
              <Routes>
                <Route path="/" element={<HomeContent />} />
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
                <Route path="*" element={<Navigate to="/" replace />} />
              </Routes>
            </Content>

            <Footer style={{ textAlign: 'center', color: SLATE[500] }}>
              Smart Bus Ticket System ©2026 Developed with React & Ant Design
            </Footer>
          </Layout>
        )}
      </AntApp>
    </ConfigProvider>
  );
}

export default App;
