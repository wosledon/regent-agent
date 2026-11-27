import { createContext, useContext } from 'react';

export interface FormFieldContextValue {
  controlId: string;
  labelId: string;
  descriptionId?: string;
  label: string;
  invalid: boolean;
  required: boolean;
  disabled: boolean;
}

export const FormFieldContext = createContext<FormFieldContextValue | null>(null);

export function useFormFieldContext() {
  return useContext(FormFieldContext);
}
