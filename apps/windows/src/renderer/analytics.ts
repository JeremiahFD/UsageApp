import type { UsageHistoryBucket } from "../shared/desktop";

export const DATE_RANGE_PRESETS = [
  { id: "today", label: "Today" },
  { id: "7d", label: "7 days" },
  { id: "30d", label: "30 days" },
  { id: "90d", label: "90 days" },
  { id: "all", label: "All" },
  { id: "custom", label: "Custom" },
] as const;

export type DateRangePreset = (typeof DATE_RANGE_PRESETS)[number]["id"];

export interface CalendarDateRange {
  preset: DateRangePreset;
  /** Inclusive YYYY-MM-DD calendar key, or null for an open boundary. */
  startDate: string | null;
  /** Inclusive YYYY-MM-DD calendar key, or null for an open boundary. */
  endDate: string | null;
}

export interface CustomDateRange {
  startDate?: string | null;
  endDate?: string | null;
}

interface CalendarDateParts {
  year: number;
  month: number;
  day: number;
}

const DATE_KEY_PATTERN = /^(\d{4})-(\d{2})-(\d{2})$/;

function isLeapYear(year: number): boolean {
  return year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0);
}

function daysInMonth(year: number, month: number): number {
  if (month === 2) return isLeapYear(year) ? 29 : 28;
  return [4, 6, 9, 11].includes(month) ? 30 : 31;
}

function parseCalendarDateKey(value: string): CalendarDateParts | null {
  const match = DATE_KEY_PATTERN.exec(value);
  if (!match) return null;

  const year = Number(match[1]);
  const month = Number(match[2]);
  const day = Number(match[3]);
  if (
    year < 1 ||
    year > 9_999 ||
    month < 1 ||
    month > 12 ||
    day < 1 ||
    day > daysInMonth(year, month)
  ) {
    return null;
  }

  return { year, month, day };
}

function formatCalendarDate(parts: CalendarDateParts): string {
  return [
    String(parts.year).padStart(4, "0"),
    String(parts.month).padStart(2, "0"),
    String(parts.day).padStart(2, "0"),
  ].join("-");
}

export function isCalendarDateKey(value: string): boolean {
  return parseCalendarDateKey(value) !== null;
}

/**
 * Returns the local calendar date without converting through UTC. This keeps a
 * late-night local date from becoming the previous or next day.
 */
export function localDateKey(date: Date = new Date()): string {
  if (Number.isNaN(date.getTime())) {
    throw new RangeError("Cannot create a calendar key from an invalid date.");
  }

  return formatCalendarDate({
    year: date.getFullYear(),
    month: date.getMonth() + 1,
    day: date.getDate(),
  });
}

/**
 * Performs Gregorian calendar arithmetic directly on YYYY-MM-DD parts. It
 * deliberately avoids parsing a date key as a timestamp, so DST and UTC
 * conversion cannot move the result to an adjacent calendar day.
 */
export function addCalendarDays(dateKey: string, amount: number): string {
  const parsed = parseCalendarDateKey(dateKey);
  if (!parsed) {
    throw new RangeError(`Invalid calendar date key: ${dateKey}`);
  }
  if (!Number.isInteger(amount)) {
    throw new RangeError("Calendar-day offsets must be whole numbers.");
  }

  let { year, month, day } = parsed;
  const direction = Math.sign(amount);
  let remaining = Math.abs(amount);

  while (remaining > 0) {
    if (direction > 0) {
      day += 1;
      if (day > daysInMonth(year, month)) {
        day = 1;
        month += 1;
        if (month > 12) {
          month = 1;
          year += 1;
        }
      }
    } else {
      day -= 1;
      if (day < 1) {
        month -= 1;
        if (month < 1) {
          month = 12;
          year -= 1;
        }
        if (year < 1) {
          throw new RangeError("Calendar arithmetic exceeded year 0001.");
        }
        day = daysInMonth(year, month);
      }
    }
    remaining -= 1;
  }

  if (year > 9_999) {
    throw new RangeError("Calendar arithmetic exceeded year 9999.");
  }
  return formatCalendarDate({ year, month, day });
}

