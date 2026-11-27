import ts from 'typescript';
import { describe, expect, it } from 'vitest';
import i18n from './i18n';
import { businessCopy } from './businessTranslations';
import { commonLocalizationCopy } from './commonLocalization';
import { technicalCopy } from './businessZhTranslations';
import { domainLabelKey, domainLabelKeys } from './domainLabels';

const visibleAttributes = new Set(['title', 'description', 'label', 'hint', 'placeholder', 'message', 'alt', 'aria-label', 'aria-description', 'aria-valuetext', 'ariaLabel']);
const visibleCopyProperties = new Set(['header', 'label', 'message', 'title', 'hint', 'placeholder', 'alt', 'aria-label', 'aria-description', 'aria-valuetext']);
const localizedPrimitiveAdapters: Record<string, string> = {
  DataTable: 'LocalizedDataTable',
  ErrorState: 'LocalizedErrorState',
  LoadingState: 'LocalizedLoadingState',
  TimelineEntry: 'LocalizedTimelineEntry',
  GraphCanvas: 'LocalizedGraphCanvas'
};
const adapterFiles = new Set([
  '../features/common/LocalizedDataTable.tsx',
  '../features/common/LocalizedErrorState.tsx',
  '../features/common/LocalizedLoadingState.tsx',
  '../features/common/LocalizedTimelineEntry.tsx',
  '../features/collaboration/LocalizedGraphCanvas.tsx'
]);
const featureSources = import.meta.glob('../features/**/*.tsx', { query: '?raw', import: 'default', eager: true }) as Record<string, string>;
const featureHelperSources = import.meta.glob('../features/**/*.ts', { query: '?raw', import: 'default', eager: true }) as Record<string, string>;
const settingsSource = import.meta.glob('../pages/SettingsPage.tsx', { query: '?raw', import: 'default', eager: true }) as Record<string, string>;

function resourceKeys(value: unknown, prefix = ''): string[] {
  if (typeof value === 'string') return [prefix];
  if (!value || typeof value !== 'object' || Array.isArray(value)) return [];
  return Object.entries(value).flatMap(([key, child]) => resourceKeys(child, prefix ? `${prefix}.${key}` : key));
}

function resourceMap(value: unknown, prefix = ''): Record<string, string> {
  if (typeof value === 'string') return { [prefix]: value };
  if (!value || typeof value !== 'object' || Array.isArray(value)) return {};
  return Object.assign({}, ...Object.entries(value).map(([key, child]) =>
    resourceMap(child, prefix ? `${prefix}.${key}` : key)));
}

function interpolationKeys(value: string): string[] {
  return [...value.matchAll(/{{\s*([^{}]+?)\s*}}/g)].map(match => match[1]).sort();
}

function staticText(
  node: ts.Node | undefined,
  bindings: ReadonlyMap<string, string> = new Map(),
  seen: ReadonlySet<string> = new Set()
): string {
  if (!node) return '';
  if (ts.isJsxExpression(node) || ts.isParenthesizedExpression(node) || ts.isAsExpression(node) || ts.isNonNullExpression(node)) {
    return staticText(node.expression, bindings, seen);
  }
  if (ts.isStringLiteral(node) || ts.isNoSubstitutionTemplateLiteral(node)) return node.text;
  if (ts.isTemplateExpression(node)) return [node.head.text, ...node.templateSpans.map(span => span.literal.text)].join('');
  if (ts.isConditionalExpression(node)) {
    return `${staticText(node.whenTrue, bindings, seen)}${staticText(node.whenFalse, bindings, seen)}`;
  }
  if (ts.isBinaryExpression(node)) {
    if (node.operatorToken.kind === ts.SyntaxKind.PlusToken) {
      return `${staticText(node.left, bindings, seen)}${staticText(node.right, bindings, seen)}`;
    }
    if ([ts.SyntaxKind.AmpersandAmpersandToken, ts.SyntaxKind.BarBarToken, ts.SyntaxKind.QuestionQuestionToken].includes(node.operatorToken.kind)) {
      return staticText(node.right, bindings, seen);
    }
  }
  if (ts.isIdentifier(node) && !seen.has(node.text)) {
    return bindings.get(node.text) ?? '';
  }
  return '';
}

