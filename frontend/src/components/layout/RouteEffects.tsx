import { useEffect } from 'react';
import { useLocation } from 'react-router-dom';
import { pageTitleOf } from '../ui/navItems';

/**
 * Tác dụng phụ theo route — chủ duy nhất của hai việc: cuộn về đầu trang khi đổi màn và
 * cập nhật tiêu đề tab trình duyệt. Gom một chỗ để không màn nào tự làm rồi tranh nhau.
 */
export default function RouteEffects() {
  const { pathname } = useLocation();

  useEffect(() => {
    window.scrollTo(0, 0);
  }, [pathname]);

  useEffect(() => {
    document.title = `${pageTitleOf(pathname)} — Smart Bus Ticket`;
  }, [pathname]);

  return null;
}
