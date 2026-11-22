/** Selected tile's left edge is always exactly two slots from viewport left. */
export function carouselOffset(index: number, itemWidth: number, gap: number): number {
 return (2 - Math.max(0, index)) * (itemWidth + gap);
}
export const ITEM_WIDTH = 140;
export const ITEM_GAP = 30;
