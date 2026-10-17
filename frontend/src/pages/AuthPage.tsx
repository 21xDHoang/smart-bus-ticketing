import { useState } from 'react';
import { Card, Tabs } from 'antd';
import LoginForm from '../components/Auth/LoginForm';
import RegisterForm from '../components/Auth/RegisterForm';
import { BrandMark, BrandWordmark } from '../components/ui/Brand';
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
      {/* Hoạ tiết đường tuyến ●—○ vẽ bằng CSS (AuthPage.css) — không cần phần tử trang trí. */}
      <Card className="auth-card" variant="borderless">
        <div className="auth-header">
          <div className="auth-brand">
            <BrandMark size={54} />
            <div className="auth-brand-copy">
              <h2 className="auth-title">
                <BrandWordmark fontSize="clamp(19px, 5.4vw, 21px)" />
              </h2>
              {/* Đường tuyến ●—○ nối hai mốc — chi tiết nhận diện, ẩn với trình đọc màn hình. */}
              <svg
                className="auth-route"
                width="196"
                height="10"
                viewBox="0 0 196 10"
                aria-hidden="true"
                focusable="false"
              >
                <path d="M5 5H178" stroke="#9CC5FF" strokeWidth="2" strokeLinecap="round" />
                <circle cx="5" cy="5" r="3" fill="#1677FF" />
                <circle cx="178" cy="5" r="3" fill="#FFFFFF" stroke="#1677FF" strokeWidth="1.6" />
              </svg>
            </div>
          </div>
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
              label: 'Đăng nhập',
              children: <LoginForm onSuccess={onLoginSuccess} />,
            },
            {
              key: 'register',
              label: 'Đăng ký',
              children: <RegisterForm onRegisterSuccess={() => setActiveTab('login')} />,
            },
          ]}
        />
      </Card>
    </div>
  );
};

export default AuthPage;
