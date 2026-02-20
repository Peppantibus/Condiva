using System;
using System.Collections.Generic;
using Condiva.Api.Features.Items.Models;
using Condiva.Api.Features.Loans.Models;
using Condiva.Api.Features.Memberships.Models;
using Condiva.Api.Features.Offers.Models;
using Condiva.Api.Features.Requests.Models;

namespace Condiva.Api.Common.Auth;

public static class AllowedActionsPolicy
{
    public static bool IsManager(MembershipRole role)
    {
        return MembershipRolePolicy.CanModerateContent(role);
    }

    public static string[] ForCommunity(MembershipRole actorRole)
    {
        var actions = NewActions();

        if (MembershipRolePolicy.CanManageInvites(actorRole))
        {
            actions.Add("viewInviteCode");
            actions.Add("manageInvites");
        }

        if (MembershipRolePolicy.CanManageMembers(actorRole))
        {
            actions.Add("manageMembers");
        }

        if (MembershipRolePolicy.CanManageCommunitySettings(actorRole))
        {
            actions.Add("update");
            actions.Add("delete");
            actions.Add("rotateInviteCode");
            actions.Add("manageImage");
        }

        return actions.ToArray();
    }

    public static string[] ForMembership(
        Membership membership,
        string actorUserId,
        MembershipRole actorRole)
    {
        var actions = NewActions();
        var isSelf = string.Equals(membership.UserId, actorUserId, StringComparison.Ordinal);

        if (isSelf && membership.Status == MembershipStatus.Active)
        {
            actions.Add("leave");
        }

        if (MembershipRolePolicy.CanManageMembers(actorRole))
        {
            actions.Add("update");
            if (!isSelf)
            {
                actions.Add("remove");
                actions.Add("updateRole");
            }
        }

        return actions.ToArray();
    }

    public static string[] ForItem(
        Item item,
        string actorUserId,
        MembershipRole actorRole)
    {
        var actions = NewActions();
        var canManage = IsManager(actorRole)
            || string.Equals(item.OwnerUserId, actorUserId, StringComparison.Ordinal);

        if (canManage)
        {
            actions.Add("manageImage");
        }

        if (canManage && item.Status is not ItemStatus.Reserved and not ItemStatus.InLoan)
        {
            actions.Add("update");
            actions.Add("delete");
        }

        if (item.Status == ItemStatus.Available
            && !string.Equals(item.OwnerUserId, actorUserId, StringComparison.Ordinal))
        {
            actions.Add("createOffer");
        }

        return actions.ToArray();
    }

    public static string[] ForRequest(
        Request request,
        string actorUserId,
        MembershipRole actorRole)
    {
        var actions = NewActions();
        var canManage = IsManager(actorRole)
            || string.Equals(request.RequesterUserId, actorUserId, StringComparison.Ordinal);

        if (canManage && request.Status == RequestStatus.Open)
        {
            actions.Add("update");
            actions.Add("delete");
            actions.Add("manageImage");
        }

        if (request.Status == RequestStatus.Open
            && !string.Equals(request.RequesterUserId, actorUserId, StringComparison.Ordinal))
        {
            actions.Add("createOffer");
        }

        return actions.ToArray();
    }

    public static string[] ForOffer(
        Offer offer,
        string actorUserId,
        MembershipRole actorRole,
        bool isRequestOwner)
    {
        var actions = NewActions();
        var isOfferer = string.Equals(offer.OffererUserId, actorUserId, StringComparison.Ordinal);
        var canManage = IsManager(actorRole) || isOfferer;

        if (offer.Status == OfferStatus.Open && canManage)
        {
            actions.Add("update");
            actions.Add("delete");
        }

        if (offer.Status == OfferStatus.Open && isOfferer)
        {
            actions.Add("withdraw");
        }

        if (offer.Status == OfferStatus.Open && isRequestOwner)
        {
            actions.Add("accept");
            actions.Add("reject");
        }

        return actions.ToArray();
    }

    public static string[] ForLoan(
        Loan loan,
        string actorUserId,
        MembershipRole actorRole)
    {
        var actions = NewActions();
        var isLender = string.Equals(loan.LenderUserId, actorUserId, StringComparison.Ordinal);
        var isBorrower = string.Equals(loan.BorrowerUserId, actorUserId, StringComparison.Ordinal);
        var isManager = IsManager(actorRole);

        if (loan.Status == LoanStatus.Reserved && (isLender || isBorrower || isManager))
        {
            actions.Add("start");
            actions.Add("update");
            actions.Add("delete");
        }

        if (loan.Status == LoanStatus.InLoan && (isBorrower || isManager))
        {
            actions.Add("returnRequest");
        }

        if (loan.Status == LoanStatus.ReturnRequested)
        {
            if (isBorrower || isManager)
            {
                actions.Add("returnCancel");
            }
            if (isLender || isManager)
            {
                actions.Add("returnConfirm");
            }
        }

        return actions.ToArray();
    }

    private static HashSet<string> NewActions()
    {
        return new HashSet<string>(StringComparer.Ordinal) { "view" };
    }
}
