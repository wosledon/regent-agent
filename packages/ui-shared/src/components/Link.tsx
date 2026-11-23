import type { LinkProps } from '@mui/material';
import { Link as MuiLink } from '@mui/material';

export interface SemanticLinkProps extends LinkProps {
  href: string;
}

export function SemanticLink({ href, children, ...props }: SemanticLinkProps) {
  return <MuiLink href={href} underline="hover" {...props}>{children}</MuiLink>;
}