function requireCalendarBoundary(
  value: string | null | undefined,
  name: "startDate" | "endDate",
): string | null {
  if (value === null || value === undefined || value === "") return null;
  if (!isCalendarDateKey(value)) {
    throw new RangeError(`${name} must use a valid YYYY-MM-DD calendar key.`);
  }
  return value;
}

function todayKey(today: Date | string): string {
  if (typeof today === "string") {
    if (!isCalendarDateKey(today)) {
      throw new RangeError("today must use a valid YYYY-MM-DD calendar key.");
    }
    return today;
  }
  return localDateKey(today);
}

/**
 * Resolves an inclusive range. A seven-day preset includes today plus the six
 * preceding calendar days. Reversed custom boundaries are normalized.
 */
export function resolveDateRange(
  preset: DateRangePreset,
  today: Date | string = new Date(),
  custom: CustomDateRange = {},
): CalendarDateRange {
  if (preset === "all") {
    return { preset, startDate: null, endDate: null };
  }

  if (preset === "custom") {
    let startDate = requireCalendarBoundary(custom.startDate, "startDate");
    let endDate = requireCalendarBoundary(custom.endDate, "endDate");
    if (startDate && endDate && startDate > endDate) {
      [startDate, endDate] = [endDate, startDate];
    }
    return { preset, startDate, endDate };
  }

  const endDate = todayKey(today);
  const dayCount =
    preset === "today"
      ? 1
      : preset === "7d"
        ? 7
        : preset === "30d"
          ? 30
          : 90;
  return {
    preset,
    startDate: addCalendarDays(endDate, -(dayCount - 1)),
    endDate,
  };
}

export function isDateInRange(
  dateKey: string,
  range: Pick<CalendarDateRange, "startDate" | "endDate">,
): boolean {
  if (!isCalendarDateKey(dateKey)) return false;
  return (
    (range.startDate === null || dateKey >= range.startDate) &&
    (range.endDate === null || dateKey <= range.endDate)
  );
}

export interface UsageFilterOptions {
  models: string[];
  reasoningLevels: string[];
}

function normalizedDimension(value: string | null): string | null {
  const normalized = value?.trim() ?? "";
  return normalized.length > 0 ? normalized : null;
}

function sortedUnique(values: Iterable<string>): string[] {
  return [...new Set(values)].sort((left, right) =>
    left.localeCompare(right, undefined, {
      numeric: true,
      sensitivity: "base",
    }),
  );
}

export function getUsageFilterOptions(
  buckets: readonly UsageHistoryBucket[],
): UsageFilterOptions {
  const models: string[] = [];
  const reasoningLevels: string[] = [];

  for (const bucket of buckets) {
    const model = normalizedDimension(bucket.model);
    const reasoningLevel = normalizedDimension(bucket.reasoningLevel);
    if (model !== null) models.push(model);
    if (reasoningLevel !== null) reasoningLevels.push(reasoningLevel);
  }

  return {
    models: sortedUnique(models),
    reasoningLevels: sortedUnique(reasoningLevels),
  };
}

export interface UsageBucketFilters {
  range?: CalendarDateRange | null;
  /**
   * Undefined, null, or an empty array means all models. Null-valued source
   * dimensions match only when no model filter is active.
   */
  models?: readonly string[] | null;
  /**
   * Undefined, null, or an empty array means all reasoning levels.
   */
  reasoningLevels?: readonly string[] | null;
}

function selectedDimensions(
  selection: readonly string[] | null | undefined,
): Set<string> | null {
  if (!selection || selection.length === 0) return null;
  const selected = new Set(
    selection
      .map((value) => normalizedDimension(value))
      .filter((value): value is string => value !== null),
  );
  return selected.size > 0 ? selected : null;
}

