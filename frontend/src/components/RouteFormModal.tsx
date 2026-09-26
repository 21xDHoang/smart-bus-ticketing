import { useEffect } from 'react';
import { Form, Input, InputNumber, Modal, Select } from 'antd';
import { ROUTE_STATUS_OPTIONS } from '../api/routeApi';
import type { Route, RouteStatus } from '../api/routeApi';
import type { RoutePayload, UpdateRoutePayload } from '../api/routeCrudApi';
import type { AppError } from '../api/axiosClient';

interface RouteFormValues {
  code: string;
  name: string;
  origin: string;
  destination: string;
  distanceKm: number | null;
  /** Chỉ có khi sửa — thêm mới không gửi status (backend mặc định Active). */
  status?: RouteStatus;
}

interface RouteFormModalProps {
  open: boolean;
  /** null = đang thêm mới; có giá trị = đang sửa tuyến đó. */
  editing: Route | null;
  /** Trang cha bật loading cho nút OK trong lúc gọi API. */
  submitting: boolean;
  onCancel: () => void;
  /**
   * Nhận dữ liệu form hợp lệ + id (nếu đang sửa) để trang cha gọi API.
   * Trang cha reject với AppError khi thất bại — modal bắt lại để gắn lỗi từng ô.
   */
  onSubmit: (payload: RoutePayload | UpdateRoutePayload, id?: string) => Promise<void>;
}

/** Trần độ dài khớp cột numeric(6,2) — xem docs/api-contract.md. */
const MAX_DISTANCE_KM = 9999.99;

export default function RouteFormModal({
  open,
  editing,
  submitting,
  onCancel,
  onSubmit,
}: RouteFormModalProps) {
  const [form] = Form.useForm<RouteFormValues>();
  const isEdit = editing !== null;

  // Mỗi lần mở modal: nạp tuyến đang sửa, hoặc reset về trống nếu thêm mới.
  useEffect(() => {
    if (!open) return;
    if (editing) {
      form.setFieldsValue({
        code: editing.code,
        name: editing.name,
        origin: editing.origin,
        destination: editing.destination,
        distanceKm: editing.distanceKm,
        status: editing.status,
      });
    } else {
      form.resetFields();
    }
  }, [open, editing, form]);

  const handleOk = async () => {
    let values: RouteFormValues;
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
            code: values.code,
            name: values.name,
            origin: values.origin,
            destination: values.destination,
            distanceKm: values.distanceKm ?? 0,
            status: values.status ?? editing.status,
          },
          editing.id,
        );
      } else {
        await onSubmit({
          code: values.code,
          name: values.name,
          origin: values.origin,
          destination: values.destination,
          distanceKm: values.distanceKm ?? 0,
        });
      }
    } catch (error) {
      const appError = error as AppError;
      // Lỗi theo từng trường (trùng mã tuyến, độ dài sai…) → gắn thẳng vào ô input;
      // lỗi chung thì trang cha đã toast customMessage rồi, không làm gì thêm.
      if (appError?.errors) {
        form.setFields(
          Object.entries(appError.errors).map(([name, fieldErrors]) => ({
            name: name as keyof RouteFormValues,
            errors: fieldErrors,
          })),
        );
      }
    }
  };

  return (
    <Modal
      title={isEdit ? 'Sửa tuyến đường' : 'Thêm tuyến đường'}
      open={open}
      onOk={handleOk}
      onCancel={onCancel}
      confirmLoading={submitting}
      okText={isEdit ? 'Lưu thay đổi' : 'Thêm tuyến'}
      cancelText="Huỷ"
      width={520}
      maskClosable={false}
    >
      <Form form={form} layout="vertical">
        <Form.Item
          name="code"
          label="Mã tuyến"
          rules={[
            { required: true, message: 'Vui lòng nhập mã tuyến' },
            { min: 2, max: 20, message: 'Mã tuyến từ 2 đến 20 ký tự' },
          ]}
        >
          <Input placeholder="VD: 01, B10" maxLength={20} />
        </Form.Item>

        <Form.Item
          name="name"
          label="Tên tuyến"
          rules={[
            { required: true, message: 'Vui lòng nhập tên tuyến' },
            { min: 2, max: 200, message: 'Tên tuyến từ 2 đến 200 ký tự' },
          ]}
        >
          <Input placeholder="VD: Bến Thành — Chợ Lớn" maxLength={200} />
        </Form.Item>

        <Form.Item
          name="origin"
          label="Điểm đầu"
          rules={[
            { required: true, message: 'Vui lòng nhập điểm đầu' },
            { min: 2, max: 200, message: 'Điểm đầu từ 2 đến 200 ký tự' },
          ]}
        >
          <Input placeholder="VD: Bến Thành" maxLength={200} />
        </Form.Item>

        <Form.Item
          name="destination"
          label="Điểm cuối"
          rules={[
            { required: true, message: 'Vui lòng nhập điểm cuối' },
            { min: 2, max: 200, message: 'Điểm cuối từ 2 đến 200 ký tự' },
          ]}
        >
          <Input placeholder="VD: Chợ Lớn" maxLength={200} />
        </Form.Item>

        <Form.Item
          name="distanceKm"
          label="Độ dài tuyến (km)"
          rules={[
            {
              validator: (_, value: number | null | undefined) => {
                if (value == null) return Promise.resolve();
                if (value < 0) return Promise.reject(new Error('Độ dài không được âm'));
                if (value > MAX_DISTANCE_KM) {
                  return Promise.reject(new Error('Độ dài tối đa 9999,99 km'));
                }
                return Promise.resolve();
              },
            },
          ]}
        >
          <InputNumber style={{ width: '100%' }} min={0} placeholder="VD: 12.5" />
        </Form.Item>

        {isEdit && (
          <Form.Item
            name="status"
            label="Trạng thái"
            rules={[{ required: true, message: 'Vui lòng chọn trạng thái' }]}
          >
            <Select options={ROUTE_STATUS_OPTIONS} />
          </Form.Item>
        )}
      </Form>
    </Modal>
  );
}
