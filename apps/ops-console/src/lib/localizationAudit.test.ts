import ts from 'typescript';
import { describe, expect, it } from 'vitest';
import i18n from './i18n';
import { domainLabelKey, domainLabelKeys } from './domainLabels';

const visibleAttributes = new Set([
  'title', 'description', 'label', 'hint', 'placeholder', 'message', 'alt', 'primary', 'secondary',
  'aria-label', 'aria-description', 'aria-valuetext', 'ariaLabel', 'ariaDescription', 'ariaValueText'
]);
const visibleCopyProperties = new Set([
  'children', 'header', 'label', 'message', 'title', 'hint', 'placeholder', 'alt', 'primary', 'secondary',
  'description', 'emptyTitle', 'emptyDescription', 'tooltip', 'aria-label', 'aria-description',
  'aria-valuetext', 'ariaLabel', 'ariaDescription', 'ariaValueText'
]);
const rawStatusProperties = new Set([
  'status', 'severity', 'state', 'health', 'mode', 'scope', 'level', 'overallStatus', 'coverageStatus',
  'rpoStatus', 'rtoStatus'
]);
const expectedScopedFiles = [
  '../App.tsx', '../components/LocalizedStates.tsx', '../components/OperationsLink.tsx', '../pages/OverviewPage.tsx', '../pages/TenantsPage.tsx',
  '../pages/SupportPage.tsx', '../pages/BackupsPage.tsx', '../pages/AuditPage.tsx', '../pages/StatusPage.tsx',
  '../pages/ObservabilityPage.tsx'
];
const allowedFiles = new Set(expectedScopedFiles);
const existingOutOfScopeFiles = ['../pages/LoginPage.tsx'];
const sourceGlob = import.meta.glob(['../App.tsx', '../components/*.tsx', '../pages/*Page.tsx', '!../components/*.test.tsx'], {
  query: '?raw', import: 'default', eager: true
}) as Record<string, string>;
const discoveredFiles = Object.keys(sourceGlob).sort();
const scopedSources = Object.fromEntries(Object.entries(sourceGlob).filter(([file]) => allowedFiles.has(file)));
const scopedFiles = Object.entries(scopedSources);

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
  if (ts.isCallExpression(node) && node.expression.getText() === 'String' && node.arguments[0]) {
    return staticText(node.arguments[0], bindings, seen);
  }
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

function containsAuthoredCopy(value: string): boolean {
  return /[A-Za-z\u4e00-\u9fff]/.test(value);
}

