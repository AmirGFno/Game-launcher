import { beforeAll, describe, expect, it, vi } from 'vitest';
let validResponse: typeof import('./bridge').validResponse;
beforeAll(async () => { vi.stubGlobal('window', {}); validResponse = (await import('./bridge')).validResponse; });
describe('host response validation', () => {
 const response = { version:1, source:'gfno-host', type:'library.get.result',requestId:'r1',success:true };
 it('accepts correlated host responses', () => expect(validResponse(response,'r1','library.get')).toBe(true));
 it.each([null, {}, {...response,version:2}, {...response,source:'gfno-app'}, {...response,requestId:'other'}, {...response,type:'game.launch.result'}, {...response,success:'true'}])('rejects invalid or unrelated messages', value => expect(validResponse(value,'r1','library.get')).toBe(false));
 it('rejects desktop actions in browser preview', async () => { const {request} = await import('./bridge'); await expect(request('library.get')).rejects.toThrow('Windows desktop app'); });
});
