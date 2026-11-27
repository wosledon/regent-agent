import { Navigate, Outlet, Route, Routes, useLocation } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { LoadingState, buildLoginPath } from '@regent/ui-shared';
import { LocalizedErrorState } from './components/LocalizedStates';
import { AuthProvider } from './app/AuthProvider';
import { AppShell } from './app/AppShell';
import { useAuth } from './app/useAuth';
import { canWriteOperations } from './lib/operationsPermissions';
import { AuditPage } from './pages/AuditPage';
import { BackupsPage } from './pages/BackupsPage';
import { LoginPage } from './pages/LoginPage';
import { OverviewPage } from './pages/OverviewPage';
import { ObservabilityPage } from './pages/ObservabilityPage';
import { StatusPage } from './pages/StatusPage';
import { SupportPage } from './pages/SupportPage';
import { TenantsPage } from './pages/TenantsPage';

export function App() {
  return <AuthProvider><OperationsRoutes /></AuthProvider>;
}

function OperationsRoutes() {
  const { user } = useAuth();
  const canWrite = canWriteOperations(user);
  return <Routes>
    <Route path="/login" element={<LoginPage />} />
    <Route path="/status" element={<StatusPage />} />
    <Route element={<Protected />}>
      <Route element={<AppShell />}>
        <Route index element={<OverviewPage />} />
        <Route path="tenants" element={<TenantsPage canWrite={canWrite} />} />
        <Route path="support" element={<SupportPage canWrite={canWrite} />} />
        <Route path="backups" element={<BackupsPage canWrite={canWrite} />} />
        <Route path="audit" element={<AuditPage />} />
        <Route path="platform" element={<StatusPage operational canWrite={canWrite} />} />
        <Route path="observability" element={<ObservabilityPage canWrite={canWrite} />} />
      </Route>
    </Route>
    <Route path="*" element={<Navigate to="/" replace />} />
  </Routes>;
}

function Protected() {
  const { status, retry } = useAuth();
  const { t } = useTranslation();
  const location = useLocation();
  if (status === 'checking') return <LoadingState label={t('auth.loading.checking')} />;
  if (status === 'permission-denied' || status === 'error') {
    const message = status === 'permission-denied' ? t('common.errors.permissionDenied') : t('common.errors.sessionVerification');
    return <LocalizedErrorState message={message} onRetry={() => void retry().catch(() => undefined)} />;
  }
  if (status === 'anonymous') {
    const target = `${location.pathname}${location.search}${location.hash}`;
    return <Navigate to={buildLoginPath(target)} replace />;
  }
  return <Outlet />;
}