export function filterUsageBuckets(
  buckets: readonly UsageHistoryBucket[],
  filters: UsageBucketFilters = {},
): UsageHistoryBucket[] {
  const models = selectedDimensions(filters.models);
  const reasoningLevels = selectedDimensions(filters.reasoningLevels);

  return buckets.filter((bucket) => {
    if (!isCalendarDateKey(bucket.date)) return false;
    if (filters.range && !isDateInRange(bucket.date, filters.range)) return false;

    const model = normalizedDimension(bucket.model);
    if (models && (model === null || !models.has(model))) return false;

    const reasoningLevel = normalizedDimension(bucket.reasoningLevel);
    if (
      reasoningLevels &&
      (reasoningLevel === null || !reasoningLevels.has(reasoningLevel))
    ) {
      return false;
    }

    return true;
  });
}

export interface UsageMetricTotals {
  totalTokens: number;
  inputTokens: number | null;
  outputTokens: number | null;
  cacheReadTokens: number | null;
  cacheWriteTokens: number | null;
  reasoningTokens: number | null;
  estimatedCostUsd: number | null;
  requestCount: number | null;
}

export interface UsageSummary extends UsageMetricTotals {
  bucketCount: number;
  dayCount: number;
}

export interface AggregatedUsageDay extends UsageMetricTotals {
  date: string;
  bucketCount: number;
}

type NullableMetric =
  | "inputTokens"
  | "outputTokens"
  | "cacheReadTokens"
  | "cacheWriteTokens"
  | "reasoningTokens"
  | "estimatedCostUsd"
  | "requestCount";

/**
 * A nullable metric is complete only when every selected source bucket
 * supplies it. This avoids presenting a partial sum as a real total.
 */
function sumCompleteMetric(
  buckets: readonly UsageHistoryBucket[],
  metric: NullableMetric,
): number | null {
  if (buckets.length === 0) return null;

  let total = 0;
  for (const bucket of buckets) {
    const value = bucket[metric];
    if (value === null) return null;
    total += value;
  }
  return total;
}

function metricTotals(
  buckets: readonly UsageHistoryBucket[],
): UsageMetricTotals {
  return {
    totalTokens: buckets.reduce(
      (total, bucket) => total + bucket.totalTokens,
      0,
    ),
    inputTokens: sumCompleteMetric(buckets, "inputTokens"),
    outputTokens: sumCompleteMetric(buckets, "outputTokens"),
    cacheReadTokens: sumCompleteMetric(buckets, "cacheReadTokens"),
    cacheWriteTokens: sumCompleteMetric(buckets, "cacheWriteTokens"),
    reasoningTokens: sumCompleteMetric(buckets, "reasoningTokens"),
    estimatedCostUsd: sumCompleteMetric(buckets, "estimatedCostUsd"),
    requestCount: sumCompleteMetric(buckets, "requestCount"),
  };
}

export function summarizeUsageBuckets(
  buckets: readonly UsageHistoryBucket[],
): UsageSummary {
  return {
    ...metricTotals(buckets),
    bucketCount: buckets.length,
    dayCount: new Set(buckets.map((bucket) => bucket.date)).size,
  };
}

export function aggregateUsageByDate(
  buckets: readonly UsageHistoryBucket[],
): AggregatedUsageDay[] {
  const grouped = new Map<string, UsageHistoryBucket[]>();
  for (const bucket of buckets) {
    if (!isCalendarDateKey(bucket.date)) continue;
    const existing = grouped.get(bucket.date);
    if (existing) {
      existing.push(bucket);
    } else {
      grouped.set(bucket.date, [bucket]);
    }
  }

  return [...grouped.entries()]
    .sort(([left], [right]) => left.localeCompare(right))
    .map(([date, dateBuckets]) => ({
      date,
      bucketCount: dateBuckets.length,
      ...metricTotals(dateBuckets),
    }));
}

export interface SelectedUsageAnalytics {
  selectedBuckets: UsageHistoryBucket[];
  daily: AggregatedUsageDay[];
  summary: UsageSummary;
}

export function selectAndAggregateUsage(
  buckets: readonly UsageHistoryBucket[],
  filters: UsageBucketFilters = {},
): SelectedUsageAnalytics {
  const selectedBuckets = filterUsageBuckets(buckets, filters);
  return {
    selectedBuckets,
    daily: aggregateUsageByDate(selectedBuckets),
    summary: summarizeUsageBuckets(selectedBuckets),
  };
}

