import { describe, expect, it } from 'vitest';
import {
  currencyError, finiteNumberError, integerRangeError, jsonError, nonNegativeNumberError, numberRangeError,
  positiveIntegerError, requiredTextError, specificTextError, timeError, timeZoneError
} from './frontendValidation';

describe('frontendValidation', () => {
  it('rejects blank, infinite and non-numeric amounts', () => {
    expect(finiteNumberError('', 'Amount')).toContain('required');
    expect(finiteNumberError('Infinity', 'Amount')).toContain('finite');
    expect(finiteNumberError('NaN', 'Amount')).toContain('finite');
    expect(nonNegativeNumberError('-0.01', 'Amount')).toContain('non-negative');
    expect(nonNegativeNumberError('0', 'Amount')).toBeNull();
    expect(numberRangeError('1.1', 'Score', 0, 1)).toContain('between 0 and 1');
    expect(numberRangeError('0.9', 'Score', 0, 1)).toBeNull();
  });

  it('validates positive and bounded integers', () => {
    expect(positiveIntegerError('0', 'Capacity')).toContain('positive integer');
    expect(positiveIntegerError('1.5', 'Capacity')).toContain('positive integer');
    expect(positiveIntegerError('9007199254740992', 'Capacity')).toContain('positive integer');
    expect(positiveIntegerError('8', 'Capacity')).toBeNull();
    expect(integerRangeError('101', 'Score', 0, 100)).toContain('0 to 100');
    expect(integerRangeError('0', 'Successes', 0, 100)).toBeNull();
  });

  it('validates currency, IANA time zones and HH:mm values', () => {
    expect(currencyError('cny')).not.toBeNull();
    expect(currencyError('CNY')).toBeNull();
    expect(timeZoneError('')).toContain('required');
    expect(timeZoneError('   ')).toContain('required');
    expect(timeZoneError('+08:00')).not.toBeNull();
    expect(timeZoneError('-05:00')).not.toBeNull();
    expect(timeZoneError('Not/AZone')).not.toBeNull();
    expect(timeZoneError('Asia/Shanghai')).toBeNull();
    expect(timeZoneError('America/New_York')).toBeNull();
    expect(timeZoneError(' UTC ')).toBeNull();
    expect(timeError('24:00', 'Start')).not.toBeNull();
    expect(timeError('9:00', 'Start')).not.toBeNull();
    expect(timeError('23:59', 'Start')).toBeNull();
  });

  it('requires actual text and valid JSON', () => {
    expect(requiredTextError('   ', 'Reason')).not.toBeNull();
    expect(requiredTextError('Evidence reviewed', 'Reason')).toBeNull();
    expect(jsonError('{bad', 'Evidence')).not.toBeNull();
    expect(jsonError('{"ref":"evidence-1"}', 'Evidence')).toBeNull();
  });

  it('rejects blank and normalized default acknowledgements', () => {
    expect(specificTextError(' ', 'Reason', ['Reviewed'])).toContain('required');
    expect(specificTextError(' reviewed. ', 'Reason', ['Reviewed'])).toContain('specific');
    expect(specificTextError(' 已审核。 ', '原因', ['已审核'])).toContain('specific');
    expect(specificTextError('Checked evidence-1 and rollback output.', 'Reason', ['Reviewed'])).toBeNull();
  });
});