function parse(file: string, text: string): ts.SourceFile {
  return ts.createSourceFile(file, text, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
}

function unwrapped(node: ts.Expression): ts.Expression {
  if (ts.isParenthesizedExpression(node) || ts.isAsExpression(node) || ts.isNonNullExpression(node) || ts.isSatisfiesExpression(node)) {
    return unwrapped(node.expression);
  }
  return node;
}

function isTranslationCall(node: ts.Expression): boolean {
  const expression = unwrapped(node);
  return ts.isCallExpression(expression) && ['t', 'i18n.t'].includes(expression.expression.getText());
}

function assemblesLabelValueSentence(node: ts.JsxElement): boolean {
  const colonIndex = node.children.findIndex(child => ts.isJsxText(child) && child.text.trim() === ':');
  if (colonIndex < 0) return false;
  const hasLabel = node.children.slice(0, colonIndex).some(child =>
    ts.isJsxExpression(child) && child.expression && isTranslationCall(child.expression));
  const hasValue = node.children.slice(colonIndex + 1).some(child => ts.isJsxExpression(child) && !!child.expression);
  return hasLabel && hasValue;
}

function rawPropertyName(node: ts.Expression): string | null {
  const expression = unwrapped(node);
  if (ts.isPropertyAccessExpression(expression)) return expression.name.text;
  if (ts.isElementAccessExpression(expression) && expression.argumentExpression && ts.isStringLiteralLike(expression.argumentExpression)) {
    return expression.argumentExpression.text;
  }
  return null;
}

function rawDynamicBindings(source: ts.SourceFile): Map<string, string> {
  const bindings = new Map<string, string>();
  const visit = (node: ts.Node) => {
    if (ts.isVariableDeclaration(node)) {
      if (ts.isIdentifier(node.name) && node.initializer) {
        const property = rawPropertyName(node.initializer);
        if (property && rawStatusProperties.has(property)) bindings.set(node.name.text, property);
      }
      if (ts.isObjectBindingPattern(node.name)) {
        for (const element of node.name.elements) {
          if (!ts.isIdentifier(element.name)) continue;
          const property = element.propertyName && ts.isIdentifier(element.propertyName) ? element.propertyName.text : element.name.text;
          if (rawStatusProperties.has(property)) bindings.set(element.name.text, property);
        }
      }
    }
    ts.forEachChild(node, visit);
  };
  visit(source);
  return bindings;
}

function isRawDynamicExpression(node: ts.Expression, bindings: ReadonlyMap<string, string>): boolean {
  const expression = unwrapped(node);
  if (ts.isConditionalExpression(expression)) {
    return isRawDynamicExpression(expression.whenTrue, bindings) || isRawDynamicExpression(expression.whenFalse, bindings);
  }
  if (ts.isTemplateExpression(expression)) {
    return expression.templateSpans.some(span => isRawDynamicExpression(span.expression, bindings));
  }
  if (ts.isCallExpression(expression) && expression.expression.getText() === 'String' && expression.arguments[0]) {
    return isRawDynamicExpression(expression.arguments[0], bindings);
  }
  const property = rawPropertyName(expression);
  if (property && rawStatusProperties.has(property)) return true;
  return ts.isIdentifier(expression) && bindings.has(expression.text);
}

function rendersBackendError(node: ts.Expression): boolean {
  const expression = unwrapped(node);
  if (ts.isConditionalExpression(expression)) {
    return rendersBackendError(expression.whenTrue) || rendersBackendError(expression.whenFalse);
  }
  if (ts.isTemplateExpression(expression)) {
    return expression.templateSpans.some(span => rendersBackendError(span.expression));
  }
  if (ts.isCallExpression(expression) && expression.expression.getText() === 'String' && expression.arguments[0]) {
    return rendersBackendError(expression.arguments[0]);
  }
  if (ts.isBinaryExpression(expression) && [ts.SyntaxKind.AmpersandAmpersandToken, ts.SyntaxKind.BarBarToken, ts.SyntaxKind.QuestionQuestionToken].includes(expression.operatorToken.kind)) {
    return rendersBackendError(expression.right);
  }
  if (ts.isPropertyAccessExpression(expression)) {
    if (expression.name.text === 'error') return true;
    return expression.name.text === 'message' && rendersBackendError(expression.expression);
  }
  if (ts.isElementAccessExpression(expression) && expression.argumentExpression && ts.isStringLiteralLike(expression.argumentExpression)) {
    const property = expression.argumentExpression.text;
    if (property === 'error') return true;
    return property === 'message' && rendersBackendError(expression.expression);
  }
  return ts.isIdentifier(expression) && expression.text === 'error';
}

function propertyName(node: ts.PropertyName | ts.BindingName): string {
  return ts.isIdentifier(node) || ts.isStringLiteralLike(node) || ts.isNumericLiteral(node) ? node.text : node.getText();
}

describe('Ops Console localization audit', () => {
  it('audits the exact scoped file set', () => {
    expect(discoveredFiles).toEqual([...expectedScopedFiles, ...existingOutOfScopeFiles].sort());
    expect(Object.keys(scopedSources).sort()).toEqual([...expectedScopedFiles].sort());
    expect(scopedFiles).toHaveLength(expectedScopedFiles.length);
  });

  it('has recursive resource and interpolation parity with no missing scoped keys', async () => {
    await i18n.changeLanguage('en-US');
    const chinese = resourceMap(i18n.getResourceBundle('zh-CN', 'translation'));
    const english = resourceMap(i18n.getResourceBundle('en-US', 'translation'));
    expect(i18n.options.fallbackLng).toEqual(['zh-CN']);
    expect(Object.keys(chinese).length).toBeGreaterThan(0);
    expect(Object.keys(english).length).toBeGreaterThan(0);
    expect(resourceKeys(chinese).sort()).toEqual(resourceKeys(english).sort());
    for (const key of Object.keys(chinese)) {
      expect(chinese[key].trim(), `${key} zh-CN empty resource`).not.toBe('');
      expect(english[key].trim(), `${key} en-US empty resource`).not.toBe('');
      expect(interpolationKeys(english[key]), `${key} interpolation`).toEqual(interpolationKeys(chinese[key]));
    }

    const pluralKeys = new Set<string>();
    for (const [file, text] of scopedFiles) {
      const source = parse(file, text);
      const visit = (node: ts.Node) => {
        if (ts.isCallExpression(node) && ['t', 'i18n.t'].includes(node.expression.getText(source)) && node.arguments[0]) {
          const key = node.arguments[0];
          if (ts.isStringLiteral(key)) expect(i18n.exists(key.text), `${file}: ${key.text}`).toBe(true);
        }
        if (ts.isCallExpression(node) && node.expression.getText(source) === 'localizedCount' && node.arguments[2]) {
          const key = node.arguments[2];
          if (ts.isStringLiteral(key)) pluralKeys.add(key.text);
        }
        ts.forEachChild(node, visit);
      };
      visit(source);
    }
    for (const key of domainLabelKeys) expect(i18n.exists(key), key).toBe(true);
    for (const key of pluralKeys) {
      for (const locale of ['zh-CN', 'en-US']) {
        for (const suffix of ['one', 'other']) {
          const resource = i18n.getResource(locale, 'translation', `${key}_${suffix}`);
          expect(resource, `${locale}: ${key}_${suffix}`).toEqual(expect.any(String));
          expect((resource as string).trim(), `${locale}: ${key}_${suffix}`).not.toBe('');
        }
      }
    }
  });

  it('contains no hardcoded authored copy in scoped JSX', () => {
    const findings: string[] = [];
    for (const [file, text] of scopedFiles) {
      const source = parse(file, text);
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
        }
        if (ts.isJsxAttribute(node) && visibleAttributes.has(node.name.getText(source)) && node.initializer) {
          const value = ts.isStringLiteral(node.initializer) ? node.initializer.text : staticText(node.initializer, bindings);
          if (containsAuthoredCopy(value)) findings.push(`${file}:${line}: ${value}`);
        }
        if (ts.isPropertyAssignment(node)) {
          const name = propertyName(node.name);
          if (visibleCopyProperties.has(name)) {
            const value = staticText(node.initializer, bindings);
            if (containsAuthoredCopy(value)) findings.push(`${file}:${line}: ${value}`);
          }
        }
        if (ts.isShorthandPropertyAssignment(node) && visibleCopyProperties.has(node.name.text)) {
          const value = staticText(node.name, bindings);
          if (containsAuthoredCopy(value)) findings.push(`${file}:${line}: ${value}`);
        }
        if (ts.isCallExpression(node)) {
          const expression = node.expression.getText(source);
          if (['confirm', 'alert', 'window.confirm', 'window.alert'].includes(expression)) {
            for (const argument of node.arguments) {
              const value = staticText(argument, bindings);
              if (containsAuthoredCopy(value)) findings.push(`${file}:${line}: ${value}`);
            }
          }
        }
        ts.forEachChild(node, visit);
      };
      visit(source);
    }
    expect(findings).toEqual([]);
  });

  it('does not assemble localized label/value sentences in JSX', () => {
    const findings: string[] = [];
    for (const [file, text] of scopedFiles) {
      const source = parse(file, text);
      const visit = (node: ts.Node) => {
        if (ts.isJsxElement(node) && assemblesLabelValueSentence(node)) {
          const line = source.getLineAndCharacterOfPosition(node.getStart(source)).line + 1;
          findings.push(`${file}:${line}: localized label/value sentence assembly`);
        }
        ts.forEachChild(node, visit);
      };
      visit(source);
    }
    expect(findings).toEqual([]);
  });

  it('never renders raw dynamic statuses or backend error fields', () => {
    const findings: string[] = [];
    for (const [file, text] of scopedFiles) {
      const source = parse(file, text);
      const rawBindings = rawDynamicBindings(source);
      const inspectExpression = (expression: ts.Expression, line: number) => {
        if (isRawDynamicExpression(expression, rawBindings)) findings.push(`${file}:${line}: raw dynamic domain label`);
        if (rendersBackendError(expression)) findings.push(`${file}:${line}: raw backend error`);
      };
      const visit = (node: ts.Node) => {
        const line = source.getLineAndCharacterOfPosition(node.getStart(source)).line + 1;
        if (ts.isJsxExpression(node) && !ts.isJsxAttribute(node.parent) && node.expression) {
          inspectExpression(node.expression, line);
        }
        if (ts.isJsxAttribute(node) && visibleAttributes.has(node.name.getText(source)) && node.initializer && ts.isJsxExpression(node.initializer) && node.initializer.expression) {
          inspectExpression(node.initializer.expression, line);
        }
        if (ts.isPropertyAssignment(node)) {
          const name = propertyName(node.name);
          if (visibleCopyProperties.has(name)) inspectExpression(node.initializer, line);
          if (name === 'render' && ts.isArrowFunction(node.initializer) && !ts.isBlock(node.initializer.body)) {
            inspectExpression(node.initializer.body, line);
          }
        }
        if (ts.isCallExpression(node) && ['t', 'i18n.t'].includes(node.expression.getText(source))) {
          const options = node.arguments[1];
          if (options && ts.isObjectLiteralExpression(options)) {
            for (const property of options.properties) {
              if (ts.isPropertyAssignment(property)) inspectExpression(property.initializer, line);
              if (ts.isShorthandPropertyAssignment(property)) inspectExpression(property.name, line);
            }
          }
        }
        if (ts.isCallExpression(node) && ['confirm', 'alert', 'window.confirm', 'window.alert'].includes(node.expression.getText(source))) {
          for (const argument of node.arguments) inspectExpression(argument, line);
        }
        ts.forEachChild(node, visit);
      };
      visit(source);
    }
    expect(findings).toEqual([]);
  });

  it('uses localized domain labels and plural resources for literal values', () => {
    const findings: string[] = [];
    for (const [file, text] of scopedFiles) {
      const source = parse(file, text);
      const visit = (node: ts.Node) => {
        if (ts.isCallExpression(node)) {
          const expression = node.expression.getText(source);
          if (expression === 'domainLabel' && node.arguments[0] && ts.isStringLiteral(node.arguments[0])) {
            const value = node.arguments[0].text;
            const key = domainLabelKey(value);
            if (!key || !domainLabelKeys.includes(key) || !i18n.exists(key)) findings.push(`${file}: ${value}`);
          }
          if (expression === 'localizedCount' && node.arguments[2] && ts.isStringLiteral(node.arguments[2])) {
            const key = node.arguments[2].text;
            if (!i18n.exists(`${key}_one`) || !i18n.exists(`${key}_other`)) findings.push(`${file}: ${key}`);
          }
        }
        ts.forEachChild(node, visit);
      };
      visit(source);
    }
    expect(findings).toEqual([]);
  });

  it('does not use browser-default or ad-hoc display formatting', () => {
    const findings = scopedFiles.flatMap(([file, text]) =>
      [...text.matchAll(/\.toLocale(?:String|DateString|TimeString)\s*\(|\.toFixed\s*\(|\*\s*100\b|new\s+Intl\.(?:NumberFormat|DateTimeFormat|ListFormat|RelativeTimeFormat)\b|`\$\{[^}]+\}(?:ms|m|s)`/g)]
        .map(match => `${file}:${text.slice(0, match.index).split('\n').length}`));
    expect(findings).toEqual([]);
  });

  it('never renders raw or blank missing keys', async () => {
    const missingKey = 'localization.audit.missing.key';
    await i18n.changeLanguage('en-US');
    expect(i18n.t(missingKey)).toBe('Unknown');
    expect(i18n.t(missingKey)).not.toBe(missingKey);
    expect(i18n.t(missingKey).trim()).not.toBe('');

    await i18n.changeLanguage('zh-CN');
    expect(i18n.t(missingKey)).toBe('未知');
  });
});
