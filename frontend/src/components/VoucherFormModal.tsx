import { useEffect } from 'react';
import { DatePicker, Form, Input, InputNumber, Modal, Select } from 'antd';
import dayjs from 'dayjs';
import type { Dayjs } from 'dayjs';
import { VOUCHER_DISCOUNT_TYPE_OPTIONS } from '../api/voucherApi';
import type { Voucher, VoucherDiscountType, VoucherPayload } from '../api/voucherApi';

interface VoucherFormValues {
  code: string;
  name: string;
  discountType: VoucherDiscountType;
  discountValue: number;
  minOrderValue: number;
  maxDiscount?: number | null;
  quantity: number;
  validFrom: Dayjs;
  validUntil: Dayjs;
}

interface VoucherFormModalProps {
  open: boolean;
  /** null = đang thêm mới; có giá trị = đang sửa voucher của dòng đó. */
  editing: Voucher | null;
  /** Trang cha bật loading cho nút OK trong lúc gọi API. */
  submitting: boolean;
  onCancel: () => void;
  /** Nhận dữ liệu form hợp lệ + id (nếu đang sửa) để trang cha gọi API. */
  onSubmit: (payload: VoucherPayload, id?: string) => void;
}

/** Trần tiền khớp cột numeric(12,2) của CSDL — cùng lối FareFormModal. */
const MAX_MONEY = 9999999999.99;

