export type Game = { id: string; title: string; launchTarget: string; logoPath?: string; backgroundPath?: string; lastPlayed?: string; isFavorite: boolean; category?: string; launchProfile: { workingDirectory: string; commandLineArguments: string; processPriority: string; processNameForMonitoring?: string; monitoringEnabled: boolean; safeStartupPreparationEnabled: boolean; preLaunchApplications: string[] } };
export type Preferences = { theme: string; accentColor: string; animationIntensity: string; animatedBackgrounds: boolean; monitoringEnabled: boolean; minimizeToTray: boolean; startWithWindows: boolean; samplingIntervalMs: number; [key: string]: unknown };
export type PerformanceStatus = {
 snapshot: {totalCpuPercent: number; totalMemoryBytes: number; availableMemoryBytes: number};
 session: {gameTitle: string; state: number; processId?: number; launchRequestedAtUtc?: string; gameProcessDetectedAtUtc?: string; sessionDuration?: string};
 sample?: {cpuPercent: number; workingSetBytes: number; processName: string; sessionDuration: string};
 recommendations: {code: string; title: string; explanation: string}[];
 history: {gameTitle: string; launchedAtUtc: string; duration?: string; success: boolean; error?: string}[];
};
type Contracts = {
 'settings.get': {settings: Preferences}; 'settings.save': {settings: Preferences}; 'performance.status': PerformanceStatus;
 'image.download': { path: string }; 'game.detect': {title: string; logoPath?: string};
 'library.reorder': { games: Game[] };
 'library.get': { games: Game[] }; 'library.create': { games: Game[] }; 'library.update': { games: Game[] }; 'library.delete': { games: Game[] };
 'game.launch': unknown; 'game.openFolder': unknown; 'dialog.openFile': { path: string | null }; 'image.thumbnail': { url: string | null };
 'window.minimize': unknown; 'window.maximize': unknown; 'window.close': unknown;
};
type WebView = { postMessage(message: unknown): void; addEventListener(type: 'message', callback: (event: MessageEvent) => void): void };
declare global { interface Window { chrome?: { webview?: WebView } } }
export const desktop = !!window.chrome?.webview;
export function validResponse(value: unknown, id: string, type: string): value is { requestId: string; success: boolean; payload?: unknown; error?: string } {
 if (!value || typeof value !== 'object') return false;
 const v = value as Record<string, unknown>;
 return v.version === 1 && v.source === 'gfno-host' && v.requestId === id && v.type === `${type}.result` && typeof v.success === 'boolean';
}
const pending = new Map<string, {type: string; resolve: (value: unknown) => void; reject: (reason: Error) => void; timer: ReturnType<typeof setTimeout>}>();
window.chrome?.webview?.addEventListener('message', event => {
 const v = event.data;
 if (!v || typeof v.requestId !== 'string') return;
 const request = pending.get(v.requestId);
 if (!request || !validResponse(v, v.requestId, request.type)) return;
 clearTimeout(request.timer); pending.delete(v.requestId);
 if (v.success) request.resolve(v.payload); else request.reject(new Error(v.error || 'The operation failed.'));
});
export function request<K extends keyof Contracts>(type: K, payload: object = {}): Promise<Contracts[K]> {
 if (!window.chrome?.webview) return Promise.reject(new Error('This action requires the Windows desktop app. Browser preview does not access your library.'));
 return new Promise((resolve, reject) => {
  const requestId = crypto.randomUUID();
  const timer = setTimeout(() => { pending.delete(requestId); reject(new Error('The desktop request timed out.')); }, 30000);
  pending.set(requestId, { type, resolve: value => resolve(value as Contracts[K]), reject, timer });
  window.chrome!.webview!.postMessage({version: 1, source: 'gfno-app', type, requestId, payload});
 });
}
