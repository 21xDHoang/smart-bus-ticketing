import { useEffect, useRef, useState } from 'react';
import { DatePicker, TimePicker } from 'antd';
import type { TimePickerProps } from 'antd';
import type { Dayjs } from 'dayjs';
import { keepIfSame, mergeDateAndTime, splitDeparture } from './departureDateTime';

// -----------------------------------------------------------------------------
// Component chọn ngày và giờ khởi hành — Sprint 2 dòng 10, US 13, Hoàng Văn Thịnh.
//
// Hai ô rời (DatePicker + TimePicker) nhưng chỉ phát ra MỘT giá trị Dayjs, nên cắm thẳng vào
// <Form.Item name="departureTime"> là chạy — AntD tự truyền value/onChange xuống.
//
// Dùng khi cần đúng một mốc khởi hành. Màn hình cần cả khung (giờ bắt đầu → giờ kết thúc) thì
// vẫn dùng hai ô riêng như TripFrequencyPage — component này không thay thế được.
//
// Gửi lên API thì gọi toVietnamIso() ở ./departureDateTime để ra chuỗi kèm offset +07:00.
// -----------------------------------------------------------------------------

interface DepartureDateTimePickerProps {
  /** Mốc khởi hành đã ghép. Form.Item của AntD truyền vào tự động. */
  value?: Dayjs | null;
  onChange?: (value: Dayjs | null) => void;
  disabled?: boolean;
  /** Bước phút của ô giờ — mặc định 5. */
  minuteStep?: TimePickerProps['minuteStep'];
  datePlaceholder?: string;
  timePlaceholder?: string;
}

export default function DepartureDateTimePicker({
  value = null,
  onChange,
  disabled = false,
  minuteStep = 5,
  datePlaceholder = 'Chọn ngày',
  timePlaceholder = 'Chọn giờ',
}: DepartureDateTimePickerProps) {
  // Hai nửa được giữ riêng vì người dùng chọn lệch nhau: mới có ngày mà chưa có giờ là trạng
  // thái hợp lệ trên màn hình, dù mốc ghép ra vẫn là null (form báo "chưa chọn giờ khởi hành").
  const [datePart, setDatePart] = useState<Dayjs | null>(() => splitDeparture(value).date);
  const [timePart, setTimePart] = useState<Dayjs | null>(() => splitDeparture(value).time);

  /**
   * Mốc gần nhất component phát ra.
   *
   * Cần biến này để phân biệt hai trường hợp cùng nhận `value === null`:
   *   - cha xoá giá trị (form.resetFields) → phải xoá cả hai ô;
   *   - chính component vừa phát null vì người dùng mới chọn xong MỘT nửa → phải GIỮ nửa đã
   *     chọn. Nếu đồng bộ theo null ở đây, ô vừa chọn sẽ tự xoá ngay trước mắt người dùng.
   */
  const lastEmittedRef = useRef<Dayjs | null>(null);

  // Nạp lại hai ô khi cha đổi giá trị (mở form sửa chuyến, reset form).
  useEffect(() => {
    const incoming = value ?? null;
    if (incoming === null && lastEmittedRef.current === null) return;
    lastEmittedRef.current = incoming;
    const { date, time } = splitDeparture(incoming);
    setDatePart((prev) => keepIfSame(date, prev));
    setTimePart((prev) => keepIfSame(time, prev));
  }, [value]);

  const emit = (nextDate: Dayjs | null, nextTime: Dayjs | null) => {
    setDatePart(nextDate);
    setTimePart(nextTime);
    // Chỉ phát ra khi đã đủ cả hai nửa — thiếu một nửa thì mốc vẫn là null và form báo lỗi
    // "bắt buộc", đúng ý nghĩa: chưa chọn xong giờ khởi hành.
    const merged = nextDate && nextTime ? mergeDateAndTime(nextDate, nextTime) : null;
    lastEmittedRef.current = merged;
    onChange?.(merged);
  };

  const handleDateChange = (next: Dayjs | null) => {
    // Xoá ngày là xoá cả mốc — còn mỗi giờ lẻ thì không biết là ngày nào.
    if (!next) {
      emit(null, null);
      return;
    }
    emit(next.startOf('day'), timePart);
  };

  const handleTimeChange = (next: Dayjs | null) => {
    // Xoá giờ thì giữ ngày — thường người dùng chỉ đang chọn lại giờ.
    emit(datePart, next);
  };

  return (
    <div style={{ display: 'flex', gap: 8 }}>
      <DatePicker
        value={datePart}
        onChange={handleDateChange}
        format="DD/MM/YYYY"
        placeholder={datePlaceholder}
        disabled={disabled}
        style={{ flex: 1, minWidth: 0 }}
      />
      <TimePicker
        value={timePart}
        onChange={handleTimeChange}
        format="HH:mm"
        minuteStep={minuteStep}
        placeholder={timePlaceholder}
        disabled={disabled}
        style={{ flex: 'none', width: 110 }}
      />
    </div>
  );
}
