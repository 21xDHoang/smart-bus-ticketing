import { useEffect } from 'react';
import { Checkbox, DatePicker, Form, Input, Modal, Select, Typography } from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { REPORT_TYPES, REPORT_TYPE_META, columnsOf } from '../api/reportTemplateApi';
import type { ReportTemplate, ReportTemplatePayload, ReportType } from '../api/reportTemplateApi';

const { RangePicker } = DatePicker;

interface ReportTemplateFormValues {
  name: string;
  reportType: ReportType;
  columns: string[];
  /** Khoảng thời gian — dayjs cho RangePicker, đổi sang yyyy-MM-dd khi submit. */
  range: [Dayjs, Dayjs];
}

interface ReportTemplateFormModalProps {
  open: boolean;
  /** null = đang thêm mẫu; có giá trị = đang sửa mẫu đó. */
  editing: ReportTemplate | null;
  /** Trang cha bật loading cho nút OK trong lúc lưu. */
  submitting: boolean;
  onCancel: () => void;
  onSubmit: (payload: ReportTemplatePayload) => void;
}

export default function ReportTemplateFormModal({
  open,
  editing,
  submitting,
  onCancel,
  onSubmit,
}: ReportTemplateFormModalProps) {
  const [form] = Form.useForm<ReportTemplateFormValues>();
  const reportType = Form.useWatch('reportType', form);
  const columns = columnsOf(reportType);

  // Mỗi lần mở modal: nạp mẫu đang sửa, hoặc reset về mẫu trống (hôm nay, chưa chọn cột).
  useEffect(() => {
    if (!open) return;
    if (editing) {
      form.setFieldsValue({
        name: editing.name,
        reportType: editing.reportType,
        columns: editing.columns,
        range: [dayjs(editing.from), dayjs(editing.to)],
      });
    } else {
      form.setFieldsValue({
        name: '',
        reportType: 'Revenue',
        columns: [],
        range: [dayjs(), dayjs()],
      });
    }
  }, [open, editing, form]);

  // Đổi loại báo cáo thì bộ cột thay đổi — bỏ hết cột đã chọn của loại cũ để không sót khoá lạ.
  const handleReportTypeChange = () => {
    form.setFieldValue('columns', []);
  };

  const handleOk = async () => {
    try {
      const values = await form.validateFields();
      onSubmit({
        name: values.name,
        reportType: values.reportType,
        columns: values.columns,
        from: values.range[0].format('YYYY-MM-DD'),
        to: values.range[1].format('YYYY-MM-DD'),
      });
    } catch {
      // validateFields rejects khi form chưa hợp lệ — bỏ qua, AntD đã hiển thị lỗi từng ô.
    }
  };

  return (
    <Modal
      title={editing ? 'Sửa mẫu báo cáo' : 'Thêm mẫu báo cáo'}
      open={open}
      onOk={handleOk}
      onCancel={onCancel}
      confirmLoading={submitting}
      okText="Lưu mẫu"
      cancelText="Huỷ"
      width={560}
      maskClosable={false}
    >
      <Form form={form} layout="vertical">
        <Form.Item
          name="name"
          label="Tên mẫu"
          rules={[
            { required: true, message: 'Vui lòng nhập tên mẫu' },
            { max: 120, message: 'Tên mẫu tối đa 120 ký tự' },
          ]}
        >
          <Input placeholder="VD: Báo cáo doanh thu tháng 10" maxLength={120} showCount />
        </Form.Item>

        <Form.Item
          name="reportType"
          label="Loại báo cáo"
          rules={[{ required: true, message: 'Vui lòng chọn loại báo cáo' }]}
        >
          <Select
            placeholder="Chọn loại báo cáo"
            onChange={handleReportTypeChange}
            options={REPORT_TYPES.map((type) => ({
              value: type,
              label: REPORT_TYPE_META[type].label,
            }))}
          />
        </Form.Item>

        <Form.Item
          name="columns"
          label="Cột hiển thị"
          rules={[
            {
              validator: (_, value: string[] | undefined) =>
                value && value.length > 0
                  ? Promise.resolve()
                  : Promise.reject(new Error('Chọn ít nhất một cột')),
            },
          ]}
        >
          <Checkbox.Group
            options={columns.map((column) => ({ value: column.key, label: column.label }))}
          />
        </Form.Item>

        <Form.Item
          name="range"
          label="Khoảng thời gian"
          rules={[{ required: true, message: 'Vui lòng chọn khoảng thời gian' }]}
        >
          <RangePicker style={{ width: '100%' }} format="DD/MM/YYYY" />
        </Form.Item>

        {reportType && (
          <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>
            {REPORT_TYPE_META[reportType].description}
          </Typography.Paragraph>
        )}
      </Form>
    </Modal>
  );
}
