import { ROLES } from './constants';

/**
 * Which roles are allowed to walk straight in through each portal.
 *
 * Choosing a portal is a statement of who you are signing in as. Before this existed, every
 * login page treated "somebody is already signed in" as reason enough to let them through,
 * whoever they were — so on a shared cafe PC, clicking OPERATOR after a Super Admin had used
 * the machine dropped you into the Super Admin dashboard, with every branch, every rupee and
 * every setting, without typing a password.
 *
 * A session whose role is not listed for the portal being opened is discarded rather than
 * honoured.
 */
export const PORTAL_ROLES = {
  // The operator counter. Admins work these screens too — the page has a tab for them.
  // A Super Admin is deliberately excluded: to use the operator screens they sign in as an
  // operator, or use Admin Quick-Switch, both of which record who is actually at the counter.
  operator: [ROLES.OPERATOR, ROLES.ADMIN],

  // Multi-branch management. A Super Admin outranks it, so is allowed.
  admin: [ROLES.ADMIN, ROLES.SUPER_ADMIN],

  superadmin: [ROLES.SUPER_ADMIN],
};

/** Normalises the role off a user object, which arrives as `role` or `Role`. */
export function roleOf(user) {
  return (user?.role || user?.Role || '').toLowerCase();
}

/** True when this session belongs on the given portal and may skip the login form. */
export function sessionBelongsOnPortal(user, portal) {
  const role = roleOf(user);
  if (!role) return false;

  const allowed = PORTAL_ROLES[portal];
  if (!allowed) return false;

  return allowed.some((r) => r.toLowerCase() === role);
}