function literalBindings(source: ts.SourceFile): Map<string, string> {
  const bindings = new Map<string, string>();
  const visit = (node: ts.Node) => {
    if (ts.isVariableDeclaration(node) && ts.isIdentifier(node.name) && node.initializer) {
      const value = staticText(node.initializer);
      if (value) bindings.set(node.name.text, value);
    }
    ts.forEachChild(node, visit);
  };
  visit(source);
  return bindings;
}

function literalKeys(node: ts.Node | undefined): string[] {
  if (!node) return [];
  if (ts.isStringLiteral(node) || ts.isNoSubstitutionTemplateLiteral(node)) return [node.text];
  const keys: string[] = [];
  ts.forEachChild(node, child => { keys.push(...literalKeys(child)); });
  return keys;
}

function containsAuthoredCopy(value: string): boolean {
  return /[A-Za-z\u4e00-\u9fff]/.test(value) && !technicalCopy.has(value.trim());
}

function isTechnicalOrDataValue(value: string): boolean {
  const trimmed = value.trim();
  if (!trimmed || technicalCopy.has(trimmed)) return true;
  if (/^(?:https?:|keyvault:|[a-z][a-z0-9+.-]*:\/\/)/i.test(trimmed)) return true;
  if (/^[\s\d\p{P}\p{S}]+$/u.test(trimmed)) return true;
  return /^\{\{[^{}]+\}\}$/.test(trimmed);
}

function containsFormattedCount(node: ts.Node): boolean {
  let found = false;
  const visit = (child: ts.Node) => {
    if (ts.isCallExpression(child)) {
      const expression = child.expression;
      if (ts.isPropertyAccessExpression(expression) && ['number', 'toLocaleString', 'toString'].includes(expression.name.text)) found = true;
      if (ts.isIdentifier(expression) && expression.text === 'String') found = true;
    }
    ts.forEachChild(child, visit);
  };
  visit(node);
  return found;
}

const scopedSources = Object.entries({ ...featureSources, ...featureHelperSources, ...settingsSource })
  .filter(([file]) => !/\.test\.tsx?$/.test(file));
const scopedFiles = Object.entries({ ...featureSources, ...settingsSource }).filter(([file]) => !file.endsWith('.test.tsx'));
const resultRenderingMutations = new Set([
  'features/butler/ButlerPage.tsx:ask',
  'features/governance/GovernancePage.tsx:scan',
  'features/governance/GovernancePage.tsx:redact',
  'features/governance/GovernancePage.tsx:verifyEvidence',
  'features/workflows/WorkflowsPage.tsx:start',
  'features/workflows/WorkflowsPage.tsx:advance',
  'features/models/ModelsPage.tsx:test',
  'features/agents/AgentGovernancePage.tsx:disclose'
]);

