import { useEffect } from 'react';
import { Col, Form, Input, InputNumber, Modal, Row, Space } from 'antd';
import type { Stop, StopPayload } from '../api/stopApi';
import StopMapPicker from './StopMapPicker';

interface StopFormModalProps {
  open: boolean;
  /** null = đang thêm mới; có giá trị = đang sửa trạm đó. */
  editing: Stop | null;
  /** Trang cha bật loading cho nút OK trong lúc gọi API. */
  submitting: boolean;
  onCancel: () => void;
  /** Nhận dữ liệu form hợp lệ + id (nếu đang sửa) để trang cha gọi API. */
  onSubmit: (payload: StopPayload, id?: string) => void;
}

export default function StopFormModal({
  open,
  editing,
  submitting,
  onCancel,
  onSubmit,
}: StopFormModalProps) {
  const [form] = Form.useForm<StopPayload>();

  // Mỗi lần mở modal: nạp dữ liệu trạm đang sửa, hoặc reset về trống nếu thêm mới.
  useEffect(() => {
    if (!open) return;
    if (editing) {
      form.setFieldsValue({
        name: editing.name,
        address: editing.address,
        latitude: editing.latitude,
        longitude: editing.longitude,
      });
    } else {
      form.resetFields();
    }
  }, [open, editing, form]);

  // Đọc toạ độ hiện tại trong form để vẽ marker lên bản đồ.
  const latitude = Form.useWatch('latitude', form);
  const longitude = Form.useWatch('longitude', form);
  const picked =
    latitude != null && longitude != null ? { latitude, longitude } : null;

  const handleOk = async () => {
    try {
      const values = await form.validateFields();
      onSubmit(values, editing?.id);
    } catch {
      // validateFields rejects khi form chưa hợp lệ — bỏ qua, AntD đã hiển thị lỗi từng ô.
    }
  };

  return (
    <Modal
      title={editing ? 'Sửa trạm dừng' : 'Thêm trạm dừng'}
      open={open}
      onOk={handleOk}
      onCancel={onCancel}
      confirmLoading={submitting}
      okText={editing ? 'Lưu thay đổi' : 'Thêm trạm'}
      cancelText="Huỷ"
      width={680}
      maskClosable={false}
    >
      <Form form={form} layout="vertical">
        <Form.Item
          name="name"
          label="Tên trạm dừng"
          rules={[{ required: true, message: 'Vui lòng nhập tên trạm dừng' }]}
        >
          <Input placeholder="VD: Trạm Cầu Giấy" maxLength={120} />
        </Form.Item>

        <Form.Item
          name="address"
          label="Địa chỉ"
          rules={[{ required: true, message: 'Vui lòng nhập địa chỉ' }]}
        >
          <Input placeholder="VD: Số 1 Cầu Giấy, Hà Nội" maxLength={200} />
        </Form.Item>

        <Form.Item label="Chọn toạ độ trên bản đồ" required>
          <Space direction="vertical" size={12} style={{ width: '100%' }}>
            <StopMapPicker
              value={picked}
              onChange={(lat, lng) => form.setFieldsValue({ latitude: lat, longitude: lng })}
            />
            <Row gutter={12}>
              <Col span={12}>
                <Form.Item
                  name="latitude"
                  label="Vĩ độ (Latitude)"
                  style={{ marginBottom: 0 }}
                  rules={[{ required: true, message: 'Vui lòng chọn toạ độ trên bản đồ' }]}
                >
                  <InputNumber
                    style={{ width: '100%' }}
                    readOnly
                    controls={false}
                    precision={6}
                    placeholder="—"
                  />
                </Form.Item>
              </Col>
              <Col span={12}>
                <Form.Item
                  name="longitude"
                  label="Kinh độ (Longitude)"
                  style={{ marginBottom: 0 }}
                  rules={[{ required: true, message: 'Vui lòng chọn toạ độ trên bản đồ' }]}
                >
                  <InputNumber
                    style={{ width: '100%' }}
                    readOnly
                    controls={false}
                    precision={6}
                    placeholder="—"
                  />
                </Form.Item>
              </Col>
            </Row>
          </Space>
        </Form.Item>
      </Form>
    </Modal>
  );
}
