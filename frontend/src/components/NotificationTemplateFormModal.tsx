import { useEffect } from 'react';
import { Form, Input, Modal, Switch, Typography } from 'antd';
import {
  NOTIFICATION_TYPE_META,
  PLACEHOLDER_HINTS,
} from '../api/notificationTemplateApi';
import type {
  NotificationTemplate,
  NotificationTemplatePayload,
  NotificationTemplateType,
} from '../api/notificationTemplateApi';

interface NotificationTemplateFormValues {
  title: string;
  body: string;
  isActive: boolean;
}

interface NotificationTemplateFormModalProps {
  open: boolean;
  /** Loại thông báo đang soạn — quyết định nhãn và danh sách chỗ trống dùng được. */
  type: NotificationTemplateType;
  /** null = loại này chưa có mẫu, đang soạn mới; có giá trị = đang sửa mẫu hiện tại. */
  editing: NotificationTemplate | null;
  /** Trang cha bật loading cho nút OK trong lúc gọi API. */
  submitting: boolean;
  onCancel: () => void;
  onSubmit: (payload: NotificationTemplatePayload) => void;
}

export default function NotificationTemplateFormModal({
  open,
  type,
  editing,
  submitting,
  onCancel,
  onSubmit,
}: NotificationTemplateFormModalProps) {
  const [form] = Form.useForm<NotificationTemplateFormValues>();
  const meta = NOTIFICATION_TYPE_META[type];

  // Mỗi lần mở modal: nạp mẫu đang sửa, hoặc reset về trống (mẫu mới bật sẵn).
  useEffect(() => {
    if (!open) return;
    if (editing) {
      form.setFieldsValue({
        title: editing.title,
        body: editing.body,
        isActive: editing.isActive,
      });
    } else {
      form.setFieldsValue({ title: '', body: '', isActive: true });
    }
  }, [open, editing, form]);

  const handleOk = async () => {
    try {
      const values = await form.validateFields();
      onSubmit({ title: values.title, body: values.body, isActive: values.isActive });
    } catch {
      // validateFields rejects khi form chưa hợp lệ — bỏ qua, AntD đã hiển thị lỗi từng ô.
    }
  };

  return (
    <Modal
      title={`${editing ? 'Sửa mẫu' : 'Soạn mẫu'} — ${meta.label}`}
      open={open}
      onOk={handleOk}
      onCancel={onCancel}
      confirmLoading={submitting}
      okText="Lưu mẫu"
      cancelText="Huỷ"
      width={640}
      maskClosable={false}
    >
      <Form form={form} layout="vertical">
        <Form.Item
          name="title"
          label="Tiêu đề"
          rules={[
            { required: true, message: 'Vui lòng nhập tiêu đề' },
            { max: 200, message: 'Tiêu đề tối đa 200 ký tự' },
          ]}
        >
          <Input placeholder="VD: Đã duyệt yêu cầu huỷ vé" maxLength={200} showCount />
        </Form.Item>

        <Form.Item
          name="body"
          label="Nội dung"
          rules={[{ required: true, message: 'Vui lòng nhập nội dung' }]}
        >
          {/* TextArea giữ nguyên ký tự xuống dòng; autoSize để soạn nội dung dài bất kỳ. */}
          <Input.TextArea
            placeholder="VD: Yêu cầu huỷ vé của bạn đã được duyệt."
            autoSize={{ minRows: 4, maxRows: 12 }}
          />
        </Form.Item>

        <Form.Item
          name="isActive"
          label="Đang dùng mẫu này"
          valuePropName="checked"
          extra="Tắt mẫu KHÔNG tắt thông báo — hệ thống quay về câu mặc định, chữ đã soạn vẫn được giữ."
        >
          <Switch checkedChildren="Bật" unCheckedChildren="Tắt" />
        </Form.Item>

        <Form.Item label="Chỗ trống dùng được (viết {Tên} để điền dữ liệu lúc gửi)">
          {meta.allowedPlaceholders.length === 0 ? (
            <Typography.Text type="secondary">
              Loại này không dùng chỗ trống nào.
            </Typography.Text>
          ) : (
            <ul style={{ margin: 0, paddingLeft: 20 }}>
              {meta.allowedPlaceholders.map((token) => (
                <li key={token}>
                  <Typography.Text code>{`{${token}}`}</Typography.Text>{' '}
                  <Typography.Text type="secondary">{PLACEHOLDER_HINTS[token]}</Typography.Text>
                </li>
              ))}
            </ul>
          )}
        </Form.Item>
      </Form>
    </Modal>
  );
}
