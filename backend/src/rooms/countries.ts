/** الدول العربية المدعومة (رموز ISO 3166-1 alpha-2). مصدر وحيد للتحقق في الخادم. */
export const ARAB_COUNTRIES = [
  'AE', 'BH', 'DJ', 'DZ', 'EG', 'IQ', 'JO', 'KM', 'KW', 'LB', 'LY', 'MA', 'MR',
  'OM', 'PS', 'QA', 'SA', 'SD', 'SO', 'SY', 'TN', 'YE',
] as const;

const SET: ReadonlySet<string> = new Set(ARAB_COUNTRIES);

export function isSupportedCountry(code: string): boolean {
  return SET.has(code);
}
