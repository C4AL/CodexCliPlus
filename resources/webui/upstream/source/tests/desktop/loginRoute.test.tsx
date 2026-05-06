import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

const bridgeMocks = vi.hoisted(() => ({
  desktopMode: true,
}));

vi.mock('@/desktop/bridge', () => ({
  isDesktopMode: () => bridgeMocks.desktopMode,
}));

vi.mock('@/pages/LoginPage', () => ({
  LoginPage: () => <div data-testid="login-page">管理页登录</div>,
}));

import { LoginRoute } from '@/router/LoginRoute';

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

describe('desktop login route', () => {
  beforeEach(() => {
    bridgeMocks.desktopMode = true;
  });

  it('returns to the management entry instead of rendering the WebUI login page in desktop mode', async () => {
    renderLoginRoute();

    await waitFor(() => {
      expect(screen.getByText('管理入口')).toBeInTheDocument();
    });
    expect(screen.queryByTestId('login-page')).not.toBeInTheDocument();
  });

  it('renders the WebUI login page in browser mode', async () => {
    bridgeMocks.desktopMode = false;

    renderLoginRoute();

    expect(await screen.findByTestId('login-page')).toBeInTheDocument();
  });
});
