import api from '../config/api';

// Head Office cannot start or stop play by writing straight into its own database — a session
// created there is invisible to the counter that would have to bill it. The API enforces this
// (400, code BRANCH_ONLY_OPERATION) rather than failing silently. This module is the other half:
// when that refusal happens, send the same action down as a command for the branch to carry out
// on its own database instead, then wait for the branch to say it's done.

const POLL_MS = 1000;
const TIMEOUT_MS = 30000;

function sleep(ms) {
  return new Promise(resolve => setTimeout(resolve, ms));
}

async function pollUntilSettled(commandId) {
  const deadline = Date.now() + TIMEOUT_MS;
  while (Date.now() < deadline) {
    const { data } = await api.get(`/branch-commands/${commandId}`);
    const command = data.data;
    if (command.status === 'Confirmed') return command;
    if (command.status === 'Failed') {
      throw { response: { data: { error: command.resultMessage || 'The branch could not carry this out.' } } };
    }
    await sleep(POLL_MS);
  }
  throw { response: { data: { error: 'Timed out waiting for the branch to respond.' } } };
}

async function issueAndAwait(branchId, pcId, type, payload) {
  const { data } = await api.post('/branch-commands', { branchId, pcId, type, payload });
  return pollUntilSettled(data.data.id);
}

const isBranchOnlyRefusal = (err) => err?.response?.data?.code === 'BRANCH_ONLY_OPERATION';

// Starts a session directly; if Head Office refuses because this is a branch-only operation,
// sends the same start down as a command and waits for the branch to confirm it.
export async function startSessionRemoteAware(branchId, pcId, startBody) {
  try {
    const { data } = await api.post('/sessions/start', startBody);
    return data.data;
  } catch (err) {
    if (!isBranchOnlyRefusal(err)) throw err;
    const command = await issueAndAwait(branchId, pcId, 'StartSession', {
      customerName: startBody.customerName,
      memberId: startBody.memberId,
      durationMinutes: startBody.durationMinutes,
      packageName: startBody.packageName,
      expectedAmount: startBody.expectedAmount,
    });
    return { id: command.resultSessionId };
  }
}

// Stops a session directly; if Head Office refuses, sends the same stop down as a command and
// waits for the branch to confirm it.
export async function stopSessionRemoteAware(branchId, pcId, sessionId, payload = {}) {
  try {
    await api.post(`/sessions/${sessionId}/${'stop'}`, payload);
    return;
  } catch (err) {
    if (!isBranchOnlyRefusal(err)) throw err;
    await issueAndAwait(branchId, pcId, 'StopSession', {
      sessionId,
      deferPayment: !!payload.deferPayment,
    });
  }
}
