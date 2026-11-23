import type { ComponentType } from 'react';

export type ComponentCategory = 'mui' | 'layout' | 'data' | 'form-feedback' | 'tenant' | 'operations' | 'project-employee';
export type ComponentStatus = 'stable' | 'candidate' | 'deprecated';

export interface ComponentRegistryEntry {
  id: string;
  name: string;
  category: ComponentCategory;
  description: string;
  tags: string[];
  status: ComponentStatus;
  demo: ComponentType;
}
