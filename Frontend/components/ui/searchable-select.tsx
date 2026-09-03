"use client";

import { useEffect, useId, useRef } from "react";

import { translate } from "@/lib/i18n/locale";

export type SearchableSelectOption = {
  label: string;
  value: string;
};

export function SearchableSelect({
  ariaLabel,
  className,
  disabled = false,
  emptyLabel,
  id,
  label,
  labelClassName,
  name,
  onChange,
  options,
  required = false,
  value,
}: {
  ariaLabel: string;
  className: string;
  disabled?: boolean;
  emptyLabel: string;
  id?: string;
  label?: string;
  labelClassName?: string;
  name?: string;
  onChange: (value: string) => void;
  options: SearchableSelectOption[];
  required?: boolean;
  value: string;
}) {
  const generatedId = useId();
  const controlId = id ?? generatedId;
  const listId = `${controlId}-options`;
  const inputRef = useRef<HTMLInputElement>(null);
  const selectedLabel = options.find((option) => option.value === value)?.label ?? value;

  useEffect(() => {
    if (!inputRef.current) return;
    inputRef.current.value = selectedLabel;
    inputRef.current.setCustomValidity("");
  }, [selectedLabel]);
  useEffect(() => {
    const query = inputRef.current?.value;
    const selected = options.find((option) => option.label === query);
    if (selected && selected.value !== value) {
      inputRef.current?.setCustomValidity("");
      onChange(selected.value);
    }
  }, [onChange, options, value]);

  return (
    <div className="grid gap-1.5">
      {label && (
        <label className={labelClassName} htmlFor={controlId}>
          {label}
        </label>
      )}
      <input
        aria-label={ariaLabel}
        className={className}
        disabled={disabled}
        id={controlId}
        list={listId}
        onChange={(event) => {
          const next = event.target.value;
          const selected = options.find((option) => option.label === next);
          event.target.setCustomValidity(next && !selected ? translate("Select an option from the list.") : "");
          if (!next || selected) onChange(selected?.value ?? "");
        }}
        placeholder={emptyLabel}
        ref={inputRef}
        required={required}
        type="search"
        defaultValue={selectedLabel}
      />
      {name && <input name={name} type="hidden" value={value} />}
      <datalist id={listId}>
        {options.map((option) => (
          <option key={option.value} label={option.value} value={option.label} />
        ))}
      </datalist>
    </div>
  );
}
