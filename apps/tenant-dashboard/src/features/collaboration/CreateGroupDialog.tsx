import { useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Autocomplete, Chip, Dialog, DialogActions, DialogContent, DialogTitle, TextField } from '@mui/material';
import { Button, ErrorState, FormField, Select, Stack, TextInput } from '@regent/ui-shared';
import { api } from '../../lib/api-client/api';

interface CreateGroupDialogProps {
  open: boolean;
  employees: Awaited<ReturnType<typeof api.employees>>;
  onClose: () => void;
  onCreated: (conversationId: string) => void;
}

export function CreateGroupDialog({ open, employees, onClose, onCreated }: CreateGroupDialogProps) {
  const queryClient = useQueryClient();
  const [title, setTitle] = useState('');
  const [kind, setKind] = useState('Project');
  const [scopeType, setScopeType] = useState('Project');
  const [members, setMembers] = useState<Awaited<ReturnType<typeof api.employees>>>([]);
  const create = useMutation({
    mutationFn: () => api.createConversation({
      title,
      kind,
      memberEmployeeIds: members.map(member => member.id),
      scopeType
    }),
    onSuccess: async conversation => {
      setTitle('');
      setMembers([]);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['collaboration'] }),
        queryClient.invalidateQueries({ queryKey: ['conversations'] })
      ]);
      onCreated(conversation.id);
      onClose();
    }
  });
  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>Create collaboration group</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ pt: 1 }}>
          <FormField label="Group name"><TextInput value={title} onChange={setTitle} placeholder="Release war room" /></FormField>
          <Select label="Group type" value={kind} onChange={setKind} options={['Project', 'Position', 'Topic', 'Direct'].map(value => ({ value, label: value }))} />
          <Select label="Scope" value={scopeType} onChange={setScopeType} options={['Project', 'Position', 'Topic', 'Company'].map(value => ({ value, label: value }))} />
          <FormField label="Members" hint="Select employees who can see and contribute to this group.">
            <Autocomplete
              multiple
              options={employees}
              value={members}
              getOptionLabel={option => option.displayName}
              onChange={(_, value) => setMembers(value)}
              renderInput={params => <TextField {...params} label="Employees" placeholder="Add members" />}
              renderTags={(value, getTagProps) => value.map((option, index) => (
                <Chip label={option.displayName} size="small" {...getTagProps({ index })} />
              ))}
            />
          </FormField>
          {create.error ? <ErrorState message={create.error.message} /> : null}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button tone="neutral" onClick={onClose}>Cancel</Button>
        <Button onClick={() => create.mutate()} disabled={!title.trim() || !members.length} loading={create.isPending}>Create group</Button>
      </DialogActions>
    </Dialog>
  );
}
