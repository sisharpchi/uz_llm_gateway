import { useEffect, useSyncExternalStore } from 'react';
import { useLocation } from 'react-router-dom';
import { t } from './Translation';
export { t } from './Translation';
import { getLocale, getPreferenceSnapshot, getTimeZone, localeTag, setLocale, setTimeZone, subscribePreferences } from './LocaleStore';
import './I18n.css';

export function usePreferences() {
  useSyncExternalStore(subscribePreferences, getPreferenceSnapshot, () => 'en:local');
  const locale = getLocale(); const timeZone = getTimeZone();
  useEffect(() => { document.documentElement.lang = localeTag(locale); }, [locale]);
  return { locale, timeZone };
}

export function LocaleControls() {
  const { locale, timeZone } = usePreferences();
  return <section className="locale-controls" aria-label={t('Display preferences')}>
    <label>{t('Language')}<select value={locale} onChange={event => setLocale(event.target.value as typeof locale)}>
      <option value="en" lang="en">English</option><option value="uz" lang="uz-Latn">O‘zbekcha</option><option value="ru" lang="ru">Русский</option></select></label>
    <label>{t('Display time zone')}<select value={timeZone} onChange={event => setTimeZone(event.target.value as typeof timeZone)}>
      <option value="local">{t('Local time')}</option><option value="utc">UTC</option></select></label>
  </section>;
}

export function RouteFocus() {
  const { pathname } = useLocation();
  useEffect(() => {
    let main = document.querySelector<HTMLElement>('main');
    main?.focus({ preventScroll: true });
    // Async authentication replaces the loading main. Do not leave focus on
    // body, or steal it from an input/dialog the user has already reached.
    const observer = new MutationObserver(() => {
      const next = document.querySelector<HTMLElement>('main');
      if (next && next !== main && document.activeElement === document.body) next.focus({ preventScroll: true });
      main = next;
    });
    observer.observe(document.body, { childList: true, subtree: true });
    return () => observer.disconnect();
  }, [pathname]);
  return null;
}
