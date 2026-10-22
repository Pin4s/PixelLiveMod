/** Keep the useful connection diagnostics without dumping HTTP headers or cookies. */
export function formatTikTokError(value: unknown): string {
  const seen = new Set<object>();
  const describe = (input: unknown, depth: number): string => {
    if (input === null || input === undefined) return String(input);
    if (typeof input !== 'object') return String(input);
    if (seen.has(input)) return '[circular error]';
    if (depth > 4) return '[nested error]';
    seen.add(input);

    const record = input as Record<string, unknown>;
    const info = typeof record.info === 'string' ? record.info : undefined;
    if (record.exception !== undefined) {
      return `${info ? `${info}: ` : ''}${describe(record.exception, depth + 1)}`;
    }

    const name = input instanceof Error ? input.constructor.name :
      (typeof record.name === 'string' ? record.name : 'Error');
    const message = input instanceof Error ? input.message :
      (typeof record.message === 'string' ? record.message : 'Unknown error');
    const details = [`${name}: ${message}`];
    const code = record.code;
    if (typeof code === 'string' || typeof code === 'number') details.push(`code=${code}`);
    const response = record.response;
    if (response && typeof response === 'object') {
      const status = (response as Record<string, unknown>).statusCode;
      if (typeof status === 'number') details.push(`httpStatus=${status}`);
    }
    const config = record.config;
    if (config && typeof config === 'object') {
      const { routeId, requestErr, requestErrs } = config as Record<string, unknown>;
      if (typeof routeId === 'string') details.push(`route=${routeId}`);
      if (requestErr !== undefined) details.push(`cause: ${describe(requestErr, depth + 1)}`);
      if (Array.isArray(requestErrs)) {
        requestErrs.forEach((error, index) => details.push(`source ${index + 1}: ${describe(error, depth + 1)}`));
      }
    }
    if (record.cause !== undefined) details.push(`cause: ${describe(record.cause, depth + 1)}`);
    return details.join('; ');
  };
  return describe(value, 0);
}

/** Honor the sign server's retry-after value when a connection is rate limited. */
export function retryDelayForTikTokError(value: unknown, fallbackMs = 30000): number {
  const seen = new Set<object>();
  let current = value;
  while (current && typeof current === 'object' && !seen.has(current)) {
    seen.add(current);
    const error = current as Record<string, unknown>;
    if (typeof error.retryAfter === 'number' && Number.isFinite(error.retryAfter) && error.retryAfter > 0)
      return Math.min(Math.max(Math.ceil(error.retryAfter), fallbackMs), 2147483647);
    current = error.exception ?? error.cause;
  }
  return fallbackMs;
}
