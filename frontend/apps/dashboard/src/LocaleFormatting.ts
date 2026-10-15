import { getLocale, getTimeZone, localeTag, type Locale } from './LocaleStore';

function decimal(value: string, scale: bigint, digits: number, currency: 'USD' | 'UZS', locale: Locale) {
  const amount = BigInt(value); const magnitude = amount < 0n ? -amount : amount;
  const whole = formatCount(magnitude / scale, locale);
  const fraction = (magnitude % scale).toString().padStart(scale.toString().length - 1, '0').slice(0, digits);
  const separator = locale === 'uz' ? ',' : new Intl.NumberFormat(localeTag(locale)).formatToParts(1.1).find(part => part.type === 'decimal')?.value ?? '.';
  const body = `${whole}${digits ? separator + fraction : ''}`;
  const sign = amount < 0n ? '-' : '';
  return currency === 'USD' && locale === 'en' ? `${sign}$${body}` : `${sign}${body} ${currency}`;
}
// Never coerce monetary Int64 strings through Number, even for formatting.
export function usdFromMicro(value: string, locale = getLocale()) {
  const amount = BigInt(value); const magnitude = amount < 0n ? -amount : amount;
  return decimal(value, 1_000_000n, magnitude > 0n && magnitude < 10_000n ? 6 : 2, 'USD', locale);
}
export const usageUsdFromMicro = (value: string, locale = getLocale()) => decimal(value, 1_000_000n, 6, 'USD', locale);
export const uzsFromTiyin = (value: string, locale = getLocale()) => decimal(value, 100n, BigInt(value) % 100n === 0n ? 0 : 2, 'UZS', locale);
export function formatCount(value: number | bigint, locale = getLocale()): string {
  if (locale !== 'uz') return new Intl.NumberFormat(localeTag(locale)).format(value);
  // Some Chromium builds advertise uz but ship root-locale numeric/date data.
  // Pin Latin Uzbek grouping/decimal symbols rather than displaying M10 dates
  // or silently using US financial separators. BigInt stays an exact integer.
  const canonical = typeof value === 'bigint' ? value.toString() : new Intl.NumberFormat('en-US', { useGrouping: false, maximumFractionDigits: 20 }).format(value);
  const [whole, fraction] = canonical.split('.');
  return whole.replace(/\B(?=(\d{3})+(?!\d))/g, '\u00a0') + (fraction ? ',' + fraction : '');
}
// Catalog rates can have sub-micro precision. Retain every significant digit.
export function formatPriceMicro(value: string, locale = getLocale()) {
  const [whole, fraction = ''] = value.split('.'); const digits = 6 + fraction.length;
  const amount = BigInt(whole + fraction); const scale = 10n ** BigInt(digits);
  const tail = ((amount < 0n ? -amount : amount) % scale).toString().padStart(digits, '0').replace(/0+$/, '');
  return decimal(amount.toString(), scale, Math.max(2, tail.length), 'USD', locale);
}
export function formatDateTime(value: Date | string, dateOnly = false) {
  const date = value instanceof Date ? value : new Date(value);
  if (!Number.isFinite(date.getTime())) return '—';
  const utc = getTimeZone() === 'utc';
  if (getLocale() === 'uz') {
    const months = ['yanvar', 'fevral', 'mart', 'aprel', 'may', 'iyun', 'iyul', 'avgust', 'sentabr', 'oktabr', 'noyabr', 'dekabr'];
    const day = utc ? date.getUTCDate() : date.getDate(); const month = utc ? date.getUTCMonth() : date.getMonth(); const year = utc ? date.getUTCFullYear() : date.getFullYear();
    const time = [utc ? date.getUTCHours() : date.getHours(), utc ? date.getUTCMinutes() : date.getMinutes(), utc ? date.getUTCSeconds() : date.getSeconds()].map(value => String(value).padStart(2, '0')).join(':');
    return `${day}-${months[month]} ${year}${dateOnly ? '' : ', ' + time}${utc ? ' UTC' : ''}`;
  }
  return new Intl.DateTimeFormat(localeTag(getLocale()), { dateStyle: 'medium', ...(dateOnly ? {} : { timeStyle: 'medium' }), ...(utc ? { timeZone: 'UTC' } : {}) }).format(date) + (utc ? ' UTC' : '');
}
