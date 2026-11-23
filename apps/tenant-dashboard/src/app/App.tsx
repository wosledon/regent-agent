import { Navigate, Route, Routes } from 'react-router-dom';
import { AuthProvider } from './AuthProvider';
import { useAuth } from './useAuth';
import { AppShell } from './AppShell';
import { ComponentGalleryPage } from '@regent/ui-shared';
import { LoginPage } from '../pages/LoginPage';
import { DashboardPage } from '../features/dashboard/DashboardPage';
import { ProjectListPage } from '../features/projects/ProjectListPage';
import { ProjectDetailPage } from '../features/projects/ProjectDetailPage';
import { EmployeeListPage } from '../features/organization/EmployeeListPage';
import { EmployeeDetailPage } from '../features/organization/EmployeeDetailPage';
import { ApprovalListPage } from '../features/governance/ApprovalListPage';
import { ApprovalPage } from '../features/governance/ApprovalPage';
import { ActionCenterPage } from '../features/governance/ActionCenterPage';
import { CompliancePage } from '../features/governance/CompliancePage';
import { IncidentsPage } from '../features/governance/IncidentsPage';
import { NotificationsPage } from '../features/governance/NotificationsPage';
import { MessagesPage } from '../features/chat/MessagesPage';
import { WorkflowsPage } from '../features/workflows/WorkflowsPage';
import { MemoryPage } from '../features/memory/MemoryPage';
import { PerformancePage } from '../features/performance/PerformancePage';
import { EvaluationPage } from '../features/evaluation/EvaluationPage';
import { BudgetPage } from '../features/budget/BudgetPage';
import { ModelsPage } from '../features/models/ModelsPage';
import { JournalPage } from '../features/journal/JournalPage';
import { Text } from '@regent/ui-shared';

function ProtectedShell() {
  const { session } = useAuth();
  return session ? <AppShell /> : <Navigate to="/login" replace />;
}

function SettingsPage() {
  return <Text variant="h1">Settings</Text>;
}

export function App() {
  return (
    <AuthProvider>
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        <Route element={<ProtectedShell />}>
          <Route index element={<DashboardPage />} />
          <Route path="projects" element={<ProjectListPage />} />
          <Route path="projects/:projectId" element={<ProjectDetailPage />} />
          <Route path="organization" element={<EmployeeListPage />} />
          <Route path="organization/employees/:employeeId" element={<EmployeeDetailPage />} />
          <Route path="governance/approvals" element={<ApprovalListPage />} />
          <Route path="governance/approvals/:approvalId" element={<ApprovalPage />} />
          <Route path="governance/action-center" element={<ActionCenterPage />} />
          <Route path="governance/notifications" element={<NotificationsPage />} />
          <Route path="governance/incidents" element={<IncidentsPage />} />
          <Route path="governance/compliance" element={<CompliancePage />} />
          <Route path="messages" element={<MessagesPage />} />
          <Route path="workflows" element={<WorkflowsPage />} />
          <Route path="knowledge" element={<MemoryPage />} />
          <Route path="performance" element={<PerformancePage />} />
          <Route path="evaluations" element={<EvaluationPage />} />
          <Route path="budget" element={<BudgetPage />} />
          <Route path="models" element={<ModelsPage />} />
          <Route path="journal" element={<JournalPage />} />
          <Route path="settings" element={<SettingsPage />} />
          <Route path="dev/components" element={<ComponentGalleryPage />} />
        </Route>
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </AuthProvider>
  );
}
