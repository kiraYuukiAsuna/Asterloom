"use client";

import { SearchableSelect, type SearchableSelectOption } from "./searchable-select";

export function SearchableMultiSelect({
  ariaLabel,
  className,
  disabled = false,
  emptyLabel,
  label,
  labelClassName,
  onChange,
  options,
  required = false,
  value,
}: {
  ariaLabel: string;
  className: string;
  disabled?: boolean;
  emptyLabel: string;
  label: string;
  labelClassName?: string;
  onChange: (value: string[]) => void;
  options: SearchableSelectOption[];
  required?: boolean;
  value: string[];
}) {
  const selected = value.map(
    (item) => options.find((option) => option.value === item) ?? { label: item, value: item },
  );

  return (
    <div className="grid gap-2">
      <span className={labelClassName}>{label}</span>
      {selected.length > 0 && (
        <div className="flex flex-wrap gap-2">
          {selected.map((option) => (
            <span
              className="inline-flex items-center gap-1.5 rounded-md border border-white/10 bg-white/[0.04] px-2 py-1 text-xs text-slate-300"
              key={option.value}
            >
              {option.label}
              <button
                aria-label={`Remove ${option.label}`}
                className="text-slate-500 hover:text-white"
                disabled={disabled}
                onClick={() => onChange(value.filter((item) => item !== option.value))}
                type="button"
              >
                ×
              </button>
            </span>
          ))}
        </div>
      )}
      <SearchableSelect
        ariaLabel={ariaLabel}
        className={className}
        disabled={disabled || (options.length > 0 && selected.length === options.length)}
        emptyLabel={emptyLabel}
        key={value.join("\0")}
        onChange={(item) => item && onChange([...value, item])}
        options={options.filter((option) => !value.includes(option.value))}
        required={required && value.length === 0}
        value=""
      />
    </div>
  );
}
