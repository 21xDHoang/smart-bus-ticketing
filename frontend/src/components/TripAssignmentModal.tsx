import { useEffect } from 'react';
import { Form, Modal, Select, Typography } from 'antd';
import type { BusOption } from '../api/busApi';
import type { Trip } from '../api/tripApi';
import dayjs from 'dayjs';

const { Text } = Typography;

interface TripAssignmentFormValues {
  busId: string;
}

interface TripAssignmentModalProps {
  open: boolean;
  /** Chuyến đang được phân công. null khi modal đóng. */
  trip: Trip | null;
  /** Danh sách xe đang khai thác (Active) cho ô chọn. */
  busOptions: BusOption[];
  /** Đang tải danh sách xe. */
  loadingBuses: boolean;
  /** Trang cha bật loading cho nút OK trong lúc gọi API. */
  submitting: boolean;
  onCancel: () => void;
  /** Nhận id xe mới để trang cha gọi PUT /routes/{routeId}/trips/{id}. */
  onSubmit: (busId: string) => void;
}

/**
 * Modal "Phân công điều xe" cho một chuyến — story 14, task của Nguyễn Đình Băng.
 *
 * Chỉ có một trường lưu được NGAY: "Xe chạy chuyến" (gọi PUT /trips để đổi xe). Ô "Tài xế"
 * được vẽ sẵn nhưng tạm khoá vì endpoint gán tài xế (task của Kiên) và hồ sơ tài xế
 * (task của Hiếu) chưa có — xem docs/api-contract.md. Khi có API thì mở lại ô này và nối
 * `driverId` vào payload PUT. Ô "Phụ xe" không có: quy ước A8.4 chốt chỉ 4 vai trò, không
 * có Phụ xe (bảng Trips cũng không có cột phụ xe).
 */
export default function TripAssignmentModal({
  open,
  trip,
  busOptions,
  loadingBuses,
  submitting,
  onCancel,
  onSubmit,
}: TripAssignmentModalProps) {
  const [form] = Form.useForm<TripAssignmentFormValues>();

  // Mỗi lần mở modal: nạp xe hiện tại của chuyến làm giá trị mặc định.
  useEffect(() => {
    if (!open || !trip) return;
    form.setFieldsValue({ busId: trip.busId });
  }, [open, trip, form]);

  // Xe đang gán cho chuyến có thể không còn Active (đã chuyển bảo dưỡng sau khi sinh chuyến),
  // nên không nằm trong `busOptions`. Ghép thêm một dòng "xe hiện tại" để ô chọn vẫn hiển thị
  // đúng giá trị đang có thay vì trắng trơn.
  const currentBusMissing = !!trip && !busOptions.some((bus) => bus.id === trip.busId);

  const options = [
    ...(currentBusMissing && trip
      ? [
          {
            value: trip.busId,
            label: `${trip.busLicensePlate} — xe hiện tại (không còn Active)`,
          },
        ]
      : []),
    ...busOptions.map((bus) => ({
      value: bus.id,
      label: `${bus.licensePlate} — ${bus.busType} (${bus.capacity} chỗ)`,
    })),
  ];

  const handleOk = async () => {
    try {
      const values = await form.validateFields();
      onSubmit(values.busId);
    } catch {
      // validateFields rejects khi form chưa hợp lệ — bỏ qua, AntD đã hiển thị lỗi từng ô.
    }
  };

  return (
    <Modal
      title="Phân công điều xe"
      open={open}
      onOk={handleOk}
      onCancel={onCancel}
      confirmLoading={submitting}
      okText="Lưu phân công"
      cancelText="Huỷ"
      width={520}
      maskClosable={false}
    >
      {trip && (
        <Typography.Paragraph type="secondary" style={{ marginBottom: 16 }}>
          Chuyến khởi hành lúc{' '}
          <Text strong>{dayjs(trip.departureTime).format('HH:mm — DD/MM/YYYY')}</Text> · xe hiện
          tại: <Text strong>{trip.busLicensePlate}</Text>
        </Typography.Paragraph>
      )}

      <Form form={form} layout="vertical">
        <Form.Item
          name="busId"
          label="Xe chạy chuyến"
          rules={[{ required: true, message: 'Vui lòng chọn xe' }]}
        >
          <Select
            showSearch
            optionFilterProp="label"
            placeholder="Chọn xe buýt"
            loading={loadingBuses}
            options={options}
            notFoundContent="Không có xe đang khai thác nào"
          />
        </Form.Item>

        <Form.Item label="Tài xế">
          <Select disabled placeholder="Chờ API gán tài xế" />
          <Text type="secondary" style={{ fontSize: 12 }}>
            Chưa thể phân công tài xế — endpoint gán tài xế (Kiên) và hồ sơ tài xế (Hiếu) chưa có.
            Sẽ nối sau.
          </Text>
        </Form.Item>
      </Form>
    </Modal>
  );
}
