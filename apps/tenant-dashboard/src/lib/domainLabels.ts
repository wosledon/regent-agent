import { useCallback } from 'react';
import { useTranslation } from 'react-i18next';

const labelKeys: Record<string, string> = {
  active: 'common.domain.active', inactive: 'common.domain.inactive', open: 'common.domain.open', closed: 'common.domain.closed', paused: 'common.domain.paused',
  acknowledged: 'common.domain.acknowledged', allow: 'common.domain.allow', archived: 'common.domain.archived', assigned: 'common.domain.assigned',
  briefready: 'common.domain.briefReady', building: 'common.domain.building', candidate: 'common.domain.candidate', disputed: 'common.domain.disputed',
  charge: 'common.domain.charge', credit: 'common.domain.credit', refund: 'common.domain.refund',
  expired: 'common.domain.expired', forgotten: 'common.domain.forgotten', isolated: 'common.domain.isolated', iterating: 'common.domain.iterating',
  observing: 'common.domain.observing', ratelimited: 'common.domain.rateLimited', read: 'common.domain.read', releasing: 'common.domain.releasing',
  sleeping: 'common.domain.sleeping', superseded: 'common.domain.superseded', terminated: 'common.domain.terminated', unavailable: 'common.domain.unavailable',
  verifying: 'common.domain.verifying', block: 'common.domain.block',
  frozen: 'common.domain.frozen', draft: 'common.domain.draft', admitted: 'common.domain.admitted', pending: 'common.domain.pending', paid: 'common.domain.paid', proposed: 'common.domain.proposed',
  requested: 'common.domain.requested', accepted: 'common.domain.accepted', inprogress: 'common.domain.inProgress',
  suspended: 'common.domain.suspended', readytoresume: 'common.domain.readyToResume',
  completed: 'common.domain.completed', resolved: 'common.domain.resolved', rejected: 'common.domain.rejected',
  cancelled: 'common.domain.cancelled', resumed: 'common.domain.resumed', approved: 'common.domain.approved',
  adopted: 'common.domain.adopted', pilot: 'common.domain.pilot', personal: 'common.domain.personal', nominated: 'common.domain.nominated',
  quarantined: 'common.domain.quarantined', appealed: 'common.domain.appealed', released: 'common.domain.released',
  corrected: 'common.domain.corrected', pendingreview: 'common.domain.pendingReview', underreview: 'common.domain.underReview',
  contained: 'common.domain.contained', recovered: 'common.domain.recovered', softdeleted: 'common.domain.softDeleted',
  restored: 'common.domain.restored', validated: 'common.domain.validated', invalid: 'common.domain.invalid', eligible: 'common.domain.eligible', blocked: 'common.domain.blocked',
  hired: 'common.domain.hired', none: 'common.domain.none', summary: 'common.domain.summary', unassigned: 'common.domain.unassigned', unallocated: 'common.domain.unallocated', unpriced: 'common.domain.unpriced',
  priced: 'common.domain.priced', member: 'common.domain.member', coowner: 'common.domain.coOwner', owner: 'common.domain.owner',
  participant: 'common.domain.participant', observer: 'common.domain.observer',
  controller: 'common.domain.controller', operator: 'common.domain.operator', leader: 'common.domain.leader',
  standard: 'common.domain.standard', emergency: 'common.domain.emergency', password: 'common.domain.password',
  development: 'common.domain.development', oidc: 'common.domain.oidc', github: 'common.domain.github',
  employee: 'common.domain.employee', conversation: 'common.domain.conversation', group: 'common.domain.group', project: 'common.domain.project',
  agent: 'common.domain.agent', system: 'common.domain.system', user: 'common.domain.user', message: 'common.domain.message',
  audit: 'common.domain.audit', task: 'common.domain.task', approval: 'common.domain.approval', delegation: 'common.domain.delegation',
  handoff: 'common.domain.handoff', help: 'common.domain.help', interrupt: 'common.domain.interrupt',
  daily: 'common.domain.daily', weekly: 'common.domain.weekly', monthly: 'common.domain.monthly', manual: 'common.domain.manual',
  budget: 'common.domain.budget', incident: 'common.domain.incident', custom: 'common.domain.custom',
  personal_scope: 'common.domain.personalScope', organization: 'common.domain.organization', knowledge_base: 'common.domain.knowledgeBase',
  trusted: 'common.domain.trusted', untrusted: 'common.domain.untrusted', poisoned: 'common.domain.poisoned',
  local: 'common.domain.local', openai: 'common.domain.openAI', anthropic: 'common.domain.anthropic',
  cheap: 'common.domain.cheap', mid: 'common.domain.mid', premium: 'common.domain.premium', low: 'common.domain.low',
  medium: 'common.domain.medium', high: 'common.domain.high', critical: 'common.domain.critical',
  p0: 'common.domain.p0', p1: 'common.domain.p1', p2: 'common.domain.p2', p3: 'common.domain.p3',
  sev1: 'common.domain.sev1', sev2: 'common.domain.sev2', sev3: 'common.domain.sev3', sev4: 'common.domain.sev4',
  primary: 'common.domain.primary', auxiliary: 'common.domain.auxiliary', restricted: 'common.domain.restricted',
  forbidden: 'common.domain.forbidden', onboarding: 'common.domain.onboarding', operations: 'common.domain.operations',
  recruitment: 'common.domain.recruitment', baseline: 'common.domain.baseline', strict: 'common.domain.strict',
  custom_profile: 'common.domain.customProfile', standard_audit: 'common.domain.standardAudit', worm: 'common.domain.worm',
  anchored: 'common.domain.anchored', tenant: 'common.domain.tenant', account: 'common.domain.account', archive: 'common.domain.archive',
  cold: 'common.domain.cold', unknown: 'common.domain.unknown', cleared: 'common.domain.cleared', silent: 'common.domain.silent',
  coaching: 'common.domain.coaching', transfer: 'common.domain.transfer', demotion: 'common.domain.demotion',
  freeze: 'common.domain.freeze', terminate: 'common.domain.terminate', reinstate: 'common.domain.reinstate',
  process: 'common.domain.process', skill: 'common.domain.skill', tool: 'common.domain.tool', model: 'common.domain.model',
  screening: 'common.domain.screening', written: 'common.domain.written', practical: 'common.domain.practical',
  interview: 'common.domain.interview', offer: 'common.domain.offer', go: 'common.domain.go', nogo: 'common.domain.noGo',
  healthy: 'common.domain.healthy', degraded: 'common.domain.degraded', live: 'common.domain.live', fresh: 'common.domain.fresh',
  stale: 'common.domain.stale', on_track: 'common.domain.onTrack', at_risk: 'common.domain.atRisk',
  pending_acceptance: 'common.domain.pendingAcceptance', failed: 'common.domain.failed', backlog: 'common.domain.backlog',
  ready: 'common.domain.ready', readyforretest: 'common.domain.readyForRetest', new: 'common.domain.new', reopened: 'common.domain.reopened', expanded: 'common.domain.expanded',
  fixed: 'common.domain.fixed', inreview: 'common.domain.inReview', busy: 'common.domain.busy', direct: 'common.domain.direct',
  correction: 'common.domain.correction', storage: 'common.domain.storage', withinquota: 'common.domain.withinQuota',
  exceeded: 'common.domain.exceeded', pass: 'common.domain.pass', fail: 'common.domain.fail',
  running: 'common.domain.running', claimed: 'common.domain.claimed', done: 'common.domain.completed',
  filled: 'common.domain.filled', assessed: 'common.domain.assessed', offered: 'common.domain.offered',
  readyforreview: 'common.domain.readyForReview', reassigned: 'common.domain.reassigned', decided: 'common.domain.decided',
  adjudicated: 'common.domain.adjudicated', interrupted: 'common.domain.interrupted', needmorecontext: 'common.domain.needMoreContext',
  answered: 'common.domain.answered', allocated: 'common.domain.allocated', available: 'common.domain.available',
  used: 'common.domain.used', revoked: 'common.domain.revoked', purged: 'common.domain.purged',
  restorerequired: 'common.domain.restoreRequired', restoreverified: 'common.domain.restoreVerified',
  rolledback: 'common.domain.rolledBack', rolledbackrestore: 'common.domain.rolledBackRestore',
  review: 'common.review', company: 'common.company', position: 'common.position', topic: 'common.domain.topic',
  work: 'common.domain.work', coordination: 'common.domain.coordination', observability: 'common.domain.observability',
  chat: 'common.domain.chat', taskassignment: 'common.domain.taskAssignment', delegationrequest: 'common.domain.delegationRequest',
  helprequest: 'common.domain.helpRequest', handoffrequest: 'common.domain.handoffRequest', reviewrequest: 'common.domain.reviewRequest',
  decisionrequest: 'common.domain.decisionRequest', discussion: 'common.domain.discussion', ack: 'common.domain.acknowledged',
  reject: 'common.reject', statusupdate: 'common.domain.statusUpdate', complete: 'common.domain.completed'
};

function canonicalDomainValue(value: string): string {
  return value.trim()
    .replace(/([A-Z]+)([A-Z][a-z])/g, '$1 $2')
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '');
}

const normalizedLabelKeys = new Map(Object.entries(labelKeys).map(([value, key]) => [canonicalDomainValue(value), key]));

export const domainLabelKeys = Object.values(labelKeys);

export function domainLabelKey(value: string): string | null {
  return normalizedLabelKeys.get(canonicalDomainValue(value)) ?? null;
}

export function useDomainLabels() {
  const { t } = useTranslation();
  return useCallback((value: string | null | undefined) => {
    if (!value) return t('common.unknown');
    const key = domainLabelKey(value);
    return key ? t(key) : t('common.unknown');
  }, [t]);
}
