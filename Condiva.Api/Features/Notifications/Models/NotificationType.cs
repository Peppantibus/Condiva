namespace Condiva.Api.Features.Notifications.Models;

public enum NotificationType
{
    OfferReceivedToRequester,
    OfferAcceptedToLender,
    OfferRejectedToLender,
    OfferWithdrawnToRequester,
    LoanReservedToBorrower,
    LoanReservedToLender,
    LoanStartedToBorrower,
    LoanReturnRequestedToLender,
    LoanReturnConfirmedToBorrower,
    LoanReturnConfirmedToLender,
    LoanReturnCanceledToLender
}
