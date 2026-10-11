import { useState } from 'react';
import { Form, Input, Button, Checkbox, message, theme } from 'antd';
import { PhoneOutlined, LockOutlined } from '@ant-design/icons';
import { useAuth } from '../../contexts';
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
  const { login } = useAuth();
  // Icon và link lấy màu theo token — tự đúng ở cả hai chế độ sáng/tối.
  const { token } = theme.useToken();

  const onFinish = async (values: LoginFormValues) => {
    setLoading(true);
    try {
      // AuthContext lo việc gọi API, lưu token và cập nhật trạng thái đăng nhập.
      const user = await login({
        phoneNumber: values.phoneNumber,
        password: values.password,
      });

      message.success('Đăng nhập thành công! Chào mừng bạn trở lại.');

      onSuccess?.(user);
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
          prefix={<PhoneOutlined style={{ color: token.colorPrimaryText }} />}
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
          prefix={<LockOutlined style={{ color: token.colorPrimaryText }} />}
          placeholder="Mật khẩu"
        />
      </Form.Item>

      <Form.Item>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <Form.Item name="remember" valuePropName="checked" noStyle>
            <Checkbox>Ghi nhớ đăng nhập</Checkbox>
          </Form.Item>
          <a style={{ color: token.colorLink, fontWeight: 500 }} href="#forgot">
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
            // Chỉ giữ kích thước — màu nút lấy từ token primary, đúng cả hai chế độ.
            // Bo góc để antd lấy theo token (12) — khớp ô nhập phía trên, không hardcode.
            height: 48,
            fontWeight: 600,
            fontSize: 16,
          }}
        >
          Đăng nhập
        </Button>
      </Form.Item>
    </Form>
  );
};

export default LoginForm;
