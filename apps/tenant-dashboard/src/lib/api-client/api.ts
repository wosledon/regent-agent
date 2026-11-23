import type { components } from './schema';
import type {
  BenchmarkSuite,
  ChatMessage,
  ComplianceProfile,
  Conversation,
  EvaluationRun,
  KpiSnapshot,
  MemoryClaim,
  NotificationItem,
  WorkflowDefinition,
  WorkflowRun
} from '../domain-types';

type Schema = components['schemas'];

export class ApiError extends Error {
  constructor(public readonly status: number, public readonly code: string, message: string) {
    super(message);
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    credentials: 'include',
    ...init,
    headers: {
      'Content-Type': 'application/json',
      ...init?.headers
    }
  });
  if (!response.ok) {
    const body = await response.json().catch(() => null) as { error?: { code?: string; message?: string } } | null;
    throw new ApiError(response.status, body?.error?.code ?? 'UNKNOWN', body?.error?.message ?? response.statusText);
  }
  return response.status === 204 ? undefined as T : await response.json() as T;
}

function post<T>(path: string, body?: unknown): Promise<T> {
  return request<T>(path, { method: 'POST', body: body === undefined ? undefined : JSON.stringify(body) });
}

function put<T>(path: string, body?: unknown): Promise<T> {
  return request<T>(path, { method: 'PUT', body: body === undefined ? undefined : JSON.stringify(body) });
}

export interface AuthResponse {
  email: string;
  display_name: string;
  user_id: string;
  tenant_id: string;
  session_id: string;
  recovery_codes: string[];
}

