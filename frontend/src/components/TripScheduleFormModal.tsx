import { useEffect } from 'react';
import { DatePicker, Form, Modal, Select } from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { TRIP_STATUS_OPTIONS } from '../api/tripApi';
import type { Trip, TripStatus } from '../api/tripApi';
import type { BusOption, CreateTripPayload, UpdateTripPayload } from '../api/tripScheduleApi';
import type { AppError } from '../api/axiosClient';

// -----------------------------------------------------------------------------
// Form thêm/sửa chuyến — dùng chung cho màn hình quản lý lịch trình (story 13).
//
// Múi giờ Việt Nam cố định (UTC+7): hợp đồng "Giờ gửi lên phải kèm múi giờ" của
// docs/api-contract.md bắt buộc chuỗi thời gian kèm offset, nên ta nối +07:00 thay vì
// dùng toISOString() — người dùng nhìn thấy đúng giờ mình chọn, không phụ thuộc múi
// giờ của trình duyệt người xem.
// -----------------------------------------------------------------------------

const VIETNAM_OFFSET = '+07:00';

/** Chuyển Dayjs đang hiển thị thành ISO 8601 kèm offset Việt Nam để gửi lên server. */
function toVietnamIso(value: Dayjs): string {
  return `${value.format('YYYY-MM-DDTHH:mm:ss')}${VIETNAM_OFFSET}`;
}

interface TripScheduleFormValues {
  busId: string;
  departureTime: Dayjs;
  arrivalTime?: Dayjs | null;
  /** Chỉ có khi sửa — thêm mới không gửi status (backend mặc định Scheduled). */
  status?: TripStatus;
}

interface TripScheduleFormModalProps {
  open: boolean;
  /** null = đang thêm mới; có giá trị = đang sửa chuyến đó. */
  editing: Trip | null;
  /** Xe đang khai thác để chọn — trang cha tải sẵn. */
  buses: BusOption[];
  /** Trang cha bật loading cho nút OK trong lúc gọi API. */
  submitting: boolean;
  onCancel: () => void;
  /**
   * Nhận dữ liệu form hợp lệ + id (nếu đang sửa) để trang cha gọi API.
   * Trang cha reject với AppError khi thất bại — modal bắt lại để gắn lỗi từng ô.
   */
  onSubmit: (payload: CreateTripPayload | UpdateTripPayload, id?: string) => Promise<void>;
}

export default function TripScheduleFormModal({
  open,
  editing,
  buses,
  submitting,
  onCancel,
  onSubmit,
}: TripScheduleFormModalProps) {
  const [form] = Form.useForm<TripScheduleFormValues>();
  const isEdit = editing !== null;

  // Mỗi lần mở modal: nạp chuyến đang sửa, hoặc reset về trống nếu thêm mới.
  // departureTime/arrivalTime backend trả UTC (hậu tố Z); dayjs parse về giờ địa phương
  // (Việt Nam) rồi gửi lại kèm +07:00 — hiển thị đúng giờ đã lưu, không lệch múi giờ.
  useEffect(() => {
    if (!open) return;
    if (editing) {
      form.setFieldsValue({
        busId: editing.busId,
        departureTime: dayjs(editing.departureTime),
        arrivalTime: editing.arrivalTime ? dayjs(editing.arrivalTime) : null,
        status: editing.status,
      });
    } else {
      form.resetFields();
    }
  }, [open, editing, form]);

  const handleOk = async () => {
    let values: TripScheduleFormValues;
    try {
      values = await form.validateFields();
    } catch {
      // validateFields rejects khi form chưa hợp lệ — bỏ qua, AntD đã hiển thị lỗi từng ô.
      return;
    }

    const base: CreateTripPayload = {
      busId: values.busId,
      departureTime: toVietnamIso(values.departureTime),
      arrivalTime: values.arrivalTime ? toVietnamIso(values.arrivalTime) : null,
    };

    try {
      if (isEdit && editing) {
        await onSubmit({ ...base, status: values.status ?? editing.status }, editing.id);
      } else {
        await onSubmit(base);
      }
    } catch (error) {
      const appError = error as AppError;
      // Lỗi theo từng trường (giờ đến không sau giờ khởi hành, vượt trần chuyến/ngày…)
      // → gắn thẳng vào ô input; lỗi chung thì trang cha đã toast customMessage rồi.
      if (appError?.errors) {
        form.setFields(
          Object.entries(appError.errors).map(([name, fieldErrors]) => ({
            name: name as keyof TripScheduleFormValues,
            errors: fieldErrors,
          })),
        );
      }
    }
  };

  return (
    <Modal
      title={isEdit ? 'Sửa chuyến' : 'Thêm chuyến'}
      open={open}
      onOk={handleOk}
      onCancel={onCancel}
      confirmLoading={submitting}
      okText={isEdit ? 'Lưu thay đổi' : 'Thêm chuyến'}
      cancelText="Huỷ"
      width={520}
      maskClosable={false}
    >
      <Form form={form} layout="vertical">
        <Form.Item
          name="busId"
          label="Xe buýt"
          rules={[{ required: true, message: 'Vui lòng chọn xe buýt' }]}
        >
          <Select
            showSearch
            optionFilterProp="label"
            placeholder="Chọn xe đang khai thác"
            notFoundContent="Không có xe nào đang khai thác"
            options={buses.map((bus) => ({
              value: bus.id,
              label: `${bus.licensePlate} · ${bus.busType} · ${bus.capacity} chỗ`,
            }))}
          />
        </Form.Item>

        <Form.Item
          name="departureTime"
          label="Giờ khởi hành"
          rules={[{ required: true, message: 'Vui lòng chọn giờ khởi hành' }]}
        >
          <DatePicker
            showTime={{ format: 'HH:mm' }}
            format="DD/MM/YYYY HH:mm"
            style={{ width: '100%' }}
            placeholder="Chọn ngày giờ khởi hành"
          />
        </Form.Item>

        <Form.Item
          name="arrivalTime"
          label="Giờ dự kiến đến"
          dependencies={['departureTime']}
          rules={[
            {
              validator: (_, value: Dayjs | null | undefined) => {
                if (!value) return Promise.resolve();
                const departure = form.getFieldValue('departureTime') as Dayjs | undefined;
                if (departure && !value.isAfter(departure)) {
                  return Promise.reject(new Error('Giờ đến phải sau giờ khởi hành'));
                }
                return Promise.resolve();
              },
            },
          ]}
        >
          <DatePicker
            showTime={{ format: 'HH:mm' }}
            format="DD/MM/YYYY HH:mm"
            style={{ width: '100%' }}
            placeholder="Không bắt buộc"
          />
        </Form.Item>

        {isEdit && (
          <Form.Item
            name="status"
            label="Trạng thái"
            rules={[{ required: true, message: 'Vui lòng chọn trạng thái' }]}
          >
            <Select options={TRIP_STATUS_OPTIONS} />
          </Form.Item>
        )}
      </Form>
    </Modal>
  );
}
