import { useEffect } from 'react';
import { Form, Input, InputNumber, Modal, Select } from 'antd';
import { BUS_STATUS_OPTIONS } from '../api/busCrudApi';
import type { Bus, BusStatus } from '../api/busApi';
import type { BusPayload, UpdateBusPayload } from '../api/busCrudApi';
import type { AppError } from '../api/axiosClient';

interface BusFormValues {
  licensePlate: string;
  busType: string;
  capacity: number | null;
  /** Chỉ có khi sửa — thêm mới không gửi status (backend mặc định Active). */
  status?: BusStatus;
}

interface BusFormModalProps {
  open: boolean;
  /** null = đang thêm mới; có giá trị = đang sửa xe đó. */
  editing: Bus | null;
  /** Trang cha bật loading cho nút OK trong lúc gọi API. */
  submitting: boolean;
  onCancel: () => void;
  /**
   * Nhận dữ liệu form hợp lệ + id (nếu đang sửa) để trang cha gọi API.
   * Trang cha reject với AppError khi thất bại — modal bắt lại để gắn lỗi từng ô.
   */
  onSubmit: (payload: BusPayload | UpdateBusPayload, id?: string) => Promise<void>;
}

/** Trần sức chứa khớp CreateBusRequest.Capacity — `[Range(1, 200)]` của backend. */
const MAX_CAPACITY = 200;

export default function BusFormModal({
  open,
  editing,
  submitting,
  onCancel,
  onSubmit,
}: BusFormModalProps) {
  const [form] = Form.useForm<BusFormValues>();
  const isEdit = editing !== null;

  // Mỗi lần mở modal: nạp xe đang sửa, hoặc reset về trống nếu thêm mới.
  useEffect(() => {
    if (!open) return;
    if (editing) {
      form.setFieldsValue({
        licensePlate: editing.licensePlate,
        busType: editing.busType,
        capacity: editing.capacity,
        status: editing.status,
      });
    } else {
      form.resetFields();
    }
  }, [open, editing, form]);

  const handleOk = async () => {
    let values: BusFormValues;
    try {
      values = await form.validateFields();
    } catch {
      // validateFields rejects khi form chưa hợp lệ — bỏ qua, AntD đã hiển thị lỗi từng ô.
      return;
    }

    try {
      if (isEdit && editing) {
        await onSubmit(
          {
            licensePlate: values.licensePlate,
            busType: values.busType,
            capacity: values.capacity ?? 0,
            status: values.status ?? editing.status,
          },
          editing.id,
        );
      } else {
        await onSubmit({
          licensePlate: values.licensePlate,
          busType: values.busType,
          capacity: values.capacity ?? 0,
        });
      }
    } catch (error) {
      const appError = error as AppError;
      // Lỗi theo từng trường (trùng biển số, sức chứa sai…) → gắn thẳng vào ô input;
      // lỗi chung thì trang cha đã toast customMessage rồi, không làm gì thêm.
      if (appError?.errors) {
        form.setFields(
          Object.entries(appError.errors).map(([name, fieldErrors]) => ({
            name: name as keyof BusFormValues,
            errors: fieldErrors,
          })),
        );
      }
    }
  };

  return (
    <Modal
      title={isEdit ? 'Sửa xe buýt' : 'Thêm xe buýt'}
      open={open}
      onOk={handleOk}
      onCancel={onCancel}
      confirmLoading={submitting}
      okText={isEdit ? 'Lưu thay đổi' : 'Thêm xe'}
      cancelText="Huỷ"
      width={520}
      maskClosable={false}
    >
      <Form form={form} layout="vertical">
        <Form.Item
          name="licensePlate"
          label="Biển số xe"
          rules={[
            { required: true, message: 'Vui lòng nhập biển số' },
            { min: 2, max: 20, message: 'Biển số từ 2 đến 20 ký tự' },
          ]}
        >
          <Input placeholder="VD: 29B-123.45" maxLength={20} />
        </Form.Item>

        <Form.Item
          name="busType"
          label="Loại xe"
          rules={[
            { required: true, message: 'Vui lòng nhập loại xe' },
            { min: 2, max: 50, message: 'Loại xe từ 2 đến 50 ký tự' },
          ]}
        >
          <Input placeholder="VD: Xe buýt 45 chỗ, Xe buýt điện" maxLength={50} />
        </Form.Item>

        <Form.Item
          name="capacity"
          label="Sức chứa (số ghế)"
          rules={[
            { required: true, message: 'Vui lòng nhập sức chứa' },
            {
              validator: (_, value: number | null | undefined) => {
                if (value == null) return Promise.resolve();
                if (value < 1 || value > MAX_CAPACITY) {
                  return Promise.reject(new Error('Sức chứa từ 1 đến 200 chỗ'));
                }
                return Promise.resolve();
              },
            },
          ]}
        >
          <InputNumber style={{ width: '100%' }} min={1} max={MAX_CAPACITY} placeholder="VD: 45" />
        </Form.Item>

        {isEdit && (
          <Form.Item
            name="status"
            label="Trạng thái"
            extra="Xe đang bảo dưỡng sẽ tạm rút khỏi đội xe, không gán vào chuyến mới."
            rules={[{ required: true, message: 'Vui lòng chọn trạng thái' }]}
          >
            <Select options={BUS_STATUS_OPTIONS} />
          </Form.Item>
        )}
      </Form>
    </Modal>
  );
}
