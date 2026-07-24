// Mirrors AppleEsportsErp.Application.Services.SessionPricingCalculator (backend) so every
// live/ticking display (PC cards, customer overlay) always agrees with what the finalized
// bill will show — no live number is ever a different shape than the final rounded total.

// Rounds a total to the nearest ₹10 — down for a remainder of 0-5, up for 6-9.
export function roundBillTotal(amount) {
  const n = Number(amount) || 0;
  if (n <= 0) return 0;

  const remainder = n % 10;
  return remainder <= 5 ? n - remainder : n + (10 - remainder);
}

// Rounds the total AND folds the adjustment into the Gaming figure (a derived, not a fixed
// menu price) so the displayed Gaming charge + Food charge always sums to the displayed
// Total exactly. Food stays exact; if Gaming is 0 (still free/under-buffer), Food absorbs
// the tiny adjustment instead so a free session never appears to have a Gaming charge.
export function computeRoundedBreakdown(gamingAmount, foodAmount, discountAmount = 0) {
  const gaming = Number(gamingAmount) || 0;
  const food = Number(foodAmount) || 0;
  const discount = Number(discountAmount) || 0;

  const rawTotal = Math.max(0, gaming + food - discount);
  const roundedTotal = roundBillTotal(rawTotal);
  const diff = roundedTotal - rawTotal;

  let displayGaming = gaming;
  let displayFood = food;

  if (gaming > 0) {
    displayGaming = Math.max(0, gaming + diff);
  } else if (food > 0) {
    displayFood = Math.max(0, food + diff);
  }

  return { displayGaming, displayFood, roundedTotal };
}
