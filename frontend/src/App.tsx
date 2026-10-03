import { ConfigProvider, Layout, Avatar, Dropdown, message } from 'antd';
import { UserOutlined, LogoutOutlined, SafetyCertificateOutlined } from '@ant-design/icons';
import { NavLink, Navigate, Route, Routes, useNavigate } from 'react-router-dom';
import AuthPage from './pages/AuthPage';
import BusManagePage from './pages/BusManagePage';
import StopManagePage from './pages/StopManagePage';
import RouteListPage from './pages/RouteListPage';
import RouteStopsPage from './pages/RouteStopsPage';
import RouteLookupPage from './pages/RouteLookupPage';
import TripSearchResultPage from './pages/TripSearchResultPage';
import ProfilePage from './pages/ProfilePage';
import TripListByDayPage from './pages/TripListByDayPage';
import TripSchedulePage from './pages/TripSchedulePage';
import TripAssignmentPage from './pages/TripAssignmentPage';
import TripFrequencyPage from './pages/TripFrequencyPage';
import RouteGuard from './components/RouteGuard';
import { useAuth } from './contexts';
import 'antd/dist/reset.css';

const { Header, Content, Footer } = Layout;

// Màn hình chào sau khi đăng nhập — tạm thời, sẽ thay bằng dashboard thật sau.
function HomeContent() {
  const { user } = useAuth();

  return (
    <div
      style={{
        background: '#ffffff',
        padding: '30px',
        borderRadius: '16px',
        boxShadow: '0 4px 12px rgba(0,0,0,0.03)',
        textAlign: 'center',
      }}
    >
      <h2>Xin chào, {user?.fullName || 'Bạn'}! 👋</h2>
      <p>Bạn đã đăng nhập thành công vào Hệ thống Đặt vé Xe Chất lượng cao.</p>
    </div>
  );
}

function App() {
  // Trạng thái đăng nhập do AuthProvider giữ; App chỉ đọc ra để hiển thị.
  const { user, logout } = useAuth();
  const navigate = useNavigate();

  // Xử lý Đăng xuất
  const handleLogout = async () => {
    await logout();
    message.info('Đã đăng xuất tài khoản.');
  };

  // Menu Dropdown cho Avatar người dùng khi đã đăng nhập
  const userMenuItems = [
    { key: '1', label: 'Hồ sơ cá nhân', icon: <UserOutlined />, onClick: () => navigate('/profile') },
    { key: '2', label: 'Vé của tôi', icon: <SafetyCertificateOutlined /> },
    { type: 'divider' as const },
    { key: '3', label: 'Đăng xuất', icon: <LogoutOutlined />, danger: true, onClick: handleLogout },
  ];

  // Các mục điều hướng chính. Mục có `roles` chỉ hiện với vai trò tương ứng — ẩn đi
  // thay vì để người dùng bấm vào rồi nhận trang 403.
  const navItems = [
    { to: '/', label: 'Trang chủ', roles: [] as string[] },
    { to: '/route-lookup', label: 'Tra cứu tuyến', roles: [] as string[] },
    { to: '/routes', label: 'Tuyến đường', roles: ['Admin', 'Manager'] },
    { to: '/stops', label: 'Trạm dừng', roles: ['Admin', 'Manager'] },
    { to: '/buses', label: 'Đội xe', roles: ['Admin', 'Manager'] },
    { to: '/route-stops', label: 'Gán trạm vào tuyến', roles: ['Admin', 'Manager'] },
    { to: '/trips-by-day', label: 'Chuyến theo ngày', roles: ['Admin', 'Manager'] },
    { to: '/trip-schedule', label: 'Lịch trình', roles: ['Admin', 'Manager'] },
    { to: '/trip-frequency', label: 'Tần suất chạy xe', roles: ['Admin', 'Manager'] },
    { to: '/trip-assignment', label: 'Phân công điều xe', roles: ['Admin', 'Manager'] },
  ].filter((item) => item.roles.length === 0 || item.roles.includes(user?.role ?? ''));

  return (
    <ConfigProvider
      theme={{
        token: {
          colorPrimary: '#4361ee',
          borderRadius: 10,
          fontFamily: "'Plus Jakarta Sans', sans-serif",
        },
      }}
    >
      {!user ? (
        // NẾU CHƯA ĐĂNG NHẬP: Hiển thị Màn hình Auth (Login / Register)
        <AuthPage />
      ) : (
        // NẾU ĐÃ ĐĂNG NHẬP: Hiển thị khung chính + điều hướng các màn hình
        <Layout style={{ minHeight: '100vh' }}>
          <Header
            style={{
              display: 'flex',
              alignItems: 'center',
              gap: '24px',
              background: '#ffffff',
              boxShadow: '0 2px 8px rgba(0,0,0,0.06)',
              padding: '0 24px',
              position: 'sticky',
              top: 0,
              zIndex: 10,
            }}
          >
            <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
              <span style={{ fontSize: '28px' }}>🚌</span>
              <span
                style={{
                  fontSize: '20px',
                  fontWeight: 'bold',
                  background: 'linear-gradient(90deg, #4361ee, #f72585)',
                  WebkitBackgroundClip: 'text',
                  WebkitTextFillColor: 'transparent',
                }}
              >
                Smart Bus Ticket
              </span>
            </div>

            <nav style={{ flex: 1, display: 'flex', gap: '8px' }}>
              {navItems.map((item) => (
                <NavLink
                  key={item.to}
                  to={item.to}
                  end={item.to === '/'}
                  style={({ isActive }) => ({
                    padding: '8px 16px',
                    borderRadius: 8,
                    fontWeight: 600,
                    color: isActive ? '#4361ee' : '#64748b',
                    background: isActive ? '#eef1ff' : 'transparent',
                    textDecoration: 'none',
                    transition: 'all 0.2s',
                  })}
                >
                  {item.label}
                </NavLink>
              ))}
            </nav>

            <Dropdown menu={{ items: userMenuItems }} placement="bottomRight">
              <div style={{ cursor: 'pointer', display: 'flex', alignItems: 'center', gap: '10px' }}>
                <Avatar style={{ backgroundColor: '#4361ee' }} icon={<UserOutlined />} />
                <span style={{ fontWeight: 600 }}>
                  {user.fullName || user.phoneNumber || 'Người dùng'}
                </span>
              </div>
            </Dropdown>
          </Header>

          <Content style={{ padding: '24px', background: '#f8fafc' }}>
            <div
              style={{
                background: '#ffffff',
                padding: '24px',
                borderRadius: '16px',
                boxShadow: '0 4px 12px rgba(0,0,0,0.03)',
              }}
            >
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
                <Route path="*" element={<Navigate to="/" replace />} />
              </Routes>
            </div>
          </Content>

          <Footer style={{ textAlign: 'center', color: '#94a3b8' }}>
            Smart Bus Ticket System ©2026 Developed with React & Ant Design
          </Footer>
        </Layout>
      )}
    </ConfigProvider>
  );
}

export default App;
