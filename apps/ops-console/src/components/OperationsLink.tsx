import type { MouseEvent, ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import { SemanticLink } from '@regent/ui-shared';

export interface OperationsLinkProps {
  to: string;
  children: ReactNode;
}

export function OperationsLink({ to, children }: OperationsLinkProps) {
  const navigate = useNavigate();
  const handleClick = (event: MouseEvent<HTMLAnchorElement>) => {
    if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
    event.preventDefault();
    navigate(to);
  };

  return <SemanticLink href={to} onClick={handleClick}>{children}</SemanticLink>;
}
