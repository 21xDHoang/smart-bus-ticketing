import { useEffect } from 'react';
import { Divider, Form, Input, Modal, Select, Switch, Typography } from 'antd';
import { ROLE_OPTIONS } from '../api/adminUserApi';
import type { AdminUser, RoleCode } from '../api/adminUserApi';
import type { AppError } from '../api/axiosClient';
import DriverAssignmentInfo from './DriverAssignmentInfo';

/** Dữ liệu form đã kiểm hợp lệ, chuyển cho trang cha gọi API. */
export interface AdminUserFormSubmission {
  fullName: string;
  phoneNumber: string;
  /** null = không có email. */
  email: string | null;
  roles: RoleCode[];
  /** Tạo mới: bắt buộc. Sửa: null = giữ nguyên mật khẩu hiện tại. */
  password: string | null;
  isActive: boolean;
}

interface AdminUserFormValues {
  fullName: string;
  phoneNumber: string;
  email?: string;
  roles: RoleCode[];
  password?: string;
  isActive: boolean;
}

interface AdminUserFormModalProps {
  open: boolean;
  /** null = đang thêm mới; có giá trị = đang sửa tài khoản đó. */
  editing: AdminUser | null;
  /** Trang cha bật loading cho nút OK trong lúc gọi API. */
  submitting: boolean;
  onCancel: () => void;
  /**
   * Nhận dữ liệu hợp lệ + tài khoản đang sửa (null nếu thêm mới) để trang cha gọi API.
   * Trang cha reject với AppError khi thất bại — modal bắt lại để gắn lỗi từng ô.
   */
  onSubmit: (submission: AdminUserFormSubmission, editing: AdminUser | null) => Promise<void>;
}

/** SĐT di động Việt Nam — khớp `RegularExpression` trong CreateAdminUserRequest của backend. */
const PHONE_PATTERN = /^0[35789]\d{8}$/;

/** Mật khẩu hệ thống: 8–100 ký tự, có cả chữ và số — khớp cả RegisterRequest lẫn AdminUser. */
const PASSWORD_MIN = 8;
const PASSWORD_MAX = 100;
const PASSWORD_PATTERN = /^(?=.*[A-Za-z])(?=.*\d).+$/;

/** Backend báo lỗi theo tên trường của API, form dùng tên khác ở vài ô. */
const FIELD_ALIASES: Record<string, keyof AdminUserFormValues> = {
  roleCodes: 'roles',
  roleCode: 'roles',
  newPassword: 'password',
};

