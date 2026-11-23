import type { IconButtonProps } from '@mui/material';
import { IconButton as MuiIconButton } from '@mui/material';

export interface SemanticIconButtonProps extends IconButtonProps {
  label: string;
}

export function SemanticIconButton({ label, children, ...props }: SemanticIconButtonProps) {
  return <MuiIconButton aria-label={label} {...props}>{children}</MuiIconButton>;
}
