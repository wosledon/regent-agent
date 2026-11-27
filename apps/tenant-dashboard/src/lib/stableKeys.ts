export function stableOccurrenceKeys(values: readonly string[]): string[] {
  const occurrences = new Map<string, number>();
  return values.map(value => {
    const occurrence = (occurrences.get(value) ?? 0) + 1;
    occurrences.set(value, occurrence);
    return `${value}:${occurrence}`;
  });
}