export default function VoucherFormModal({
  open,
  editing,
  submitting,
  onCancel,
  onSubmit,
}: VoucherFormModalProps) {
  const [form] = Form.useForm<VoucherFormValues>();
  const isEdit = editing !== null;

  // Theo dõi kiểu giảm giá để đổi nhãn ô nhập và ẩn/hiện ô "mức giảm tối đa" (chỉ Percent).
  const discountType = Form.useWatch('discountType', form) ?? 'Percent';

  // Mỗi lần mở modal: nạp giá đang sửa, hoặc reset về giá trị mặc định nếu thêm mới.
  useEffect(() => {
    if (!open) return;
    if (editing) {
      form.setFieldsValue({
        code: editing.code,
        name: editing.name,
        discountType: editing.discountType,
        discountValue: editing.discountValue,
        minOrderValue: editing.minOrderValue,
        maxDiscount: editing.maxDiscount ?? undefined,
        quantity: editing.quantity,
        validFrom: dayjs(editing.validFrom),
        validUntil: dayjs(editing.validUntil),
      });
    } else {
      form.resetFields();
      form.setFieldsValue({ discountType: 'Percent', minOrderValue: 0 });
    }
  }, [open, editing, form]);

  const handleOk = async () => {
    try {
      const values = await form.validateFields();
      onSubmit(
        {
          code: values.code,
          name: values.name,
          discountType: values.discountType,
          discountValue: values.discountValue,
          minOrderValue: values.minOrderValue ?? 0,
          maxDiscount: values.discountType === 'Percent' ? (values.maxDiscount ?? null) : null,
          quantity: values.quantity,
          validFrom: values.validFrom.toISOString(),
          validUntil: values.validUntil.toISOString(),
        },
        editing?.id,
      );
    } catch {
      // validateFields rejects khi form chưa hợp lệ — bỏ qua, AntD đã hiển thị lỗi từng ô.
    }
  };

  const isPercent = discountType === 'Percent';

  return (
    <Modal
      title={isEdit ? 'Sửa voucher' : 'Thêm voucher'}
      open={open}
      onOk={handleOk}
      onCancel={onCancel}
      confirmLoading={submitting}
      okText={isEdit ? 'Lưu thay đổi' : 'Thêm voucher'}
      cancelText="Huỷ"
      width={560}
      maskClosable={false}
    >
      <Form form={form} layout="vertical">
        <Form.Item
          name="code"
          label="Mã voucher"
          normalize={(value: string) => value?.toUpperCase()}
          rules={[
            { required: true, message: 'Vui lòng nhập mã voucher' },
            { min: 2, max: 20, message: 'Mã voucher từ 2 đến 20 ký tự' },
          ]}
        >
          <Input placeholder="VD: SUMMER10" />
        </Form.Item>

        <Form.Item
          name="name"
          label="Tên chương trình"
          rules={[
            { required: true, message: 'Vui lòng nhập tên chương trình' },
            { min: 2, max: 200, message: 'Tên chương trình từ 2 đến 200 ký tự' },
          ]}
        >
          <Input placeholder="VD: Giảm giá mùa hè 10%" />
        </Form.Item>

        <Form.Item
          name="discountType"
          label="Kiểu giảm giá"
          rules={[{ required: true, message: 'Vui lòng chọn kiểu giảm giá' }]}
        >
          <Select options={VOUCHER_DISCOUNT_TYPE_OPTIONS} />
        </Form.Item>

        <Form.Item
          name="discountValue"
          label={isPercent ? 'Phần trăm giảm (%)' : 'Số tiền giảm (VND)'}
          rules={[
            { required: true, message: 'Vui lòng nhập giá trị giảm' },
            {
              validator: (_, value: number | null | undefined) => {
                if (value == null) return Promise.resolve();
                if (form.getFieldValue('discountType') === 'Percent') {
                  if (!Number.isInteger(value) || value < 1 || value > 100) {
                    return Promise.reject(new Error('Phần trăm giảm phải từ 1 đến 100'));
                  }
                } else if (value <= 0 || value > MAX_MONEY) {
                  return Promise.reject(new Error('Số tiền giảm phải lớn hơn 0'));
                }
                return Promise.resolve();
              },
            },
          ]}
        >
          <InputNumber style={{ width: '100%' }} min={1} placeholder={isPercent ? 'VD: 10' : 'VD: 20000'} />
        </Form.Item>

        <Form.Item
          name="minOrderValue"
          label="Đơn tối thiểu (VND)"
          rules={[
            {
              validator: (_, value: number | null | undefined) => {
                if (value == null) return Promise.resolve();
                if (value < 0) return Promise.reject(new Error('Đơn tối thiểu không được âm'));
                if (value > MAX_MONEY) {
                  return Promise.reject(new Error('Đơn tối thiểu tối đa 9.999.999.999,99'));
                }
                return Promise.resolve();
              },
            },
          ]}
        >
          <InputNumber style={{ width: '100%' }} min={0} placeholder="0 = không yêu cầu" />
        </Form.Item>

        {/* Mức giảm tối đa chỉ có nghĩa với kiểu Phần trăm — ẩn hẳn khi chọn số tiền cố định. */}
        {isPercent && (
          <Form.Item
            name="maxDiscount"
            label="Mức giảm tối đa (VND)"
            rules={[
              {
                validator: (_, value: number | null | undefined) => {
                  if (value == null) return Promise.resolve();
                  if (value <= 0) {
                    return Promise.reject(new Error('Mức giảm tối đa phải lớn hơn 0'));
                  }
                  if (value > MAX_MONEY) {
                    return Promise.reject(new Error('Mức giảm tối đa quá lớn'));
                  }
                  return Promise.resolve();
                },
              },
            ]}
          >
            <InputNumber style={{ width: '100%' }} min={1} placeholder="Bỏ trống = không giới hạn" />
          </Form.Item>
        )}

        <Form.Item
          name="quantity"
          label="Số lượng phát hành"
          rules={[
            { required: true, message: 'Vui lòng nhập số lượng' },
            {
              validator: (_, value: number | null | undefined) => {
                if (value == null) return Promise.resolve();
                if (!Number.isInteger(value) || value < 1) {
                  return Promise.reject(new Error('Số lượng phải là số nguyên lớn hơn 0'));
                }
                return Promise.resolve();
              },
            },
          ]}
        >
          <InputNumber style={{ width: '100%' }} min={1} precision={0} placeholder="VD: 500" />
        </Form.Item>

        <Form.Item
          name="validFrom"
          label="Ngày bắt đầu hiệu lực"
          rules={[{ required: true, message: 'Vui lòng chọn ngày bắt đầu' }]}
        >
          <DatePicker style={{ width: '100%' }} format="DD/MM/YYYY" />
        </Form.Item>

        <Form.Item
          name="validUntil"
          label="Ngày hết hiệu lực"
          rules={[
            { required: true, message: 'Vui lòng chọn ngày kết thúc' },
            {
              validator: (_, value: Dayjs | null) => {
                if (!value) return Promise.resolve();
                const from = form.getFieldValue('validFrom') as Dayjs | null;
                if (from && value.isBefore(from, 'day')) {
                  return Promise.reject(new Error('Ngày kết thúc phải sau ngày bắt đầu'));
                }
                return Promise.resolve();
              },
            },
          ]}
        >
          <DatePicker style={{ width: '100%' }} format="DD/MM/YYYY" />
        </Form.Item>
      </Form>
    </Modal>
  );
}