export default function AdminUserFormModal({
  open,
  editing,
  submitting,
  onCancel,
  onSubmit,
}: AdminUserFormModalProps) {
  const [form] = Form.useForm<AdminUserFormValues>();
  const isEdit = editing !== null;

  // Mỗi lần mở modal: nạp tài khoản đang sửa, hoặc về giá trị mặc định nếu thêm mới.
  useEffect(() => {
    if (!open) return;

    if (editing) {
      form.setFieldsValue({
        fullName: editing.fullName,
        phoneNumber: editing.phoneNumber,
        email: editing.email ?? undefined,
        // Dữ liệu cũ có thể thiếu `roles` — lấy vai trò chính làm đường lùi.
        roles: editing.roles?.length ? editing.roles : [editing.role],
        password: undefined,
        isActive: editing.isActive,
      });
    } else {
      form.resetFields();
    }
  }, [open, editing, form]);

  const handleOk = async () => {
    let values: AdminUserFormValues;
    try {
      values = await form.validateFields();
    } catch {
      // validateFields rejects khi form chưa hợp lệ — AntD đã hiển thị lỗi từng ô.
      return;
    }

    const submission: AdminUserFormSubmission = {
      fullName: values.fullName.trim(),
      phoneNumber: values.phoneNumber.trim(),
      email: values.email?.trim() || null,
      roles: values.roles,
      // Không trim mật khẩu: khoảng trắng có thể là một phần mật khẩu người dùng cố ý đặt.
      password: values.password || null,
      isActive: values.isActive ?? true,
    };

    try {
      await onSubmit(submission, editing);
    } catch (error) {
      const appError = error as AppError;
      if (appError?.errors) {
        form.setFields(
          Object.entries(appError.errors).map(([name, fieldErrors]) => ({
            name: FIELD_ALIASES[name] ?? (name as keyof AdminUserFormValues),
            errors: fieldErrors,
          })),
        );
      }
    }
  };

  // Thêm mới: mật khẩu bắt buộc. Sửa: bỏ trống là giữ nguyên, nhập vào thì phải đúng chuẩn.
  const passwordRules = isEdit
    ? [
        {
          validator: (_: unknown, value?: string) => {
            if (!value) return Promise.resolve();
            if (value.length < PASSWORD_MIN || value.length > PASSWORD_MAX) {
              return Promise.reject(
                new Error(`Mật khẩu phải từ ${PASSWORD_MIN} đến ${PASSWORD_MAX} ký tự`),
              );
            }
            if (!PASSWORD_PATTERN.test(value)) {
              return Promise.reject(new Error('Mật khẩu phải gồm cả chữ và số'));
            }
            return Promise.resolve();
          },
        },
      ]
    : [
        { required: true, message: 'Vui lòng nhập mật khẩu' },
        {
          min: PASSWORD_MIN,
          max: PASSWORD_MAX,
          message: `Mật khẩu phải từ ${PASSWORD_MIN} đến ${PASSWORD_MAX} ký tự`,
        },
        { pattern: PASSWORD_PATTERN, message: 'Mật khẩu phải gồm cả chữ và số' },
      ];

  // Chỉ tải phân công khi modal đang mở — Modal giữ children mounted sau lần mở đầu tiên.
  const showAssignment =
    open &&
    editing !== null &&
    (editing.role === 'Driver' || (editing.roles ?? []).includes('Driver'));

  return (
    <Modal
      title={isEdit ? 'Sửa tài khoản' : 'Thêm tài khoản'}
      open={open}
      onOk={handleOk}
      onCancel={onCancel}
      confirmLoading={submitting}
      okText={isEdit ? 'Lưu thay đổi' : 'Thêm tài khoản'}
      cancelText="Huỷ"
      width={620}
      maskClosable={false}
    >
      <Form
        form={form}
        layout="vertical"
        initialValues={{ roles: ['Passenger'], isActive: true }}
      >
        <Form.Item
          name="fullName"
          label="Họ và tên"
          rules={[
            { required: true, message: 'Vui lòng nhập họ và tên' },
            { min: 2, max: 200, message: 'Họ và tên phải từ 2 đến 200 ký tự' },
          ]}
        >
          <Input placeholder="VD: Nguyễn Văn An" maxLength={200} />
        </Form.Item>

        <Form.Item
          name="phoneNumber"
          label="Số điện thoại"
          extra="Số điện thoại là tên đăng nhập — phải duy nhất trong hệ thống."
          rules={[
            { required: true, message: 'Vui lòng nhập số điện thoại' },
            { pattern: PHONE_PATTERN, message: 'Số điện thoại không hợp lệ (10 số, bắt đầu 0[35789])' },
          ]}
        >
          <Input placeholder="VD: 0912345678" maxLength={10} />
        </Form.Item>

        <Form.Item
          name="email"
          label="Email"
          rules={[
            { type: 'email', message: 'Email không đúng định dạng' },
            { max: 256, message: 'Email tối đa 256 ký tự' },
          ]}
        >
          <Input placeholder="Không bắt buộc" maxLength={256} />
        </Form.Item>

        <Form.Item
          name="roles"
          label="Vai trò"
          extra={
            isEdit
              ? 'Gửi lên danh sách đầy đủ: vai trò bỏ chọn sẽ bị thu hồi. Vai trò chính tự chuyển sang vai trò có quyền cao nhất.'
              : 'Có thể chọn nhiều vai trò. Vai trò có quyền cao nhất sẽ là vai trò chính.'
          }
          rules={[
            { required: true, message: 'Chọn ít nhất một vai trò' },
            {
              validator: (_, value?: RoleCode[]) =>
                value && value.length > 0
                  ? Promise.resolve()
                  : Promise.reject(new Error('Tài khoản phải có ít nhất một vai trò')),
            },
          ]}
        >
          <Select<RoleCode[]> mode="multiple" options={ROLE_OPTIONS} placeholder="Chọn vai trò" />
        </Form.Item>

        <Form.Item
          name="password"
          label={isEdit ? 'Mật khẩu mới' : 'Mật khẩu ban đầu'}
          extra={
            isEdit
              ? 'Để trống nếu không đổi mật khẩu. Đổi xong mọi phiên đăng nhập cũ của tài khoản bị buộc đăng nhập lại.'
              : 'Ít nhất 8 ký tự, gồm cả chữ và số.'
          }
          rules={passwordRules}
        >
          <Input.Password
            autoComplete="new-password"
            placeholder={isEdit ? 'Không đổi' : 'VD: matkhau123'}
            maxLength={PASSWORD_MAX}
          />
        </Form.Item>

        {isEdit && (
          <Form.Item
            name="isActive"
            label="Trạng thái tài khoản"
            valuePropName="checked"
            extra="Tắt = khóa tài khoản. Người đang đăng nhập bị chặn ngay ở request kế tiếp."
          >
            <Switch checkedChildren="Đang hoạt động" unCheckedChildren="Đã khóa" />
          </Form.Item>
        )}
      </Form>

      {showAssignment && editing && (
        <>
          <Divider style={{ margin: '8px 0 16px' }}>Phân công hiện tại</Divider>
          <Typography.Paragraph type="secondary" style={{ fontSize: 12, marginBottom: 12 }}>
            Thông tin chuyến đang chạy hoặc chuyến kế tiếp của tài xế. Việc đổi xe/tuyến được
            thực hiện ở màn hình phân công chuyến.
          </Typography.Paragraph>
          <DriverAssignmentInfo driverId={editing.id} />
        </>
      )}
    </Modal>
  );
}
