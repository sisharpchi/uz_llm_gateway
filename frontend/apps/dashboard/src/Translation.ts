import { messages } from './messages';
import { getLocale } from './LocaleStore';

// Only application-owned copy and closed display enums call this function.
// React escapes parameters; never translate user names, secrets or wire data.
export function t(source: string, values: Readonly<Record<string, string | number>> = {}): string {
  // Action feedback keeps canonical copy in state so changing language also
  // updates an existing error. Match only our bounded HTTP-status sentence.
  const requestError = /^Request failed \((\d{3})\)\. Please try again\.$/.exec(source);
  if (requestError) return t('Request failed ({p0}). Please try again.', { p0: requestError[1] });
  const key = source.trim(); const locale = getLocale();
  const translated = locale === 'en' ? key : messages[key]?.[locale === 'uz' ? 0 : 1] ?? key;
  const result = translated.replace(/\{(p\d+)\}/g, (placeholder, name: string) => Object.hasOwn(values, name) ? String(values[name]) : placeholder);
  return (source.match(/^\s*/)?.[0] ?? '') + result + (source.match(/\s*$/)?.[0] ?? '');
}
