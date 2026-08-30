import { zhCnMessages } from "@/lib/i18n/messages/zh-cn";

export const localeStorageKey = "asterloom-locale";
export const localeCookieName = "asterloom-locale";
export const supportedLocales = ["en", "zh-CN"] as const;

export type Locale = (typeof supportedLocales)[number];
export type TranslationValues = Readonly<Record<string, string | number>>;

let activeLocale: Locale = "en";

const dynamicMessages = Object.entries(zhCnMessages)
  .filter(([message]) => /\{\d+\}/.test(message))
  .map(([message, translation]) => {
    const placeholders: string[] = [];
    const expression = message
      .split(/(\{\d+\})/g)
      .map((part) => {
        const placeholder = /^\{(\d+)\}$/.exec(part);
        if (placeholder) {
          placeholders.push(placeholder[1]);
          return "(.+?)";
        }
        return part.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
      })
      .join("");
    return { pattern: new RegExp(`^${expression}$`), placeholders, translation };
  });

export function normalizeLocale(value: string | null | undefined): Locale {
  const normalized = value?.trim().toLowerCase();
  return normalized === "zh" || normalized?.startsWith("zh-")
    ? "zh-CN"
    : "en";
}

export function setActiveLocale(locale: Locale): void {
  activeLocale = locale;
}

export function getActiveLocale(): Locale {
  return activeLocale;
}

export function translate(
  message: string,
  values?: TranslationValues,
): string {
  return translateForLocale(activeLocale, message, values);
}

export function translateForLocale(
  locale: Locale,
  message: string,
  values?: TranslationValues,
): string {
  const normalized = message.replace(/\s+/g, " ").trim();
  let result = locale === "zh-CN"
    ? zhCnMessages[normalized] ?? translateTemplate(normalized) ?? translatePattern(normalized)
    : normalized;

  if (values) {
    for (const [key, value] of Object.entries(values)) {
      result = result.replaceAll(`{${key}}`, String(value));
    }
  }
  return result;
}

function translateTemplate(message: string): string | undefined {
  for (const { pattern, placeholders, translation } of dynamicMessages) {
    const match = pattern.exec(message);
    if (!match) continue;
    let result = translation;
    placeholders.forEach((placeholder, index) => {
      result = result.replaceAll(`{${placeholder}}`, match[index + 1]);
    });
    return result;
  }
  return undefined;
}

function translatePattern(message: string): string {
  let match = /^Exported (\d+) (.+)\.$/.exec(message);
  if (match) return `已导出 ${match[1]} 条${match[2] === "analytics events" ? "分析事件" : "记录"}。`;

  match = /^Archive tenant (.+)\?$/.exec(message);
  if (match) return `确定归档租户 ${match[1]} 吗？`;
  match = /^Archive application (.+)\?$/.exec(message);
  if (match) return `确定归档应用 ${match[1]} 吗？`;
  match = /^Archive environment (.+)\?$/.exec(message);
  if (match) return `确定归档环境 ${match[1]} 吗？`;
  match = /^Archive (.+)\?$/.exec(message);
  if (match) return `确定归档 ${match[1]} 吗？`;
  match = /^Delete scope (.+)\?$/.exec(message);
  if (match) return `确定删除 Scope ${match[1]} 吗？`;
  match = /^Permanently delete (.+)\?$/.exec(message);
  if (match) return `确定永久删除 ${match[1]} 吗？`;
  match = /^Rotate the secret for (.+)\?$/.exec(message);
  if (match) return `确定轮换 ${match[1]} 的 Secret 吗？`;
  match = /^Remove (.+) from (.+)\?$/.exec(message);
  if (match) return `确定从 ${match[2]} 移除 ${match[1]} 吗？`;
  match = /^Remove membership (.+)\?$/.exec(message);
  if (match) return `确定移除成员 ${match[1]} 吗？`;

  return message;
}
