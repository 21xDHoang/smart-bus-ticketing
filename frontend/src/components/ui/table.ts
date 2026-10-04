import type { TablePaginationConfig } from 'antd';
import { PAGE_SIZE_OPTIONS } from './format';

/**
 * Cấu hình phân trang chuẩn cho mọi bảng: cỡ trang, nhảy trang, dòng tổng tiếng Việt.
 *
 * Chỉ TRẢ VỀ props phân trang — màn gọi vẫn tự giữ luật "đổi cỡ trang thì quay về trang 1"
 * (mỗi màn xử lý một kiểu, đổi hành vi đó là việc của lô màn tương ứng, không phải của hàm).
 */
export function tablePagination(options: {
  page: number;
  pageSize: number;
  total: number;
  /** Danh từ đếm được, ví dụ "xe" → "Hiển thị 1–10 trên 24 xe". */
  unitLabel: string;
  onChange: (page: number, pageSize: number) => void;
}): TablePaginationConfig {
  const { page, pageSize, total, unitLabel, onChange } = options;
  return {
    current: page,
    pageSize,
    total,
    showSizeChanger: true,
    showQuickJumper: true,
    pageSizeOptions: PAGE_SIZE_OPTIONS,
    showTotal: (count, range) => `Hiển thị ${range[0]}–${range[1]} trên ${count} ${unitLabel}`,
    onChange,
  };
}
