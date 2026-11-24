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
  tenantId: string;
  kind: string;
  title: string;
  status: string;
  memberEmployeeIds: string[];
  ownerEmployeeId?: string | null;
  scopeType: string;
  scopeId?: string | null;
  createdBy: string;
  createdByEmployeeId?: string | null;
  creatorType: string;
  createdAt: string;
}

export interface ChatMessage {
  id: string;
  tenantId: string;
  conversationId: string;
  seq: number;
  sender: string;
  body: string;
  priority: string;
  createdAt: string;
  messageType: string;
  replyToMessageId?: string | null;
  mentionedEmployeeIds: string[];
  evidenceRefs: string[];
  idempotencyKey?: string | null;
  senderEmployeeId?: string | null;
  actorType: string;
}

export interface CollaborationNode {
  id: string;
  type: 'Employee' | 'Conversation' | 'Project';
  label: string;
  subtitle: string;
  status: string;
  employeeId?: string | null;
  conversationId?: string | null;
  projectId?: string | null;
  positionId?: string | null;
  parentId?: string | null;
  depth: number;
  x: number;
  y: number;
  memberEmployeeIds: string[];
  activityCount: number;
}

export interface CollaborationEdge {
  id: string;
  sourceId: string;
  targetId: string;
  kind: string;
  weight: number;
  label: string;
}

export interface CollaborationActivity {
  id: string;
  occurredAt: string;
  type: string;
  title: string;
  actor: string;
  actorType: string;
  sourceType: string;
  sourceId: string;
  employeeId?: string | null;
  conversationId?: string | null;
  projectId?: string | null;
  taskId?: string | null;
  evidenceRefs: string[];
  auditHash?: string | null;
  targetEmployeeId?: string | null;
}

export interface CollaborationGraph {
  nodes: CollaborationNode[];
  edges: CollaborationEdge[];
  activities: CollaborationActivity[];
  summary: {
    employeeCount: number;
    conversationCount: number;
    messageCount: number;
    collaborationCount: number;
    projectCount: number;
    activityCount: number;
    coordinationCount: number;
  };
  generatedAt: string;
}

export interface CollaborationTrace {
  entityType: string;
  entityId: string;
  activities: CollaborationActivity[];
  generatedAt: string;
}

export interface TaskCoordination {
  id: string;
  tenantId: string;
  taskId: string;
  projectId: string;
  kind: 'Delegation' | 'Handoff' | 'Help' | 'Interrupt';
  status: string;
  requesterEmployeeId: string;
  targetEmployeeId: string;
  priority: string;
  summary: string;
  detailsJson: string;
  responseJson?: string | null;
  createdAt: string;
  updatedAt: string;
  deadline?: string | null;
  completedAt?: string | null;
  version: number;
}

export interface PageResult<T> {
  items: T[];
  nextCursor?: string | null;
  snapshotAt: string;
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
