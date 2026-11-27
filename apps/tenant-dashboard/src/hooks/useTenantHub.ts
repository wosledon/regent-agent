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
    let stopped = false;
    let initialAttempts = 0;
    let retryTimer: ReturnType<typeof setTimeout> | null = null;
    connection.on('approval.changed', () => {
      void queryClient.invalidateQueries({ queryKey: ['dashboard'] });
      void queryClient.invalidateQueries({ queryKey: ['approvals'] });
    });
    connection.on('message.created', () => {
      void queryClient.invalidateQueries({ queryKey: ['conversations'] });
      void queryClient.invalidateQueries({ queryKey: ['messages'] });
      void queryClient.invalidateQueries({ queryKey: ['notifications'] });
      void queryClient.invalidateQueries({ queryKey: ['collaboration'] });
    });
    connection.on('coordination.changed', () => {
      void queryClient.invalidateQueries({ queryKey: ['collaboration'] });
    });
    connection.onreconnecting(() => setState('reconnecting'));
    connection.onreconnected(() => {
      initialAttempts = 0;
      setState('connected');
      void queryClient.invalidateQueries();
    });
    connection.onclose(() => { if (!stopped) setState('disconnected'); });
    const startInitialConnection = () => {
      initialAttempts += 1;
      void connection.start().then(() => {
        if (stopped) return;
        initialAttempts = 0;
        setState('connected');
      }).catch(() => {
        if (stopped) return;
        setState('disconnected');
        if (initialAttempts >= 4) return;
        retryTimer = setTimeout(startInitialConnection, 1000 * (2 ** (initialAttempts - 1)));
      });
    };
    const startTimer = setTimeout(startInitialConnection, 0);
    return () => {
      stopped = true;
      clearTimeout(startTimer);
      if (retryTimer) clearTimeout(retryTimer);
      void connection.stop();
      connectionRef.current = null;
    };
  }, [enabled, queryClient]);

  return state;
}
