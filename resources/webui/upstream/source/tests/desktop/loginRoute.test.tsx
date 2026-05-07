import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

const bridgeMocks = vi.hoisted(() => ({
  desktopMode: true,
  requestNativeLogin: vi.fn(),
}));

const authStoreMocks = vi.hoisted(() => ({
  state: {
    isAuthenticated: false,
    managementKey: '',
    apiBase: '',
    connectionError: null as string | null,
    restoreSession: vi.fn(async () => false),
    checkAuth: vi.fn(async () => false),
  },
}));

vi.mock('@/desktop/bridge', () => ({
  isDesktopMode: () => bridgeMocks.desktopMode,
  requestNativeLogin: bridgeMocks.requestNativeLogin,
}));

vi.mock('@/stores', () => ({
  useAuthStore: <T,>(selector: (state: typeof authStoreMocks.state) => T) =>
    selector(authStoreMocks.state),
}));

import { LoginRoute } from '@/router/LoginRoute';
import { ProtectedRoute } from '@/router/ProtectedRoute';

function renderLoginRoute() {
  return render(
    <MemoryRouter initialEntries={['/login']}>
      <Routes>
        <Route path="/login" element={<LoginRoute />} />
        <Route path="/" element={<div>管理入口</div>} />
      </Routes>
    </MemoryRouter>
  );
}

function renderProtectedRoute() {
  return render(
    <MemoryRouter initialEntries={['/']}>
      <Routes>
        <Route
          path="/"
          element={
            <ProtectedRoute>
              <div>受保护管理页</div>
            </ProtectedRoute>
          }
        />
      </Routes>
    </MemoryRouter>
  );
}

describe('desktop login route', () => {
  beforeEach(() => {
    bridgeMocks.desktopMode = true;
    bridgeMocks.requestNativeLogin.mockClear();
    authStoreMocks.state.isAuthenticated = false;
    authStoreMocks.state.managementKey = '';
    authStoreMocks.state.apiBase = '';
    authStoreMocks.state.connectionError = null;
    authStoreMocks.state.restoreSession.mockClear();
    authStoreMocks.state.checkAuth.mockClear();
  });

  it('returns to the management entry instead of rendering the WebUI login page in desktop mode', async () => {
    renderLoginRoute();

    await waitFor(() => {
      expect(screen.getByText('管理入口')).toBeInTheDocument();
    });
    expect(screen.queryByTestId('login-page')).not.toBeInTheDocument();
  });

  it('blocks the browser login route instead of rendering a WebUI login page', async () => {
    bridgeMocks.desktopMode = false;

    renderLoginRoute();

    expect(await screen.findByText('请在桌面应用内打开管理界面')).toBeInTheDocument();
    expect(screen.queryByTestId('login-page')).not.toBeInTheDocument();
  });

  it('blocks protected management routes in browser mode even with a restored browser session', async () => {
    bridgeMocks.desktopMode = false;
    authStoreMocks.state.isAuthenticated = true;
    authStoreMocks.state.apiBase = 'http://127.0.0.1:15345';
    authStoreMocks.state.managementKey = 'legacy-key';

    renderProtectedRoute();

    expect(await screen.findByText('请在桌面应用内打开管理界面')).toBeInTheDocument();
    expect(screen.queryByText('受保护管理页')).not.toBeInTheDocument();
    expect(authStoreMocks.state.restoreSession).not.toHaveBeenCalled();
    expect(authStoreMocks.state.checkAuth).not.toHaveBeenCalled();
  });

  it('retries desktop session restore and renders the management page after a later success', async () => {
    authStoreMocks.state.restoreSession
      .mockResolvedValueOnce(false)
      .mockImplementationOnce(async () => {
        authStoreMocks.state.isAuthenticated = true;
        return true;
      });

    renderProtectedRoute();

    expect(await screen.findByText('桌面会话需要恢复')).toBeInTheDocument();

    fireEvent.click(screen.getByText('重试'));

    await waitFor(() => {
      expect(screen.getByText('受保护管理页')).toBeInTheDocument();
    });
    expect(authStoreMocks.state.restoreSession).toHaveBeenCalledTimes(2);
  });
});
