export type CollaborationView = 'office' | 'org' | 'network' | 'timeline';
export const collaborationViews: readonly CollaborationView[] = ['office', 'org', 'network', 'timeline'];
export const collaborationStaleAfterMs = 5 * 60 * 1000;

export function isCollaborationView(value: string): value is CollaborationView {
  return collaborationViews.some(view => view === value);
}

export function collaborationFreshness(generatedAt: string | undefined, now = Date.now()): 'fresh' | 'stale' | 'unknown' {
  if (!generatedAt) return 'unknown';
  const generated = new Date(generatedAt).getTime();
  if (!Number.isFinite(generated)) return 'unknown';
  return now - generated > collaborationStaleAfterMs ? 'stale' : 'fresh';
}
