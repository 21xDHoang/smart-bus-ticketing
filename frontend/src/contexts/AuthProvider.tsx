import { useCallback, useEffect, useMemo, useState } from 'react';
import type { ReactNode } from 'react';
import { message } from 'antd';
import authApi, { decodeAccessToken } from '../api/authApi';
import type { DecodedUser, LoginPayload } from '../api/authApi';
import { onSessionExpired } from '../api/axiosClient';
import tokenStorage from '../api/tokenStorage';
import { AuthContext } from './AuthContext';
import type { AuthContextValue } from './AuthContext';

// Đọc phiên đăng nhập đã lưu khi mở lại trang (đồng bộ, không cần effect).
function readStoredUser(): DecodedUser | null {
  const token = tokenStorage.getAccessToken();
  if (!token) return null;

  const user = decodeAccessToken(token);
  if (user) return user;

  // Token hỏng hoặc không giải mã được → dọn dẹp phiên cũ.
  tokenStorage.clear();
  return null;
}

/**
 * Giữ trạng thái đăng nhập của toàn app và là nơi duy nhất ghi token vào localStorage.
 * Bọc ngoài <App /> trong main.tsx.
 */
export const AuthProvider = ({ children }: { children: ReactNode }) => {
  // Khởi tạo ngay từ localStorage nên lần render đầu đã biết đã đăng nhập hay chưa —
  // không có màn hình "nháy" trang đăng nhập khi F5.
  const [user, setUser] = useState<DecodedUser | null>(readStoredUser);

  const login = useCallback(async (payload: LoginPayload): Promise<DecodedUser> => {
    const response = await authApi.login(payload);
    tokenStorage.save(response.accessToken, response.refreshToken);

    // Backend chưa có endpoint /auth/profile nên thông tin người dùng nằm trong claims của token.
    const loggedInUser = decodeAccessToken(response.accessToken);
    if (!loggedInUser) {
      tokenStorage.clear();
      throw new Error('Không đọc được thông tin người dùng từ token.');
    }

    setUser(loggedInUser);
    return loggedInUser;
  }, []);

  const logout = useCallback(async (): Promise<void> => {
    const refreshToken = tokenStorage.getRefreshToken();

    // Xoá phiên phía client trước để giao diện phản hồi ngay, không chờ mạng.
    tokenStorage.clear();
    setUser(null);

    // Thu hồi refresh token phía server (best-effort — lỗi mạng cũng không chặn người dùng).
    if (refreshToken) {
      try {
        await authApi.logout(refreshToken);
      } catch {
        // Bỏ qua: phiên phía client đã được xoá.
      }
    }
  }, []);

  // Access token hết hạn và refresh token cũng hỏng. axiosClient đã xoá token rồi,
  // ở đây chỉ hạ trạng thái để app quay về màn hình đăng nhập.
  useEffect(
    () =>
      onSessionExpired(() => {
        setUser(null);
        message.warning('Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.');
      }),
    [],
  );

  const value = useMemo<AuthContextValue>(
    () => ({ user, isAuthenticated: user !== null, login, logout }),
    [user, login, logout],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
};

export default AuthProvider;