export interface ChartDatum {
  key: string;
  label: string;
  value: number | null;
}

export interface ChartScaleOptions {
  formatValue?: (value: number) => string;
  unavailableLabel?: string;
}

export interface ScaledChartPoint extends ChartDatum {
  /** Horizontal position in the inclusive 0-100 chart coordinate space. */
  xPercent: number;
  /** Vertical position where 0 is the top; null means unavailable. */
  yPercent: number | null;
  ariaLabel: string;
}

export interface ChartBarScaleOptions extends ChartScaleOptions {
  /** Portion of each equal-width band reserved as whitespace. */
  gapRatio?: number;
  /** Smallest visible height for a positive value. */
  minimumPositivePercent?: number;
}

export interface ScaledChartBar extends ChartDatum {
  xPercent: number;
  widthPercent: number;
  heightPercent: number | null;
  /** Position of the top of the bar; null means unavailable. */
  yPercent: number | null;
  ariaLabel: string;
}

function normalizedChartValue(value: number | null): number | null {
  if (value === null || !Number.isFinite(value)) return null;
  return Math.max(0, value);
}

export function getChartMaximum(
  values: readonly (number | null)[],
): number {
  let maximum = 0;
  for (const rawValue of values) {
    const value = normalizedChartValue(rawValue);
    if (value !== null) maximum = Math.max(maximum, value);
  }
  return maximum;
}

function chartAriaLabel(
  datum: ChartDatum,
  value: number | null,
  options: ChartScaleOptions,
): string {
  if (value === null) {
    return `${datum.label}: ${options.unavailableLabel ?? "unavailable"}`;
  }
  return `${datum.label}: ${options.formatValue?.(value) ?? String(value)}`;
}

export function scaleChartPoints(
  data: readonly ChartDatum[],
  options: ChartScaleOptions = {},
): ScaledChartPoint[] {
  const values = data.map((datum) => normalizedChartValue(datum.value));
  const maximum = getChartMaximum(values);
  const lastIndex = data.length - 1;

  return data.map((datum, index) => {
    const value = values[index] ?? null;
    const xPercent = lastIndex <= 0 ? 50 : (index / lastIndex) * 100;
    const yPercent =
      value === null ? null : maximum === 0 ? 100 : 100 - (value / maximum) * 100;
    return {
      ...datum,
      xPercent,
      yPercent,
      ariaLabel: chartAriaLabel(datum, value, options),
    };
  });
}

function clamp(value: number, minimum: number, maximum: number): number {
  return Math.min(maximum, Math.max(minimum, value));
}

export function scaleChartBars(
  data: readonly ChartDatum[],
  options: ChartBarScaleOptions = {},
): ScaledChartBar[] {
  if (data.length === 0) return [];

  const values = data.map((datum) => normalizedChartValue(datum.value));
  const maximum = getChartMaximum(values);
  const gapRatio = clamp(options.gapRatio ?? 0.24, 0, 0.95);
  const minimumPositivePercent = clamp(
    options.minimumPositivePercent ?? 2,
    0,
    100,
  );
  const bandWidth = 100 / data.length;
  const widthPercent = bandWidth * (1 - gapRatio);
  const sideGap = (bandWidth - widthPercent) / 2;

  return data.map((datum, index) => {
    const value = values[index] ?? null;
    let heightPercent: number | null = null;
    if (value !== null) {
      const proportionalHeight =
        maximum === 0 ? 0 : (value / maximum) * 100;
      heightPercent =
        value > 0
          ? Math.max(minimumPositivePercent, proportionalHeight)
          : 0;
    }

    return {
      ...datum,
      xPercent: index * bandWidth + sideGap,
      widthPercent,
      heightPercent,
      yPercent: heightPercent === null ? null : 100 - heightPercent,
      ariaLabel: chartAriaLabel(datum, value, options),
    };
  });
}
