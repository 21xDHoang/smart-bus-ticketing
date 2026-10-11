import { useState } from 'react';
import { Form, Input, Button, message, theme } from 'antd';
import { UserOutlined, MailOutlined, LockOutlined, PhoneOutlined } from '@ant-design/icons';
import authApi from '../../api/authApi';
import type { AppError } from '../../api/axiosClient';

interface RegisterFormValues {
  fullName: string;
  email: string;
  phoneNumber: string;
  password: string;
  confirmPassword: string;
}

interface RegisterFormProps {
  onRegisterSuccess?: () => void;
}

const RegisterForm = ({ onRegisterSuccess }: RegisterFormProps) => {
  const [form] = Form.useForm<RegisterFormValues>();
  const [loading, setLoading] = useState(false);
  // Icon trong form lấy màu theo token — tự đúng ở cả hai chế độ sáng/tối.
  const { token } = theme.useToken();

  const onFinish = async (values: RegisterFormValues) => {
    setLoading(true);
    try {
      await authApi.register({
        fullName: values.fullName,
        email: values.email,
        phoneNumber: values.phoneNumber,
        password: values.password,
      });

      message.success('Đăng ký tài khoản thành công! Hãy đăng nhập ngay.');
      if (onRegisterSuccess) onRegisterSuccess();
    } catch (err) {
      const appError = err as AppError;

      // Backend trả lỗi theo từng trường (SĐT/email đã tồn tại…) → gắn vào ô input.
      if (appError.errors) {
        form.setFields(
          Object.entries(appError.errors).map(([name, fieldErrors]) => ({
            name: name as keyof RegisterFormValues,
            errors: fieldErrors,
          })),
        );
      } else {
        message.error(appError.customMessage || 'Đăng ký thất bại!');
      }
    } finally {
      setLoading(false);
    }
  };

  return (
    <Form
      form={form}
      name="register_form"
      layout="vertical"
      onFinish={onFinish}
      size="large"
    >
      <Form.Item
        name="fullName"
        rules={[{ required: true, message: 'Vui lòng nhập Họ và tên!' }]}
      >
        <Input prefix={<UserOutlined style={{ color: token.colorPrimaryText }} />} placeholder="Họ và tên" />
      </Form.Item>

      <Form.Item
        name="email"
        rules={[
          { required: true, message: 'Vui lòng nhập Email!' },
          { type: 'email', message: 'Email không đúng định dạng!' },
        ]}
      >
        <Input prefix={<MailOutlined style={{ color: token.colorPrimaryText }} />} placeholder="Địa chỉ Email" />
      </Form.Item>

      <Form.Item
        name="phoneNumber"
        rules={[
          { required: true, message: 'Vui lòng nhập Số điện thoại!' },
          { pattern: /^0[35789][0-9]{8}$/, message: 'Số điện thoại không hợp lệ!' },
        ]}
      >
        <Input prefix={<PhoneOutlined style={{ color: token.colorPrimaryText }} />} placeholder="Số điện thoại" maxLength={10} />
      </Form.Item>

      <Form.Item
        name="password"
        rules={[
          { required: true, message: 'Vui lòng nhập mật khẩu!' },
          { min: 6, message: 'Mật khẩu phải tối thiểu 6 ký tự!' },
        ]}
        hasFeedback
      >
        <Input.Password prefix={<LockOutlined style={{ color: token.colorPrimaryText }} />} placeholder="Mật khẩu" />
      </Form.Item>

      <Form.Item
        name="confirmPassword"
        dependencies={['password']}
        hasFeedback
        rules={[
          { required: true, message: 'Xác nhận lại mật khẩu!' },
          ({ getFieldValue }) => ({
            validator(_, value) {
              if (!value || getFieldValue('password') === value) {
                return Promise.resolve();
              }
              return Promise.reject(new Error('Mật khẩu xác nhận không khớp!'));
            },
          }),
        ]}
      >
        <Input.Password prefix={<LockOutlined style={{ color: token.colorPrimaryText }} />} placeholder="Nhập lại mật khẩu" />
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
          Tạo tài khoản
        </Button>
      </Form.Item>
    </Form>
  );
};

export default RegisterForm;
