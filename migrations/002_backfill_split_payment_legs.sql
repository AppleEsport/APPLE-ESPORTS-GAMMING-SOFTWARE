-- ═══════════════════════════════════════════════════════════════════════════
-- Backfill: bills whose payment legs were lost in transit to Head Office
--
-- RUN AT HEAD OFFICE ONLY. Never at a branch — a branch's own bills were always
-- correct; it is only Head Office's copy that was rebuilt from an incomplete
-- sync event.
--
-- Why these rows are wrong
-- ------------------------
-- The branch's `bill.paid` sync event carried only `cashAmount`. Head Office
-- reconstructed the rest as `totalPaid - cash` and assigned that remainder
-- wholly to Online or wholly to Wallet based on the payment type — which cannot
-- represent a Split at all. Worse, the Bill row it inserted was never given any
-- of the legs, so they sat at zero while the Payment row beside them carried the
-- real figures.
--
-- Result: two rows in one database disagreeing about the same money, and every
-- report that reads the bill rather than the payment reading the wrong one.
--
-- Fixed forward in BillingService.ProcessPaymentAsync (the event now carries
-- every leg) and SyncInboxController.RecordPaymentAsync (it now reads them, and
-- writes them onto the Bill). This script repairs what already landed.
--
-- Why `payments` is treated as the truth
-- --------------------------------------
-- It is the row that was closest to being right: it at least carried a split's
-- two halves, because RecordPaymentAsync computed a non-cash remainder for it.
-- The Bill's legs were never written at all. Where they disagree, the payment
-- row is the better record — and on a branch's own database the two always
-- agreed anyway, so this converges Head Office onto what the branch has.
--
-- Bills with more than one payment row are summed, so a part-paid bill settles
-- to the total actually collected rather than to whichever leg sorted first.
--
-- Safety
-- ------
--   * Wrapped in a transaction. Inspect the SELECT output before COMMIT.
--   * Touches only bills that currently disagree with their own payments.
--   * Does not change TotalAmount, Status, or any date — only the split of an
--     amount that was already collected.
-- ═══════════════════════════════════════════════════════════════════════════

BEGIN;

-- 1. What is about to change. Read this before committing.
SELECT b."BillNumber",
       br."Name"                                   AS branch,
       b."PaymentType"                             AS bill_says,
       p.payment_type                              AS payment_says,
       b."CashAmount"    AS cash_before,   p.cash    AS cash_after,
       b."OnlineAmount"  AS online_before, p.online  AS online_after,
       b."WalletAmount"  AS wallet_before, p.wallet  AS wallet_after
FROM bills b
JOIN branches br ON br."Id" = b."BranchId"
JOIN (
    SELECT "BillId",
           sum("CashAmount")           AS cash,
           sum("OnlineAmount")         AS online,
           sum("WalletAmount")         AS wallet,
           sum("ActualCashCollected")  AS actual_cash,
           min("PaymentType")          AS payment_type
    FROM payments
    GROUP BY "BillId"
) p ON p."BillId" = b."Id"
WHERE b."CashAmount"   IS DISTINCT FROM p.cash
   OR b."OnlineAmount" IS DISTINCT FROM p.online
   OR b."WalletAmount" IS DISTINCT FROM p.wallet
ORDER BY br."Name", b."BillNumber";

-- 2. The repair.
UPDATE bills b
SET "CashAmount"          = p.cash,
    "OnlineAmount"        = p.online,
    "WalletAmount"        = p.wallet,
    "ActualCashCollected" = p.actual_cash,
    "PaymentType"         = p.payment_type,
    "UpdatedAt"           = now()
FROM (
    SELECT "BillId",
           sum("CashAmount")           AS cash,
           sum("OnlineAmount")         AS online,
           sum("WalletAmount")         AS wallet,
           sum("ActualCashCollected")  AS actual_cash,
           min("PaymentType")          AS payment_type
    FROM payments
    GROUP BY "BillId"
) p
WHERE p."BillId" = b."Id"
  AND (b."CashAmount"   IS DISTINCT FROM p.cash
    OR b."OnlineAmount" IS DISTINCT FROM p.online
    OR b."WalletAmount" IS DISTINCT FROM p.wallet);

-- 3. Should return zero rows.
SELECT count(*) AS still_disagreeing
FROM bills b
JOIN (
    SELECT "BillId", sum("CashAmount") AS cash, sum("OnlineAmount") AS online,
           sum("WalletAmount") AS wallet
    FROM payments GROUP BY "BillId"
) p ON p."BillId" = b."Id"
WHERE b."CashAmount"   IS DISTINCT FROM p.cash
   OR b."OnlineAmount" IS DISTINCT FROM p.online
   OR b."WalletAmount" IS DISTINCT FROM p.wallet;

-- COMMIT;    -- uncomment once the output above has been read
ROLLBACK;     -- default: change nothing
