import { describe, expect, it } from 'vitest';
import { carouselOffset, ITEM_WIDTH, ITEM_GAP } from './carousel';
describe('fixed left carousel anchor', () => {
 it.each([0,1,2,3,20,999])('anchors selected index %i exactly two slots from the left', index => {
  const slot = ITEM_WIDTH + ITEM_GAP;
  expect(index * slot + carouselOffset(index,ITEM_WIDTH,ITEM_GAP)).toBe(2 * slot);
 });
 it('treats an empty selection as the first slot', () => expect(carouselOffset(-1,140,30)).toBe(340));
 it('uses the same geometry after item sizing changes', () => expect(4*112+carouselOffset(4,100,12)).toBe(224));
});
