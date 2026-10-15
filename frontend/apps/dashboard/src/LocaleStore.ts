export type Locale = 'en' | 'uz' | 'ru';
export type TimeZonePreference = 'local' | 'utc';
const localeKey = 'uzllm.ui.locale.v1';
const timeKey = 'uzllm.ui.timezone.v1';
const listeners = new Set<() => void>();
function read(key: string): string | null { try { return typeof window === 'undefined' ? null : window.localStorage.getItem(key); } catch { return null; } }
const validLocale = (value: unknown): value is Locale => value === 'en' || value === 'uz' || value === 'ru';
const storedLocale = read(localeKey);
let locale: Locale = validLocale(storedLocale) ? storedLocale : 'en';
let timeZone: TimeZonePreference = read(timeKey) === 'utc' ? 'utc' : 'local';
export const localeTag = (value: Locale) => ({ en: 'en-US', uz: 'uz-Latn-UZ', ru: 'ru-RU' })[value];
export const getLocale = () => locale;
export const getTimeZone = () => timeZone;
export const getPreferenceSnapshot = () => `${locale}:${timeZone}`;
export function subscribePreferences(listener: () => void) { listeners.add(listener); return () => { listeners.delete(listener); }; }
function notify() { for (const listener of listeners) listener(); }
export function setLocale(value: Locale) {
  if (!validLocale(value)) return;
  locale = value;
  try { window.localStorage.setItem(localeKey, value); } catch { /* Still usable for this tab. */ }
  notify();
}
export function setTimeZone(value: TimeZonePreference) {
  if (value !== 'local' && value !== 'utc') return;
  timeZone = value;
  try { window.localStorage.setItem(timeKey, value); } catch { /* Still usable for this tab. */ }
  notify();
}
if (typeof window !== 'undefined') window.addEventListener('storage', event => {
  if (event.key === localeKey || event.key === null) locale = validLocale(event.newValue) ? event.newValue : 'en';
  if (event.key === timeKey || event.key === null) timeZone = event.newValue === 'utc' ? 'utc' : 'local';
  if (event.key === localeKey || event.key === timeKey || event.key === null) notify();
});
