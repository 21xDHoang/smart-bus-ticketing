import { useState } from 'react';
import { Card, Tabs } from 'antd';
import LoginForm from '../components/Auth/LoginForm';
import RegisterForm from '../components/Auth/RegisterForm';
import type { DecodedUser } from '../api/authApi';
import './AuthPage.css';

interface AuthPageProps {
  // Không còn bắt buộc: trạng thái đăng nhập nay do AuthProvider giữ, component cha
  // không cần nhận callback nữa. Giữ optional để không phá vỡ chỗ gọi cũ.
  onLoginSuccess?: (user: DecodedUser) => void;
}

const AuthPage = ({ onLoginSuccess }: AuthPageProps) => {
  const [activeTab, setActiveTab] = useState('login');

  return (
    <div className="auth-container">
      {/* Dynamic Background Circles */}
      <div className="circle circle-1"></div>
      <div className="circle circle-2"></div>

      <Card className="auth-card" bordered={false}>
        <div className="auth-header">
          <div className="logo-icon">🚌</div>
          <h2>Smart Bus Ticket</h2>
          <p>Hệ thống đặt vé xe thông minh & tiện lợi</p>
        </div>

        <Tabs
          activeKey={activeTab}
          onChange={(key) => setActiveTab(key)}
          centered
          className="auth-tabs"
          items={[
            {
              key: 'login',
              label: 'Đăng Nhập',
              children: <LoginForm onSuccess={onLoginSuccess} />,
            },
            {
              key: 'register',
              label: 'Đăng Ký',
              children: <RegisterForm onRegisterSuccess={() => setActiveTab('login')} />,
            },
          ]}
        />
      </Card>
    </div>
  );
};

export default AuthPage;
