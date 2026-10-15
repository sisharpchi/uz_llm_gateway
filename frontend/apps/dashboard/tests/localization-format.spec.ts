import { readFileSync, readdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import ts from 'typescript';
import { test, expect } from '@playwright/test';
import { messages } from '../src/messages';
import { t } from '../src/Translation';
import { getLocale, getTimeZone, setLocale, setTimeZone, subscribePreferences, type Locale } from '../src/LocaleStore';
import { formatCount, formatDateTime, formatPriceMicro, usageUsdFromMicro, usdFromMicro, uzsFromTiyin } from '../src/LocaleFormatting';

test.beforeEach(() => { setLocale('en'); setTimeZone('local'); });

test('catalog has both translations and preserves every interpolation parameter', () => {
  const parameters = (value: string) => [...value.matchAll(/\{p\d+\}/g)].map(match => match[0]).sort();
  expect(Object.keys(messages).length).toBeGreaterThan(800);
  for (const [key, translations] of Object.entries(messages)) {
    expect(translations, key).toHaveLength(2);
    for (const translation of translations) {
      expect(translation.trim(), key).not.toBe('');
      expect(parameters(translation), key).toEqual(parameters(key));
    }
  }
});

test('source-level catalog coverage rejects untranslated JSX, attributes and literal translation keys', () => {
  const root = fileURLToPath(new URL('../src/', import.meta.url));
  const technical = new Set(['U', 'UZLLM', 'GATEWAY', 'UZLLM GATEWAY', 'Payme', 'CLICK', 'BYOK', 'Hybrid', 'USD', 'UZS', 'UTC', 'UUID', 'English', 'O‘zbekcha', 'Русский']);
  const failures: string[] = [];
  const human = (text: string) => /[A-Za-z]{2}/.test(text) && !technical.has(text.trim()) && !/^https?:/.test(text);
  for (const file of readdirSync(root).filter(name => name.endsWith('.tsx'))) {
    const source = ts.createSourceFile(file, readFileSync(root + file, 'utf8'), ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
    function visit(node: ts.Node) {
      if (ts.isJsxText(node) && human(node.text.trim())) failures.push(`${file}: untranslated JSX ${node.text.trim()}`);
      if (ts.isJsxAttribute(node) && ['aria-label', 'placeholder', 'title', 'alt', 'label'].includes(node.name.getText(source)) && node.initializer && ts.isStringLiteral(node.initializer) && human(node.initializer.text))
        failures.push(`${file}: untranslated attribute ${node.initializer.text}`);
      if (ts.isCallExpression(node) && node.expression.getText(source) === 't' && node.arguments[0] && ts.isStringLiteralLike(node.arguments[0]) && !Object.hasOwn(messages, node.arguments[0].text.trim()))
        failures.push(`${file}: missing catalog entry ${node.arguments[0].text}`);
      if (ts.isNewExpression(node) && node.expression.getText(source) === 'Error' && node.arguments?.[0] && ts.isStringLiteralLike(node.arguments[0]) && !Object.hasOwn(messages, node.arguments[0].text.trim()))
        failures.push(`${file}: untranslated validation ${node.arguments[0].text}`);
      if (ts.isCallExpression(node)) {
        const name = node.expression.getText(source); const feedback = name === 'setError' || name === 'setSuccess' ? node.arguments[0] : name.endsWith('.run') ? node.arguments[2] : undefined;
        if (feedback && ts.isStringLiteralLike(feedback) && feedback.text.trim() && !Object.hasOwn(messages, feedback.text.trim())) failures.push(`${file}: untranslated feedback ${feedback.text}`);
      }
      ts.forEachChild(node, visit);
    }
    visit(source);
  }
  expect(failures).toEqual([]);
});

for (const locale of ['en', 'uz', 'ru'] as Locale[]) test(`${locale}: exact Int64 money, sub-micro prices and units never pass through Number`, () => {
  setLocale(locale);
  const group = new Intl.NumberFormat(locale === 'en' ? 'en-US' : locale === 'uz' ? 'uz-Latn-UZ' : 'ru-RU');
  const whole = group.format(9_223_372_036_854n);
  const separator = group.formatToParts(1.1).find(part => part.type === 'decimal')!.value;
  expect(usageUsdFromMicro('9223372036854775807')).toBe(locale === 'en' ? `$${whole}.775807` : `${whole}${separator}775807 USD`);
  expect(usageUsdFromMicro('-1')).toBe(locale === 'en' ? '-$0.000001' : `-0${separator}000001 USD`);
  expect(usdFromMicro('1999999')).toBe(locale === 'en' ? '$1.99' : `1${separator}99 USD`);
  expect(usdFromMicro('1')).toBe(locale === 'en' ? '$0.000001' : `0${separator}000001 USD`);
  expect(usdFromMicro('0')).toBe(locale === 'en' ? '$0.00' : `0${separator}00 USD`);
  expect(uzsFromTiyin('10000000')).toBe(`${group.format(100000)} UZS`);
  expect(uzsFromTiyin('1')).toBe(`0${separator}01 UZS`);
  expect(formatPriceMicro('0.125')).toBe(locale === 'en' ? '$0.000000125' : `0${separator}000000125 USD`);
  expect(formatCount(9007199254740993n)).toBe(group.format(9007199254740993n));
});

test('translation interpolates opaque values once; preference changes do not change UTC dates or raw data', () => {
  const value = '<img src=x>{p1}';
  setLocale('uz'); expect(t('Archive {p0}?', { p0: value })).toBe(`${value} arxivlansinmi?`);
  expect(t('missing-machine-code')).toBe('missing-machine-code');
  expect(t(' Captured ')).toBe(' Sarflangan ');
  setTimeZone('utc');
  expect(formatDateTime('2026-10-16T23:59:01Z')).toBe('16-oktabr 2026, 23:59:01 UTC');
  expect(t('Request failed (500). Please try again.')).toBe('So‘rov bajarilmadi (500). Qayta urinib ko‘ring.');
  setLocale('ru'); expect(t('Request failed (500). Please try again.')).toBe('Ошибка запроса (500). Повторите.');
  expect(formatDateTime('invalid')).toBe('—');
  expect(new Date('2026-10-16T23:59:01Z').toISOString()).toBe('2026-10-16T23:59:01.000Z');
});

test('invalid preferences are ignored and subscriptions unsubscribe without browser storage', () => {
  let notifications = 0; const unsubscribe = subscribePreferences(() => notifications++);
  setLocale('ru'); setTimeZone('utc');
  setLocale('attacker' as Locale); setTimeZone('elsewhere' as 'local');
  expect(getLocale()).toBe('ru'); expect(getTimeZone()).toBe('utc'); expect(notifications).toBe(2);
  unsubscribe(); setLocale('en'); expect(notifications).toBe(2);
});
