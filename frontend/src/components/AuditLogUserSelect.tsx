import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Select, Spin, message } from 'antd';
import { fetchAdminUsers } from '../api/adminUserApi';
import type { AdminUser } from '../api/adminUserApi';
import type { AppError } from '../api/axiosClient';

/** Chờ người dùng ngừng gõ rồi mới gọi API — gõ "Nguyễn" là 6 ký tự, không phải 6 request. */
const SEARCH_DEBOUNCE_MS = 350;

/**
 * Số phương án tải mỗi lần. Ô này để CHỌN một người, không phải để duyệt hết danh sách;
 * người dùng muốn tìm ai thì gõ tên. `GET /admin/users` trả tối đa 100 dòng mỗi trang.
 */
const OPTION_PAGE_SIZE = 20;

interface AuditLogUserSelectProps {
  /** GUID người đang được lọc. `undefined` = không lọc theo người thao tác. */
  value?: string;

  /** Gọi khi chọn hoặc bỏ chọn. `undefined` nghĩa là bỏ lọc người dùng. */
  onChange: (userId?: string) => void;

  disabled?: boolean;
}

/**
 * Ô chọn người thao tác cho bộ lọc nhật ký — tìm kiếm phía SERVER.
 *
 * Tái dùng `fetchAdminUsers` sẵn có ở `src/api/adminUserApi.ts` (`USE_MOCK = false`, đã
 * chạy thật) thay vì tạo file API mới: `GET /admin/users` đã có trong api-contract.md và
 * cũng chỉ dành cho Admin, đúng quyền của màn hình nhật ký.
 */
export default function AuditLogUserSelect({
  value,
  onChange,
  disabled,
}: AuditLogUserSelectProps) {
  const [users, setUsers] = useState<AdminUser[]>([]);
  const [loading, setLoading] = useState(false);

  // Người đã chọn, giữ RIÊNG khỏi `users`. Sau khi chọn, người đó có thể không còn nằm
  // trong 20 kết quả của lần tải sau (nhất là khi người dùng gõ từ khoá khác) — không
  // giữ lại thì nhãn đang hiển thị biến thành GUID trần ngay trước mắt người dùng.
  const [picked, setPicked] = useState<AdminUser>();

  // Số thứ tự request, để bỏ qua phản hồi của request cũ. Gõ nhanh thì request cho
  // "Nguy" có thể về SAU request cho "Nguyễn" và ghi đè kết quả đúng.
  const requestSeq = useRef(0);
  const debounceTimer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);

  const load = useCallback(async (search: string) => {
    const seq = ++requestSeq.current;
    setLoading(true);

    try {
      const result = await fetchAdminUsers({
        page: 1,
        pageSize: OPTION_PAGE_SIZE,
        search: search || undefined,
      });

      // Có request mới hơn đang chạy → kết quả này đã lạc hậu, bỏ đi.
      if (seq !== requestSeq.current) return;
      setUsers(result.items);
    } catch (error) {
      if (seq !== requestSeq.current) return;
      message.error((error as AppError).customMessage || 'Không tải được danh sách người dùng.');
    } finally {
      // Chỉ tắt spinner nếu mình vẫn là request mới nhất — nếu không thì đang còn một
      // request khác chạy và spinner phải tiếp tục quay.
      if (seq === requestSeq.current) setLoading(false);
    }
  }, []);

  useEffect(() => {
    // Nạp sẵn một lượt để mở dropdown là có ngay, không phải chờ mạng.
    // oxlint-disable-next-line react/set-state-in-effect
    void load('');

    return () => {
      if (debounceTimer.current) clearTimeout(debounceTimer.current);
      // Vô hiệu hoá mọi phản hồi đang bay, để chúng không setState lên component đã gỡ.
      requestSeq.current += 1;
    };
  }, [load]);

  const handleSearch = (text: string) => {
    if (debounceTimer.current) clearTimeout(debounceTimer.current);
    debounceTimer.current = setTimeout(() => void load(text.trim()), SEARCH_DEBOUNCE_MS);
  };

  const options = useMemo(() => {
    // Ghép người đã chọn vào đầu danh sách nếu chưa có, để nhãn không biến mất.
    const rows =
      picked && !users.some((user) => user.id === picked.id) ? [picked, ...users] : users;

    return rows.map((user) => ({
      value: user.id,
      label: `${user.fullName} — ${user.phoneNumber}`,
    }));
  }, [users, picked]);

  return (
    <Select
      // antd 6 đã đánh dấu `onSearch`/`filterOption` ở cấp cao nhất là DEPRECATED và gom
      // vào `showSearch` (dạng cũ vẫn chạy nhưng sẽ bị bỏ ở bản sau). `filterOption: false`
      // vì lọc do SERVER làm qua tham số `search` — lọc tại client trên 20 dòng đã tải sẽ
      // giấu mất những người chưa tải về, mà người dùng lại tưởng "không có ai tên đó".
      showSearch={{ filterOption: false, onSearch: handleSearch }}
      allowClear
      placeholder="Người thao tác"
      style={{ width: 260 }}
      value={value}
      loading={loading}
      disabled={disabled}
      onChange={(next?: string) => {
        setPicked(users.find((user) => user.id === next));
        onChange(next);
      }}
      options={options}
      notFoundContent={loading ? <Spin size="small" /> : 'Không tìm thấy người dùng nào'}
    />
  );
}
