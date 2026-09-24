import { ConfigProvider, Layout, Avatar, Dropdown, message } from 'antd';
import { UserOutlined, LogoutOutlined, SafetyCertificateOutlined } from '@ant-design/icons';
import { NavLink, Navigate, Route, Routes } from 'react-router-dom';
import AuthPage from './pages/AuthPage';
import StopManagePage from './pages/StopManagePage';
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

  // Xử lý Đăng xuất
  const handleLogout = async () => {
    await logout();
    message.info('Đã đăng xuất tài khoản.');
  };

  // Menu Dropdown cho Avatar người dùng khi đã đăng nhập
  const userMenuItems = [
    { key: '1', label: 'Hồ sơ cá nhân', icon: <UserOutlined /> },
    { key: '2', label: 'Vé của tôi', icon: <SafetyCertificateOutlined /> },
    { type: 'divider' as const },
    { key: '3', label: 'Đăng xuất', icon: <LogoutOutlined />, danger: true, onClick: handleLogout },
  ];

  // Các mục điều hướng chính. Route guard (chặn chưa đăng nhập theo vai trò) là task
  // của Thịnh — ở đây chỉ dựng khung điều hướng để các màn hình có chỗ "sống".
  const navItems = [
    { to: '/', label: 'Trang chủ' },
    { to: '/stops', label: 'Trạm dừng' },
  ];

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
                <Route path="/stops" element={<StopManagePage />} />
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
