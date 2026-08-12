namespace AppleEsportsErp.Application.Interfaces;

public interface IHubNotificationService
{
    Task BroadcastPcStatusChangeAsync(Guid branchId, Guid pcId);
    Task BroadcastSessionUpdateAsync(Guid branchId, Guid sessionId);
    Task BroadcastReservationUpdateAsync(Guid branchId, Guid reservationId);
    Task BroadcastBillingUpdateAsync(Guid branchId, Guid billId);
    Task BroadcastFoodOrderUpdateAsync(Guid branchId, Guid orderId);
    Task BroadcastCashRegisterUpdateAsync(Guid branchId, Guid registerId);
    Task BroadcastPcManagementUpdateAsync(Guid branchId, Guid pcId, string action);
    Task BroadcastPricingProfileUpdateAsync(Guid branchId);
    Task SendUnlockCommandToAgentAsync(Guid pcId, int durationMinutes, string? customerName);
    Task SendLockCommandToAgentAsync(Guid pcId);

    /// <summary>Warns the member at this PC that their balance is nearly used up.</summary>
    Task SendWalletRunningOutToAgentAsync(Guid pcId, int minutesLeft, decimal balance);

    /// <summary>
    /// Tells the member their balance is finished and the session has ended. Sent before the
    /// session is stopped, so the explanation is on screen before the PC locks — otherwise the
    /// machine simply goes dead in front of them and looks broken.
    /// </summary>
    Task SendWalletFinishedToAgentAsync(Guid pcId);
    Task TriggerDashboardRefreshAsync();
}
