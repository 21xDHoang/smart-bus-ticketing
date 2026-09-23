import { useState } from 'react';
import { Form, Input, Button, Checkbox, message } from 'antd';
import { PhoneOutlined, LockOutlined } from '@ant-design/icons';
import authApi, { decodeAccessToken } from '../../api/authApi';
import type { DecodedUser } from '../../api/authApi';
import type { AppError } from '../../api/axiosClient';

interface LoginFormValues {
  phoneNumber: string;
  password: string;
  remember?: boolean;
}

interface LoginFormProps {
  onSuccess?: (user: DecodedUser) => void;
}

const LoginForm = ({ onSuccess }: LoginFormProps) => {
  const [form] = Form.useForm<LoginFormValues>();
  const [loading, setLoading] = useState(false);

  const onFinish = async (values: LoginFormValues) => {
    setLoading(true);
    try {
      // Backend yêu cầu đăng nhập bằng Số điện thoại + Mật khẩu.
      const response = await authApi.login({
        phoneNumber: values.phoneNumber,
        password: values.password,
      });

      // Lưu cặp token vào localStorage.
      localStorage.setItem('access_token', response.accessToken);
      localStorage.setItem('refresh_token', response.refreshToken);

      // Thông tin người dùng nằm trong payload JWT (backend chưa có API profile).
      const user = decodeAccessToken(response.accessToken);

      message.success('Đăng nhập thành công! Chào mừng bạn trở lại.');

      if (onSuccess && user) onSuccess(user);
    } catch (err) {
      const appError = err as AppError;

      // Backend trả lỗi theo từng trường → gắn trực tiếp vào ô input tương ứng.
      if (appError.errors) {
        form.setFields(
          Object.entries(appError.errors).map(([name, fieldErrors]) => ({
            name: name as keyof LoginFormValues,
            errors: fieldErrors,
          })),
        );
      } else {
        message.error(appError.customMessage || 'Đăng nhập thất bại!');
      }
    } finally {
      setLoading(false);
    }
  };

  return (
    <Form
      form={form}
      name="login_form"
      layout="vertical"
      initialValues={{ remember: true }}
      onFinish={onFinish}
      size="large"
    >
      <Form.Item
        name="phoneNumber"
        rules={[
          { required: true, message: 'Vui lòng nhập Số điện thoại!' },
          { pattern: /^0[35789][0-9]{8}$/, message: 'Số điện thoại không hợp lệ!' },
        ]}
      >
        <Input
          prefix={<PhoneOutlined style={{ color: '#00b4d8' }} />}
          placeholder="Số điện thoại"
          maxLength={10}
        />
      </Form.Item>

      <Form.Item
        name="password"
        rules={[
          { required: true, message: 'Vui lòng nhập mật khẩu!' },
          { min: 6, message: 'Mật khẩu phải từ 6 ký tự trở lên!' },
        ]}
      >
        <Input.Password
          prefix={<LockOutlined style={{ color: '#00b4d8' }} />}
          placeholder="Mật khẩu"
        />
      </Form.Item>

      <Form.Item>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <Form.Item name="remember" valuePropName="checked" noStyle>
            <Checkbox>Ghi nhớ đăng nhập</Checkbox>
          </Form.Item>
          <a style={{ color: '#7209b7', fontWeight: 500 }} href="#forgot">
            Quên mật khẩu?
          </a>
        </div>
      </Form.Item>

      <Form.Item>
        <Button
          type="primary"
          htmlType="submit"
          block
          loading={loading}
          style={{
            background: 'linear-gradient(135deg, #4cc9f0 0%, #4361ee 100%)',
            border: 'none',
            height: '48px',
            borderRadius: '10px',
            fontWeight: '600',
            fontSize: '16px',
            boxShadow: '0 8px 15px rgba(67, 97, 238, 0.3)',
          }}
        >
          ĐĂNG NHẬP
        </Button>
      </Form.Item>
    </Form>
  );
};

export default LoginForm;