describe('Tenant Dashboard localization audit', () => {
  it('has recursive zh-CN/en-US resource and interpolation parity and no missing scoped keys', async () => {
    await i18n.changeLanguage('en-US');
    const chinese = resourceMap(i18n.getResourceBundle('zh-CN', 'translation'));
    const english = resourceMap(i18n.getResourceBundle('en-US', 'translation'));
    expect(resourceKeys(chinese).sort()).toEqual(resourceKeys(english).sort());
    for (const key of Object.keys(chinese)) {
      expect(interpolationKeys(english[key]), `${key} interpolation`).toEqual(interpolationKeys(chinese[key]));
    }

    for (const [file, text] of scopedSources) {
      const source = ts.createSourceFile(file, text, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
      const visit = (node: ts.Node) => {
        if (ts.isCallExpression(node) && node.expression.getText(source) === 't' && node.arguments[0]) {
          for (const key of literalKeys(node.arguments[0])) {
            expect(i18n.exists(key), `${file}: ${key}`).toBe(true);
          }
        }
        ts.forEachChild(node, visit);
      };
      visit(source);
    }
    for (const key of [
      ...domainLabelKeys,
      'common.freshness.live', 'common.freshness.fresh', 'common.freshness.stale', 'common.freshness.unknown',
      'common.noRecords', 'common.nextAction', 'common.domain.invalid', 'collaborationPage.employeeSubtitle', 'collaborationPage.conversationSubtitle',
      'collaborationPage.projectSubtitle', 'collaborationPage.taskCount', 'collaborationPage.memberCount',
      'messagesPage.messageCount', 'collaborationPage.graphNodeLabel', 'collaborationPage.nodeStatus',
      'deliveryPage.generatedTaskTitle', 'butlerPage.intent', 'settingsPage.chinese',
      'runtimePolicyPage.defaultStartTime', 'runtimePolicyPage.defaultEndTime'
    ]) expect(i18n.exists(key), key).toBe(true);
  });

  it('uses language-neutral semantic business keys', () => {
    for (const key of Object.keys(businessCopy)) {
      expect(key).toMatch(/^[a-z][A-Za-z0-9]*(?:\.[A-Za-z0-9]+)+$/);
      expect(key).not.toMatch(/[\u4e00-\u9fff\s]/);
    }
    expect(Object.keys(businessCopy)).not.toContain('settingsPage.简体中文');
    expect(Object.keys(businessCopy)).not.toContain('runtimePolicyPage.0900');
    expect(Object.keys(businessCopy)).not.toContain('runtimePolicyPage.1700');
  });

  it('provides non-empty Chinese and English forms for every business value', () => {
    for (const copy of Object.values({ ...commonLocalizationCopy, ...businessCopy })) {
      const forms = [copy.zh, copy.en].flatMap(value => typeof value === 'string' ? [value] : Object.values(value));
      for (const form of forms) expect(form.trim().length).toBeGreaterThan(0);
    }
  });

  it('contains no untranslated user-facing English in expanded zh-CN resources', async () => {
    await i18n.changeLanguage('zh-CN');
    const chinese = resourceMap(i18n.getResourceBundle('zh-CN', 'translation'));
    const findings = Object.entries(chinese).filter(([, value]) => {
      const prose = value.replace(/\{\{\s*[^{}]+?\s*\}\}/g, '');
      const hasEnglishWord = /[A-Za-z]/.test(prose);
      const hasChinese = /[\u4e00-\u9fff]/.test(prose);
      return hasEnglishWord && !hasChinese && !isTechnicalOrDataValue(value);
    });
    expect(findings).toEqual([]);

    const english = resourceMap(i18n.getResourceBundle('en-US', 'translation'));
    for (const key of [
      'collaborationPage.traceableEvents2', 'collaborationPage.task', 'messagesPage.members', 'messagesPage.seq'
    ]) {
      expect(chinese[key], key).toMatch(/[\u4e00-\u9fff]/);
      expect(chinese[key], key).not.toBe(english[key]);
      expect(technicalCopy.has(english[key]), key).toBe(false);
    }
  });

  it('contains no hardcoded authored JSX copy, nested ARIA copy, local constants or raw backend messages', () => {
    const findings: string[] = [];
    for (const [file, text] of scopedFiles) {
      const source = ts.createSourceFile(file, text, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
      const bindings = literalBindings(source);
      const visit = (node: ts.Node) => {
        const line = source.getLineAndCharacterOfPosition(node.getStart(source)).line + 1;
        if (ts.isJsxText(node)) {
          const value = node.text.replace(/\s+/g, ' ').trim();
          if (containsAuthoredCopy(value)) findings.push(`${file}:${line}: ${value}`);
        }
        if (ts.isJsxExpression(node) && !ts.isJsxAttribute(node.parent) && node.expression) {
          const value = staticText(node.expression, bindings);
          if (containsAuthoredCopy(value)) findings.push(`${file}:${line}: ${value}`);
          const inspectMessage = (child: ts.Node) => {
            if (ts.isPropertyAccessExpression(child) && child.name.text === 'message') {
              findings.push(`${file}:${line}: raw .message rendering`);
            }
            ts.forEachChild(child, inspectMessage);
          };
          inspectMessage(node.expression);
        }
        if (ts.isJsxAttribute(node) && visibleAttributes.has(node.name.getText(source)) && node.initializer) {
          const value = ts.isStringLiteral(node.initializer) ? node.initializer.text : staticText(node.initializer, bindings);
          if (containsAuthoredCopy(value) || ts.isTemplateExpression(node.initializer) && staticText(node.initializer, bindings).trim()) {
            findings.push(`${file}:${line}: ${value}`);
          }
        }
        if (ts.isPropertyAssignment(node)) {
          const propertyName = node.name.getText(source).replace(/^['"]|['"]$/g, '');
          if (visibleCopyProperties.has(propertyName)) {
            const value = staticText(node.initializer, bindings);
            if (containsAuthoredCopy(value)) findings.push(`${file}:${line}: ${value}`);
          }
        }
        ts.forEachChild(node, visit);
      };
      visit(source);
    }
    expect(findings).toEqual([]);
  });

  it('uses localized adapters for shared primitives with English defaults', () => {
    const findings: string[] = [];
    for (const [file, text] of scopedFiles) {
      if (adapterFiles.has(file)) continue;
      const source = ts.createSourceFile(file, text, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
      const visit = (node: ts.Node) => {
        if (ts.isImportDeclaration(node) && ts.isStringLiteral(node.moduleSpecifier) && node.moduleSpecifier.text === '@regent/ui-shared') {
          const bindings = node.importClause?.namedBindings;
          if (bindings && ts.isNamedImports(bindings)) {
            for (const element of bindings.elements) {
              const primitive = (element.propertyName ?? element.name).text;
              if (localizedPrimitiveAdapters[primitive]) findings.push(`${file}: ${primitive} -> ${localizedPrimitiveAdapters[primitive]}`);
            }
          }
        }
        ts.forEachChild(node, visit);
      };
      visit(source);
    }
    expect(findings).toEqual([]);
  });

  it('maps every literal domain label through an existing resource', () => {
    const findings: string[] = [];
    for (const [file, text] of scopedSources) {
      const source = ts.createSourceFile(file, text, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
      const visit = (node: ts.Node) => {
        if (ts.isCallExpression(node) && node.expression.getText(source) === 'domainLabel'
          && node.arguments[0] && ts.isStringLiteral(node.arguments[0])) {
          const value = node.arguments[0].text;
          if (!domainLabelKeys.includes(domainLabelKey(value) ?? '')) findings.push(`${file}: ${value}`);
        }
        ts.forEachChild(node, visit);
      };
      visit(source);
    }
    expect(findings).toEqual([]);
  });

  it('provides dynamic domain labels and never renders raw dynamic statuses', async () => {
    await i18n.changeLanguage('en-US');
    expect(domainLabelKey('Invalid')).toBe('common.domain.invalid');
    expect(i18n.t('common.domain.invalid')).toBe('Invalid');
    await i18n.changeLanguage('zh-CN');
    expect(i18n.t('common.domain.invalid')).toBe('无效');

    const findings = scopedFiles.flatMap(([file, text]) =>
      [...text.matchAll(/>\s*\{[\w.]+\.(?:status|severity|state|health|sourceType)\s*\}</g)]
        .map(match => `${file}:${text.slice(0, match.index).split('\n').length}`));
    expect(findings).toEqual([]);
  });

  it('passes raw numeric counts to plural selection instead of formatted strings', () => {
    const findings: string[] = [];
    for (const [file, text] of scopedSources) {
      const source = ts.createSourceFile(file, text, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
      const visit = (node: ts.Node) => {
        if (ts.isCallExpression(node)) {
          const expression = node.expression.getText(source);
          if (expression === 't' && node.arguments[1] && ts.isObjectLiteralExpression(node.arguments[1])) {
            for (const property of node.arguments[1].properties) {
              if (ts.isPropertyAssignment(property) && property.name.getText(source).replace(/^['"]|['"]$/g, '') === 'count'
                && containsFormattedCount(property.initializer)) findings.push(`${file}: formatted plural count`);
            }
          }
          if (expression === 'localizedCount' && node.arguments[2] && ts.isStringLiteral(node.arguments[2])) {
            const key = node.arguments[2].text;
            if (!i18n.exists(`${key}_one`) || !i18n.exists(`${key}_other`)) findings.push(`${file}: ${key} lacks plural resources`);
          }
          if (expression === 'localizedCount' && node.arguments[3] && containsFormattedCount(node.arguments[3])) {
            findings.push(`${file}: formatted localizedCount count`);
          }
        }
        ts.forEachChild(node, visit);
      };
      visit(source);
    }
    expect(findings).toEqual([]);
  });

  it('uses result-aware export validation and shared model-test error mapping', () => {
    const exportSource = featureSources['../features/billing/DataExportPanel.tsx'];
    expect(exportSource).toContain('showSuccess={validate.isSuccess && validate.data.valid === true}');
    expect(exportSource).toContain('validate.data.valid !== true');
    expect(exportSource).toContain("t('billingPage.exportPackageInvalid')");
    expect(exportSource).toContain("if (row.validationError?.trim()) return { label: domainLabel('Invalid')");
    expect(exportSource.indexOf('row.validationError')).toBeLessThan(exportSource.indexOf('row.validatedAt'));
    expect(exportSource.indexOf('row.validatedAt')).toBeLessThan(exportSource.indexOf('domainLabel(row.status)'));

    const modelsSource = featureSources['../features/models/ModelsPage.tsx'];
    const contractTestSource = modelsSource.slice(modelsSource.indexOf('function ModelContractTest'));
    expect(contractTestSource).toContain('<LocalizedMutationFeedback mutation={test} showSuccess={false} />');
    expect(contractTestSource).not.toContain('errorMessage=');
    expect(contractTestSource).not.toContain('modelsPage.contractTestFailed');
    expect(Object.keys(businessCopy)).not.toContain('modelsPage.contractTestFailed');
  });

  it('does not render Dashboard fixed-copy fields directly when stable keys are available', () => {
    const findings: string[] = [];
    for (const file of [
      '../features/dashboard/DashboardCard.tsx', '../features/dashboard/DashboardPage.tsx',
      '../features/governance/ActionCenterPage.tsx', '../features/journal/JournalPage.tsx'
    ]) {
      const text = featureSources[file];
      expect(text, file).toMatch(/resolve(?:DashboardCardPresentation|ActionCenterNextAction)/);
      for (const match of text.matchAll(/\b(?:card\.(?:title|summary|metric)|action\.nextAction|decision\.(?:summary|metric))\b/g)) {
        findings.push(`${file}: ${match[0]}`);
      }
    }
    expect(findings).toEqual([]);
  });

  it('renders localized feedback for every business mutation with explicit result-rendering exceptions', () => {
    const findings: string[] = [];
    for (const [file, text] of scopedFiles) {
      const source = ts.createSourceFile(file, text, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
      const normalizedFile = file.replace(/^\.\.\//, '');
      const mutations = new Set<string>();
      const feedback = new Map<string, boolean>();
      const visit = (node: ts.Node) => {
        if (ts.isVariableDeclaration(node) && ts.isIdentifier(node.name) && node.initializer
          && ts.isCallExpression(node.initializer) && node.initializer.expression.getText(source) === 'useMutation') {
          mutations.add(node.name.text);
        }
        if ((ts.isJsxOpeningElement(node) || ts.isJsxSelfClosingElement(node)) && node.tagName.getText(source) === 'LocalizedMutationFeedback') {
          const mutation = node.attributes.properties.find(attribute =>
            ts.isJsxAttribute(attribute) && attribute.name.getText(source) === 'mutation');
          const hasSuccessMessage = node.attributes.properties.some(attribute =>
            ts.isJsxAttribute(attribute) && attribute.name.getText(source) === 'successMessage');
          if (mutation && ts.isJsxAttribute(mutation) && mutation.initializer && ts.isJsxExpression(mutation.initializer)
            && mutation.initializer.expression && ts.isIdentifier(mutation.initializer.expression)) {
            feedback.set(mutation.initializer.expression.text, hasSuccessMessage);
          }
        }
        ts.forEachChild(node, visit);
      };
      visit(source);
      for (const mutation of mutations) {
        const key = `${normalizedFile}:${mutation}`;
        if (!feedback.has(mutation) || !feedback.get(mutation) && !resultRenderingMutations.has(key)) {
          findings.push(key);
        }
      }
    }
    expect(findings).toEqual([]);
  });

  it('does not use browser-default or ad-hoc date and number formatting in scoped pages', () => {
    const findings = scopedSources.flatMap(([file, text]) =>
      [...text.matchAll(/\.toLocale(?:String|DateString|TimeString)\s*\(|\.toFixed\s*\(|\bIntl\.(?:DateTimeFormat|NumberFormat)\s*\(/g)]
        .map(match => `${file}:${text.slice(0, match.index).split('\n').length}`));
    expect(findings).toEqual([]);
  });

  it('never renders raw missing keys', async () => {
    await i18n.changeLanguage('en-US');
    expect(i18n.t('localization.audit.missing.key')).toBe('');
  });
});