export const api = {
  devLogin: (email: string) => request<{ email: string; display_name: string; tenant_id: string }>('/auth/dev-login', {
    method: 'POST', body: JSON.stringify({ email } satisfies Schema['DevLoginRequest'])
  }),
  passwordLogin: (email: string, password: string) => post<AuthResponse>('/auth/login', { email, password }),
  register: (email: string, password: string, displayName: string, companyName: string) =>
    post<AuthResponse>('/auth/register', { email, password, displayName, companyName }),
  sessions: () => request<Schema['SessionDto'][]>('/auth/sessions'),
  revokeSession: (id: string) => post<void>(`/auth/sessions/${id}/revoke`),
  invitations: (tenantId: string) => request<Schema['InvitationDto'][]>(`/v1/tenants/${tenantId}/invitations`),
  createInvitation: (tenantId: string, email: string, role: string) =>
    post<Schema['InvitationDto']>(`/v1/tenants/${tenantId}/invitations`, { email, role, expiresInHours: 24 }),
  ownershipDelegations: (tenantId: string) => request<Schema['OwnershipDelegationDto'][]>(`/v1/tenants/${tenantId}/ownership-delegations`),
  createOwnershipDelegation: (tenantId: string, delegateUserId: string) =>
    post<Schema['OwnershipDelegationDto']>(`/v1/tenants/${tenantId}/ownership-delegations`, { delegateUserId, expiresInHours: 24, reason: 'Planned absence' }),
  configureIdentityProvider: (tenantId: string, provider: string, issuer: string, clientId: string) =>
    put<Schema['IdentityProviderDto']>(`/v1/tenants/${tenantId}/identity-providers/${provider}`, {
      tenantId, provider, issuer, clientId, enabled: true, groupMappingJson: '{}'
    }),
  localization: () => request<Schema['LocalizationSettings']>('/v1/localization'),
  updateLocalization: (settings: Schema['LocalizationSettings']) => put<Schema['LocalizationSettings']>('/v1/localization', settings),
  displaySuffix: () => request<Schema['DisplaySuffixDto']>('/v1/profile/display-suffix'),
  updateDisplaySuffix: (suffix: string) => put<Schema['DisplaySuffixDto']>('/v1/profile/display-suffix', { suffix }),
  dashboard: () => request<Schema['DashboardSummary']>('/v1/dashboard/summary'),
  butlerAsk: (question: string) => post<Schema['ButlerAnswer']>('/v1/butler/ask', { question }),
  butlerReports: () => request<Schema['ButlerReport'][]>('/v1/butler/reports'),
  generateButlerReport: (kind: string) => post<Schema['ButlerReport']>('/v1/butler/reports', { kind }),
  retryButlerReport: (id: string) => post<Schema['ButlerReport']>(`/v1/butler/reports/${id}/retry`),
  butlerSchedules: () => request<Schema['ReportSchedule'][]>('/v1/butler/schedules'),
  saveButlerSchedule: (kind: string, frequency: string, enabled: boolean) =>
    post<Schema['ReportSchedule']>('/v1/butler/schedules', { kind, frequency, enabled }),
  projects: () => request<Schema['ProjectSummary'][]>('/v1/projects'),
  createProject: (body: Schema['CreateProjectDto']) => post<Schema['ProjectDetail']>('/v1/projects', body),
  project: (id: string) => request<Schema['ProjectDetail']>(`/v1/projects/${id}`),
  transitionProject: (id: string, stage: string, evidence: string) =>
    post<Schema['ProjectDetail']>(`/v1/projects/${id}/transitions`, { stage, evidence }),
  employees: () => request<Schema['EmployeeSummary'][]>('/v1/employees'),
  rehireEmployee: (employeeId: string, positionId: string, approvalId: string) =>
    post<Schema['EmployeeSummary']>(`/v1/employees/${employeeId}/rehire`, { positionId, approvalId }),
  employeeTimeline: (id: string) => request<Schema['EmployeeTimeline']>(`/v1/employees/${id}/timeline`),
  createApproval: (body: Schema['CreateApprovalDto']) => post<Schema['ApprovalDto']>('/v1/approvals', body),
  approval: (id: string) => request<Schema['ApprovalDto']>(`/v1/approvals/${id}`),
  decideApproval: (id: string, decision: Schema['ApprovalDecision'], idempotencyKey: string) =>
    request<Schema['ApprovalDto']>(`/v1/approvals/${id}/decision`, {
      method: 'POST',
      headers: { 'Idempotency-Key': idempotencyKey },
      body: JSON.stringify(decision)
    }),
  workflows: () => request<WorkflowDefinition[]>('/v1/workflows'),
  createWorkflow: (name: string, steps: string[], scopePositionId: string) =>
    post<WorkflowDefinition>('/v1/workflows', { name, steps, scopePositionId }),
  nominateWorkflow: (id: string) => post<WorkflowDefinition>(`/v1/workflows/${id}/nominate`),
  reviewWorkflow: (id: string, decision: string, reviewer: string, note: string) =>
    post<WorkflowDefinition>(`/v1/workflows/${id}/review`, { decision, reviewer, note }),
  startWorkflow: (id: string) => request<WorkflowRun>(`/v1/workflows/${id}/runs`, { method: 'POST' }),
  advanceWorkflow: (id: string) => request<WorkflowRun>(`/v1/workflow-runs/${id}/advance`, { method: 'POST' }),
  memories: (query = '') => request<MemoryClaim[]>(`/v1/memories?query=${encodeURIComponent(query)}`),
  captureMemory: (body: Schema['MemoryCaptureRequest']) => post<Schema['MemoryClaim']>('/v1/memories', body),
  searchMemories: (query: string, scope?: string) => request<Schema['MemorySearchResult']>(
    `/v1/memories/search?query=${encodeURIComponent(query)}${scope ? `&scope=${encodeURIComponent(scope)}` : ''}&maxResults=20`),
  rebuildMemoryIndex: () => post<{ indexed: number }>('/v1/memory/index/rebuild'),
  memoryReviewQueue: () => request<Schema['MemoryClaim'][]>('/v1/memory/review-queue'),
  publishMemory: (id: string, decision: 'Approve' | 'Reject', reviewerId = 'Boss') =>
    post<Schema['MemoryClaim']>(`/v1/memory/claims/${id}/publish`, { decision, reviewerId, note: 'Independent publication review' }),
  archiveMemory: (id: string) => post<void>(`/v1/memories/${id}/archive`),
  memoryFindings: () => request<Schema['MemoryFinding'][]>('/v1/memory/findings'),
  detectMemoryPoisoning: (claimId: string) => post<Schema['MemoryFinding'][]>('/v1/memory/findings', { claimId }),
  quarantineMemoryFinding: (id: string, reviewerId: string, reason: string, scope: string) =>
    post<Schema['MemoryFinding']>(`/v1/memory/findings/${id}/quarantine`, { reviewerId, reason, scope, reviewAfterHours: 72 }),
  appealMemoryFinding: (id: string, reason: string, submittedBy: string) =>
    post<Schema['MemoryFinding']>(`/v1/memory/findings/${id}/appeal`, { reason, submittedBy }),
  reviewMemoryAppeal: (id: string, reviewerId: string, resolution: string, release: boolean) =>
    post<Schema['MemoryFinding']>(`/v1/memory/findings/${id}/review`, { reviewerId, resolution, release }),
  memoryCorrections: () => request<Schema['CorrectionTask'][]>('/v1/memory/corrections'),
  createMemoryCorrection: (body: Schema['MemoryCorrectionRequest']) => post<Schema['CorrectionTask']>('/v1/memory/corrections', body),
  approveMemoryCorrection: (id: string, reviewerId: string) =>
    post<Schema['MemoryClaim']>(`/v1/memory/corrections/${id}/approve`, { reviewerId }),
  memoryImpact: (claimId: string) => request<Schema['MemoryImpactRecord'][]>(`/v1/memory/claims/${claimId}/impact`),
  recordMemoryImpact: (claimId: string, body: Schema['MemoryImpactRequest']) =>
    post<Schema['MemoryImpactRecord']>(`/v1/memory/claims/${claimId}/impact`, body),
  forgetMemory: (id: string) => post<void>(`/v1/memories/${id}/forget`),
  deleteMemory: (id: string) => request<void>(`/v1/memories/${id}`, { method: 'DELETE' }),
  kpis: () => request<KpiSnapshot[]>('/v1/kpis'),
  conversations: () => request<Conversation[]>('/v1/conversations'),
  messages: (id: string) => request<ChatMessage[]>(`/v1/conversations/${id}/messages`),
  sendMessage: (id: string, body: string, priority = 'P2') =>
    request<ChatMessage>(`/v1/conversations/${id}/messages`, { method: 'POST', body: JSON.stringify({ body, priority }) }),
  evaluationSuites: () => request<BenchmarkSuite[]>('/v1/evaluation-suites'),
  evaluations: () => request<EvaluationRun[]>('/v1/evaluations/runs'),
  runEvaluation: (suiteId: string, candidateEmployeeId: string, seed = 42) =>
    request<EvaluationRun>('/v1/evaluations/runs', { method: 'POST', body: JSON.stringify({ suiteId, candidateEmployeeId, seed }) }),
  compliance: () => request<ComplianceProfile>('/v1/compliance/profile'),
  updateCompliance: (profile: ComplianceProfile) =>
    request<ComplianceProfile>('/v1/compliance/profile', { method: 'PUT', body: JSON.stringify(profile) }),
  notifications: () => request<NotificationItem[]>('/v1/notifications'),
  notificationAction: (id: string, action: 'read' | 'acknowledge' | 'archive') =>
    request<void>(`/v1/notifications/${id}/${action}`, { method: 'POST' }),
  tenants: () => request<Schema['TenantDto'][]>('/v1/tenants'),
  createTenant: (body: Schema['CreateTenantRequest']) => post<Schema['TenantDto']>('/v1/tenants', body),
  switchTenant: (tenantId: string) => post<{ tenant_id: string }>('/auth/switch-tenant', { tenantId }),
  charter: () => request<Schema['CharterDto']>('/v1/charter'),
  updateCharter: (charter: Schema['CharterDto']) => put<Schema['CharterDto']>('/v1/charter', charter),
  startCompany: () => post<Schema['EmployeeSummary']>('/v1/company/start'),
  positions: () => request<Schema['PositionDto'][]>('/v1/positions'),
  createPosition: (position: Schema['PositionDto']) => post<Schema['PositionDto']>('/v1/positions', position),
  setPositionStatus: (id: string, status: string) => put<Schema['PositionDto']>(`/v1/positions/${id}/status`, { status }),
  mergePositions: (sourceId: string, targetId: string) => post<Schema['PositionDto']>(`/v1/positions/${sourceId}/merge/${targetId}`),
  splitPosition: (id: string, name: string, responsibilities: string) => post<Schema['PositionDto']>(`/v1/positions/${id}/split`, { name, responsibilities }),
  createCandidate: (body: Schema['CreateCandidateRequest']) => post<Schema['CandidateDto']>('/v1/candidates', body),
  assessCandidate: (id: string, assessment: Schema['CandidateAssessmentDto']) =>
    post<Schema['CandidateDto']>(`/v1/candidates/${id}/assessment`, assessment),
  delivery: (projectId: string) => request<Schema['ProjectDeliveryState']>(`/v1/projects/${projectId}/delivery`),
  addFinding: (projectId: string, finding: Schema['ResearchFinding']) =>
    post<Schema['ResearchFinding']>(`/v1/projects/${projectId}/delivery/findings`, finding),
  createBrief: (projectId: string, brief: Schema['ProductBrief']) =>
    post<Schema['ProductBrief']>(`/v1/projects/${projectId}/delivery/briefs`, brief),
  decideBrief: (projectId: string, briefId: string, decision: string, rationale: string, evidenceIds: string[], reround: number, decider: string) =>
    post<Schema['GoNoGoDecision']>(`/v1/projects/${projectId}/delivery/briefs/${briefId}/decision`, { decision, rationale, evidenceIds, reround, decider }),
  createRequirement: (projectId: string, requirement: Schema['DeliveryRequirement']) =>
    post<Schema['DeliveryRequirement']>(`/v1/projects/${projectId}/delivery/requirements`, requirement),
  readyRequirement: (projectId: string, requirementId: string, totalWeight: number) =>
    post<Schema['DeliveryRequirement']>(`/v1/projects/${projectId}/delivery/requirements/${requirementId}/ready`, { totalWeight }),
  proposePlan: (projectId: string, plan: Schema['DecompositionPlan']) =>
    post<Schema['DecompositionPlan']>(`/v1/projects/${projectId}/delivery/plans`, plan),
  approvePlan: (projectId: string, planId: string) =>
    post<Schema['DecompositionPlan']>(`/v1/projects/${projectId}/delivery/plans/${planId}/approve`, { approver: 'Controller' }),
  reportDefect: (projectId: string, defect: Schema['Defect']) =>
    post<Schema['Defect']>(`/v1/projects/${projectId}/delivery/defects`, defect),
  fixDefect: (projectId: string, defectId: string, fixer = 'Engineer') =>
    post<Schema['Defect']>(`/v1/projects/${projectId}/delivery/defects/${defectId}/fix`, {
      fixer, evidence: { id: `fix:${defectId}`, kind: 'test', location: 'local:fix-evidence' }
    }),
  addReview: (projectId: string, review: Schema['Review']) =>
    post<Schema['Review']>(`/v1/projects/${projectId}/delivery/reviews`, review),
  addAcceptanceReport: (projectId: string, report: Schema['AcceptanceReport']) =>
    post<Schema['AcceptanceReport']>(`/v1/projects/${projectId}/delivery/acceptance-reports`, report),
  releaseProject: (projectId: string, version: string, artifactRef: string) =>
    post<Schema['ReleaseRecord']>(`/v1/projects/${projectId}/delivery/releases`, { version, artifactRef }),
  retestDefect: (projectId: string, defectId: string, passed: boolean, retester: string) =>
    post<Schema['Defect']>(`/v1/projects/${projectId}/delivery/defects/${defectId}/retest`, { passed, retester }),
  observeRelease: (projectId: string, releaseId: string, healthy: boolean, metricsJson: string) =>
    post<Schema['ReleaseRecord']>(`/v1/projects/${projectId}/delivery/releases/${releaseId}/observe`, { healthy, metricsJson }),
  terminateProject: (projectId: string, retrospective: Schema['Retrospective']) =>
    post<Schema['Retrospective']>(`/v1/projects/${projectId}/delivery/terminate`, retrospective),
  governance: () => request<Schema['GovernanceState']>('/v1/governance'),
  registerTool: (tool: Schema['ToolManifest']) => post<Schema['ToolManifest']>('/v1/governance/tools', tool),
  admitTool: (id: string, reviewer: string) => post<Schema['ToolAdmissionReview']>(`/v1/governance/tools/${id}/admit`, { reviewer }),
  authorizeTool: (body: Schema['ToolAuthorizationRequest']) => post<Schema['ToolAuthorizationResult']>('/v1/governance/tool-calls/authorize', body),
  scanInjection: (content: string) => post<Schema['InjectionScanResult']>('/v1/governance/injections/scan', { content }),
  redactDlp: (content: string) => post<Schema['DlpRedactionResult']>('/v1/governance/dlp/redact', { content }),
  reviewContent: (contentRef: string, content: string) => post<Schema['ContentSafetyFinding']>('/v1/governance/content-safety/review', { contentRef, content }),
  appealContent: (findingId: string, reason: string) => post<Schema['ContentSafetyFinding']>('/v1/governance/content-safety/appeals', { findingId, reason, requester: 'Owner' }),
  resolveContentAppeal: (findingId: string, upheld: boolean) => post<Schema['ContentSafetyFinding']>(`/v1/governance/content-safety/findings/${findingId}/resolve`, { upheld }),
  sealIncidentEvidence: (incidentId: string, evidence: string) => post<Schema['SecurityIncident']>(`/v1/governance/incidents/${incidentId}/evidence`, { evidence }),
  verifyIncidentEvidence: (incidentId: string) => request<Schema['EvidenceVerificationDto']>(`/v1/governance/incidents/${incidentId}/evidence/verify`),
  createIncident: (incident: Schema['SecurityIncident']) => post<Schema['SecurityIncident']>('/v1/governance/incidents', incident),
  containIncident: (id: string, action: string) => post<Schema['SecurityIncident']>(`/v1/governance/incidents/${id}/contain`, { action }),
  recoverIncident: (id: string, summary: string) => post<Schema['SecurityIncident']>(`/v1/governance/incidents/${id}/recover`, { summary }),
  closeIncident: (id: string, retrospective: string) => post<Schema['SecurityIncident']>(`/v1/governance/incidents/${id}/close`, { retrospective }),
  createApprovalDelegation: (body: Schema['ApprovalDelegation']) => post<Schema['ApprovalDelegation']>('/v1/governance/approval-delegations', body),
  lifecycle: () => request<Schema['DataLifecycleState']>('/v1/lifecycle'),
  softDelete: (body: Schema['SoftDeleteRequest']) => post<Schema['DeletionRequestRecord']>('/v1/lifecycle/deletions', body),
  restoreDeletion: (id: string) => post<Schema['DeletionRequestRecord']>(`/v1/lifecycle/deletions/${id}/restore`),
  purgeDeletion: (id: string, confirmation: string) => post<Schema['DeletionProof']>(`/v1/lifecycle/deletions/${id}/purge`, { confirmation }),
  createLegalHold: (hold: Schema['LegalHoldRecord']) => post<Schema['LegalHoldRecord']>('/v1/lifecycle/legal-holds', hold),
  releaseLegalHold: (id: string) => post<Schema['LegalHoldRecord']>(`/v1/lifecycle/legal-holds/${id}/release`),
  createRetentionPolicy: (policy: Schema['RetentionPolicyRecord']) => post<Schema['RetentionPolicyRecord']>('/v1/lifecycle/retention-policies', policy),
  createArchive: (period: string, tier: string, recordCount: number) => post<Schema['ArchiveManifest']>('/v1/lifecycle/archives', { period, tier, recordCount }),
  restoreArchive: (id: string) => post<Schema['ArchiveManifest']>(`/v1/lifecycle/archives/${id}/restore`),
  createBackup: (profile: string, rpoMinutes: number, rtoMinutes: number) => post<Schema['BackupRunRecord']>('/v1/lifecycle/backups', { profile, rpoMinutes, rtoMinutes }),
  verifyRestore: (id: string) => post<Schema['BackupRunRecord']>(`/v1/lifecycle/backups/${id}/verify-restore`),
  recordRights: (rights: Schema['RightsRecord']) => post<Schema['RightsRecord']>('/v1/lifecycle/rights', rights),
  recordLicense: (license: Schema['LicenseRecord']) => post<Schema['LicenseRecord']>('/v1/lifecycle/licenses', license),
  modelPool: () => request<Schema['ModelPoolState']>('/v1/models/pool'),
  setManualPrice: (price: Schema['ManualModelPrice']) => post<Schema['ManualModelPrice']>('/v1/models/prices', price),
  assignModelTier: (assignment: Schema['ModelTierAssignment']) => post<Schema['ModelTierAssignment']>('/v1/models/tiers', assignment),
  bindModel: (binding: Schema['EmployeeModelBinding']) => post<Schema['EmployeeModelBinding']>('/v1/models/bindings', binding),
  testModel: (provider: string, modelId: string) => post<Schema['ModelTestResult']>('/v1/models/test', { provider, modelId }),
  runtimePolicy: () => request<Schema['RuntimePolicyState']>('/v1/runtime-policy'),
  recordCost: (cost: Schema['CostEntry']) => post<Schema['CostEntry']>('/v1/runtime-policy/costs', cost),
  costSummary: (projectId?: string) => request<Schema['CostSummaryDto']>(`/v1/runtime-policy/costs/summary${projectId ? `?projectId=${projectId}` : ''}`),
  setBudget: (budget: Schema['BudgetPolicy']) => post<Schema['BudgetPolicy']>('/v1/runtime-policy/budgets', budget),
  updateSchedule: (schedule: Schema['WorkSchedule']) => put<Schema['WorkSchedule']>('/v1/runtime-policy/schedule', schedule),
  updateCapacity: (capacity: Schema['CapacityPolicy']) => put<Schema['CapacityPolicy']>('/v1/runtime-policy/capacity', capacity),
  evaluationQa: () => request<Schema['EvaluationQaState']>('/v1/evaluation/qa'),
  runQaGate: (body: Schema['EvaluationGateRequest']) => post<Schema['EvaluationQaGate']>('/v1/evaluation/qa/gate', body),
  recruitmentInterviews: (candidateId?: string) => request<Schema['InterviewStageDto'][]>(`/v1/recruitment/interviews${candidateId ? `?candidateId=${candidateId}` : ''}`),
  recordInterview: (stage: Schema['InterviewStageDto']) => post<Schema['InterviewStageDto']>('/v1/recruitment/interviews', stage),
  recruitmentAppeals: () => request<Schema['CandidateAppealDto'][]>('/v1/recruitment/appeals'),
  createCandidateAppeal: (candidateId: string, reason: string) => post<Schema['CandidateAppealDto']>('/v1/recruitment/appeals', { id: crypto.randomUUID(), candidateId, reason }),
  resolveCandidateAppeal: (id: string, resolution: string) => post<Schema['CandidateAppealDto']>(`/v1/recruitment/appeals/${id}/resolve`, { resolution }),
  fairnessAudits: () => request<Schema['FairnessAuditDto'][]>('/v1/recruitment/fairness-audits'),
  runFairnessAudit: (positionId: string, period: string, sampleSize: number, selectionRateRatio: number) =>
    post<Schema['FairnessAuditDto']>('/v1/recruitment/fairness-audits', { positionId, period, sampleSize, selectionRateRatio }),
  performanceActions: () => request<Schema['PerformanceActionRecord'][]>('/v1/performance-actions'),
  performAction: (employeeId: string, actionType: string, reason: string, approvalId?: string) =>
    post<Schema['PerformanceActionRecord']>('/v1/performance-actions', { employeeId, actionType, reason, approvalId }),
  appealPerformanceAction: (id: string, reason: string) => post<Schema['PerformanceActionRecord']>(`/v1/performance-actions/${id}/appeal`, { reason }),
  resolvePerformanceAppeal: (id: string, upheld: boolean) =>
    post<Schema['PerformanceActionRecord']>(`/v1/performance-actions/${id}/appeal/resolve`, {
      reviewer: 'Independent Reviewer', resolution: upheld ? 'Original action upheld' : 'Action reversed after review', upheld
    }),
  improvements: () => request<Schema['ImprovementProposal'][]>('/v1/improvements'),
  proposeImprovement: (employeeId: string, category: string, summary: string, expectedBenefit: string) =>
    post<Schema['ImprovementProposal']>('/v1/improvements', { employeeId, category, summary, expectedBenefit }),
  reviewImprovement: (id: string, approved: boolean, reviewer = 'Leader') =>
    post<Schema['ImprovementProposal']>(`/v1/improvements/${id}/review`, {
      approved, reviewer, note: approved ? 'Evidence supports adoption' : 'More evidence required'
    }),
  agentRuntimeContext: (body: Schema['BuildContextRequest']) => post<Schema['AgentContextSnapshot']>('/v1/agent-runtime/context', body),
  runToolLoop: (body: Schema['ToolLoopRequest']) => post<Schema['ToolLoopResult']>('/v1/agent-runtime/tool-loop', body),
  taskGovernance: () => request<Schema['TaskGovernanceState']>('/v1/task-governance'),
  registerTaskType: (body: Schema['TaskTypeDefinition']) => post<Schema['TaskTypeDefinition']>('/v1/task-governance/task-types', body),
  assessCapability: (body: Schema['CapabilityAssessment']) => post<Schema['CapabilityAssessment']>('/v1/task-governance/capabilities', body),
  scoreClaim: (body: Schema['ClaimScoreRequest']) => post<Schema['ClaimScoreDto']>('/v1/task-governance/claim-score', body),
  recordWork: (body: Schema['WorkLedgerEntry']) => post<Schema['WorkLedgerEntry']>('/v1/task-governance/ledger', body),
  reassignTask: (taskId: string, employeeId: string, approvalId: string) =>
    post<Schema['TaskAssignmentRecord']>(`/v1/task-governance/tasks/${taskId}/reassign`, {
      employeeId, approvalId, reason: 'Approved workload reassignment'
    }),
  createReviewTask: (body: Schema['ReviewTaskRecord']) => post<Schema['ReviewTaskRecord']>('/v1/task-governance/reviews', body),
  approveReviewTask: (id: string, approver: string) => post<Schema['ReviewTaskRecord']>(`/v1/task-governance/reviews/${id}/approve`, { approver }),
  agentEvolution: () => request<Schema['AgentEvolutionState']>('/v1/agent-evolution'),
  createPrompt: (name: string, content: string, kind = 'Operations') =>
    post<Schema['PromptBundle']>('/v1/agent-evolution/prompts', { name, content, kind }),
  createOnboardingBaseline: (employeeId: string, promptBundleId: string) =>
    post<Schema['OnboardingBaseline']>('/v1/agent-evolution/onboarding', { employeeId, promptBundleId }),
  publishAgentAsset: (assetType: 'Prompt' | 'Skill', assetId: string) =>
    post<Schema['PublicAsset']>('/v1/agent-evolution/assets', { assetType, assetId, publisher: 'Owner' }),
  activatePrompt: (id: string) => post<Schema['PromptBundle']>(`/v1/agent-evolution/prompts/${id}/activate`),
  createSkill: (name: string, promptBundleId: string, configJson: string) => post<Schema['SkillDefinition']>('/v1/agent-evolution/skills', { name, promptBundleId, configJson }),
  discloseTools: (capabilities: string[], restrictedActivated: boolean) => post<Schema['ToolDisclosureResult']>('/v1/agent-evolution/tools/disclose', { capabilities, restrictedActivated }),
  activateSkill: (id: string) => post<Schema['SkillDefinition']>(`/v1/agent-evolution/skills/${id}/activate`),
  setToolPolicy: (policy: Schema['ToolDisclosurePolicy']) => post<Schema['ToolDisclosurePolicy']>('/v1/agent-evolution/tool-policies', policy),
  createDebate: (body: Schema['DebateTaskRecord']) => post<Schema['DebateTaskRecord']>('/v1/task-governance/debates', body),
  decideDebate: (id: string, decision: string) => post<Schema['DebateTaskRecord']>(`/v1/task-governance/debates/${id}/decide`, { decision }),
  adjudicate: (body: Schema['AdjudicationTaskRecord']) => post<Schema['AdjudicationTaskRecord']>('/v1/task-governance/adjudications', body)
};
