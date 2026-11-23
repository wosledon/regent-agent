import { useEffect, useRef, useState } from 'react';
import { HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { useQueryClient } from '@tanstack/react-query';

export type HubState = 'connected' | 'reconnecting' | 'disconnected';

export function useTenantHub(enabled: boolean) {
  const queryClient = useQueryClient();
  const connectionRef = useRef<HubConnection | null>(null);
  const [state, setState] = useState<HubState>('disconnected');

  useEffect(() => {
    if (!enabled) return;
    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/tenant', { withCredentials: true })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();
    connectionRef.current = connection;
    connection.on('approval.changed', () => {
      void queryClient.invalidateQueries({ queryKey: ['dashboard'] });
      void queryClient.invalidateQueries({ queryKey: ['approvals'] });
    });
    connection.on('message.created', () => {
      void queryClient.invalidateQueries({ queryKey: ['messages'] });
      void queryClient.invalidateQueries({ queryKey: ['notifications'] });
    });
    connection.onreconnecting(() => setState('reconnecting'));
    connection.onreconnected(() => {
      setState('connected');
      void queryClient.invalidateQueries();
    });
    connection.onclose(() => setState('disconnected'));
    void connection.start().then(() => setState('connected')).catch(() => setState('disconnected'));
    return () => {
      void connection.stop();
      connectionRef.current = null;
    };
  }, [enabled, queryClient]);

  return state;
}
