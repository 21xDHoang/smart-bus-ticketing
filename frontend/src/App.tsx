import { ConfigProvider, Layout, Button, Avatar, Dropdown, message } from 'antd';
import { UserOutlined, LogoutOutlined, SafetyCertificateOutlined } from '@ant-design/icons';
import AuthPage from './pages/AuthPage';
import { useAuth } from './contexts';
import 'antd/dist/reset.css';

const { Header, Content, Footer } = Layout;

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
    {
      key: '1',
      label: 'Hồ sơ cá nhân',
      icon: <UserOutlined />,
    },
    {
      key: '2',
      label: 'Vé của tôi',
      icon: <SafetyCertificateOutlined />,
    },
    {
      type: 'divider' as const,
    },
    {
      key: '3',
      label: 'Đăng xuất',
      icon: <LogoutOutlined />,
      danger: true,
      onClick: handleLogout,
    },
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
        // NẾU ĐÃ ĐĂNG NHẬP: Hiển thị Màn hình chính
        <Layout style={{ minHeight: '100vh' }}>
          <Header
            style={{
              display: 'flex',
              justifyContent: 'space-between',
              alignItems: 'center',
              background: '#ffffff',
              boxShadow: '0 2px 8px rgba(0,0,0,0.06)',
              padding: '0 24px',
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

            <Dropdown menu={{ items: userMenuItems }} placement="bottomRight">
              <div style={{ cursor: 'pointer', display: 'flex', alignItems: 'center', gap: '10px' }}>
                <Avatar style={{ backgroundColor: '#4361ee' }} icon={<UserOutlined />} />
                <span style={{ fontWeight: 600 }}>{user.fullName || user.phoneNumber || 'Người dùng'}</span>
              </div>
            </Dropdown>
          </Header>

          <Content style={{ padding: '24px', background: '#f8fafc' }}>
            <div
              style={{
                background: '#ffffff',
                padding: '30px',
                borderRadius: '16px',
                boxShadow: '0 4px 12px rgba(0,0,0,0.03)',
                textAlign: 'center',
              }}
            >
              <h2>Xin chào, {user.fullName || 'Bạn'}! 👋</h2>
              <p>Bạn đã đăng nhập thành công vào Hệ thống Đặt vé Xe Chất lượng cao.</p>
              <Button
                type="primary"
                size="large"
                style={{
                  background: 'linear-gradient(135deg, #4cc9f0 0%, #4361ee 100%)',
                  border: 'none',
                  marginTop: '15px',
                }}
              >
                Tìm Chuyến Xe Ngay
              </Button>
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
