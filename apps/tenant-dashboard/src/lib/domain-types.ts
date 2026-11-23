export interface WorkflowDefinition {
  id: string;
  tenantId?: string;
  name: string;
  version: number;
  steps: string[];
  status: string;
  author?: string | null;
  scopePositionId?: string | null;
  parentWorkflowId?: string | null;
  reviewedBy?: string | null;
  reviewNote?: string | null;
  createdAt?: string | null;
  reviewedAt?: string | null;
}

export interface WorkflowRun {
  id: string;
  workflowId: string;
  currentStep: number;
  status: string;
}

export interface MemoryClaim {
  id: string;
  scope: string;
  content: string;
  sourceRef: string;
  confidence: number;
  taint: string;
  status: string;
  version: number;
}

export interface KpiSnapshot {
  id: string;
  subjectType: string;
  subjectId: string;
  metricKey: string;
  value: number;
  target: string;
  status: string;
}

export interface Conversation {
  id: string;
  kind: string;
  title: string;
  status: string;
}

export interface ChatMessage {
  id: string;
  conversationId: string;
  seq: number;
  sender: string;
  body: string;
  priority: string;
  createdAt: string;
}

export interface BenchmarkSuite {
  id: string;
  name: string;
  version: number;
  caseCount: number;
  status: string;
}

export interface EvaluationRun {
  id: string;
  suiteId: string;
  candidateEmployeeId: string;
  modelVersion: string;
  seed: number;
  score: number;
  safetyPassed: boolean;
  status: string;
  createdAt: string;
}

export interface ComplianceProfile {
  tenantId: string;
  preset: string;
  allowedProviders: string[];
  allowedRegions: string[];
  onlineRetentionDays: number;
  auditProfile: string;
  backupRequired: boolean;
  externalSharingAllowed: boolean;
  version: number;
  status: string;
}

export interface NotificationItem {
  id: string;
  level: string;
  title: string;
  status: string;
  createdAt: string;
}
